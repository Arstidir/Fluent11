using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class SystemRequirements
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly Guid SteamPluginId = Guid.Parse("cb91dfc9-b977-43bf-8e70-55f46e410fab");
        private static readonly Regex SteamLink = new Regex(@"store\.steampowered\.com/app/(\d+)", RegexOptions.IgnoreCase);
        private static readonly TimeSpan SelectDelay = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan NoDataRetry = TimeSpan.FromDays(30);
        private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan UnexpectedPause = TimeSpan.FromHours(1);

        private static Func<Fluent11HelperSettings> getSettings;
        private static Fluent11HelperSettings settings => getSettings();
        private static string cacheDir;
        private static DispatcherTimer timer;
        private static string pendingAppId;
        private static int generation;
        private static DateTime pausedUntil = DateTime.MinValue;
        private static readonly SemaphoreSlim oneAtATime = new SemaphoreSlim(1, 1);

        public static void Register(string dataPath, Func<Fluent11HelperSettings> helperSettings)
        {
            getSettings = helperSettings;
            cacheDir = Path.Combine(dataPath, "SystemRequirements");
        }

        public static void OnGameSelected(Game game)
        {
            generation++;
            timer?.Stop();
            var appId = settings.SystemRequirementsCard ? SteamAppId(game) : null;
            if (appId == null)
            {
                Publish(null);
                return;
            }

            var cached = ReadCache(appId);
            Publish(cached != null ? Parse(cached) : null);
            if (cached != null && (cached.HasData || DateTime.UtcNow - cached.FetchedUtc < NoDataRetry))
            {
                return;
            }

            pendingAppId = appId;
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = SelectDelay };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Fetch(pendingAppId, generation);
                };
            }

            timer.Start();
        }

        public static void Reload(IEnumerable<Game> games, IEnumerable<Game> selected)
        {
            foreach (var appId in games.Select(SteamAppId).Where(a => a != null).Distinct())
            {
                try
                {
                    File.Delete(Path.Combine(cacheDir, appId + ".json"));
                }
                catch (Exception e)
                {
                    logger.Warn(e, $"Fluent 11: could not delete stored system requirements for Steam app {appId}.");
                }
            }

            var current = selected?.ToList();
            if (current?.Count == 1 && games.Any(g => g.Id == current[0].Id))
            {
                OnGameSelected(current[0]);
            }
        }

        internal static string SteamAppId(Game game)
        {
            if (game == null)
            {
                return null;
            }

            if (game.PluginId == SteamPluginId && Regex.IsMatch(game.GameId ?? string.Empty, @"^\d+$"))
            {
                return game.GameId;
            }

            return game.Links?.Select(a => SteamLink.Match(a?.Url ?? string.Empty)).FirstOrDefault(a => a.Success)?.Groups[1].Value;
        }

        private static void Fetch(string appId, int requestGeneration)
        {
            if (DateTime.UtcNow < pausedUntil)
            {
                return;
            }

            Task.Run(async () =>
            {
                await oneAtATime.WaitAsync();
                try
                {
                    var entry = Download(appId);
                    if (entry == null)
                    {
                        return;
                    }

                    WriteCache(appId, entry);
                    var view = Parse(entry);
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (requestGeneration == generation)
                        {
                            Publish(view);
                        }
                    }));
                }
                catch (Exception e)
                {
                    logger.Warn(e, $"Fluent 11: failed to get system requirements for Steam app {appId}.");
                }
                finally
                {
                    oneAtATime.Release();
                }
            });
        }

        private static CacheEntry Download(string appId)
        {
            string json;
            try
            {
                using (var client = new SteamWebClient())
                {
                    json = client.DownloadString($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english");
                }

                if (SteamWebClient.IsUnexpected(json))
                {
                    pausedUntil = DateTime.UtcNow + UnexpectedPause;
                    logger.Warn("Fluent 11: unexpected answer from Steam's store API (appdetails), pausing system requirement requests for an hour.");
                    return null;
                }
            }
            catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == (HttpStatusCode)429)
            {
                pausedUntil = DateTime.UtcNow + RateLimitPause;
                logger.Info("Fluent 11: Steam asks to slow down, pausing system requirement requests.");
                return null;
            }
            catch (WebException)
            {
                return null;
            }

            var entry = new CacheEntry { FetchedUtc = DateTime.UtcNow };
            try
            {
                var response = Serialization.FromJson<Dictionary<string, AppDetailsResponse>>(json);
                if (response != null && response.TryGetValue(appId, out var app) && app?.success == true)
                {
                    entry.Minimum = app.data?.pc_requirements?.minimum;
                    entry.Recommended = app.data?.pc_requirements?.recommended;
                }
            }
            catch (Exception)
            {
            }

            return entry;
        }

        private static CacheEntry ReadCache(string appId)
        {
            try
            {
                var path = Path.Combine(cacheDir, appId + ".json");
                return File.Exists(path) ? Serialization.FromJsonFile<CacheEntry>(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteCache(string appId, CacheEntry entry)
        {
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(Path.Combine(cacheDir, appId + ".json"), Serialization.ToJson(entry));
        }

        private static void Publish(RequirementsView view)
        {
            var target = settings;
            if (target != null)
            {
                target.Requirements = view;
            }
        }

        private static RequirementsView Parse(CacheEntry entry)
        {
            var minimum = ParseColumn(entry.Minimum);
            var recommended = ParseColumn(entry.Recommended);
            if (minimum.Count == 0 && recommended.Count == 0)
            {
                return null;
            }

            return new RequirementsView
            {
                Minimum = minimum,
                Recommended = recommended,
                Rows = Pair(minimum, recommended),
                RequiredSpace = StorageOf(recommended) ?? StorageOf(minimum)
            };
        }

        private static List<RequirementRow> Pair(List<RequirementLine> minimum, List<RequirementLine> recommended)
        {
            if (minimum.Count == 0)
            {
                return recommended.Select(a => Row(a.Label, a.Value, null)).ToList();
            }

            var left = recommended.ToList();
            var rows = new List<RequirementRow>();
            foreach (var line in minimum)
            {
                var match = left.FirstOrDefault(a => line.HasLabel
                    ? string.Equals(a.Label, line.Label, StringComparison.OrdinalIgnoreCase)
                    : !a.HasLabel);
                if (match != null)
                {
                    left.Remove(match);
                }

                rows.Add(Row(line.Label, line.Value, match?.Value));
            }

            rows.AddRange(left.Select(a => Row(a.Label, null, a.Value)));
            return rows;
        }

        private static RequirementRow Row(string label, string minimum, string recommended)
        {
            return new RequirementRow { Label = label, Minimum = minimum ?? string.Empty, Recommended = recommended ?? string.Empty };
        }

        private static List<RequirementLine> ParseColumn(string html)
        {
            var lines = new List<RequirementLine>();
            if (string.IsNullOrWhiteSpace(html))
            {
                return lines;
            }

            var items = Regex.Matches(html, @"<li[^>]*>(.*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Cast<Match>().Select(a => a.Groups[1].Value).ToList();
            if (items.Count == 0)
            {
                var body = Regex.Replace(html, @"^\s*<strong>[^<]*</strong>", string.Empty, RegexOptions.IgnoreCase);
                items = Regex.Split(body, @"<br\s*/?>", RegexOptions.IgnoreCase).ToList();
            }

            foreach (var item in items)
            {
                var label = string.Empty;
                var value = item;
                var strong = Regex.Match(item, @"^\s*<strong>(.*?)</strong>(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (strong.Success)
                {
                    label = Text(strong.Groups[1].Value).TrimEnd(':', '*', ' ');
                    value = strong.Groups[2].Value;
                }

                value = Shorten(label, Text(value));
                if (value.Length == 0 || value.StartsWith("Requires a 64-bit processor and operating system", StringComparison.OrdinalIgnoreCase)
                    || HiddenLabel.IsMatch(label))
                {
                    continue;
                }

                lines.Add(new RequirementLine { Label = label, Value = value });
            }

            return lines;
        }

        private static readonly Regex HiddenLabel = new Regex(@"^(Additional Notes|Sound Card|Sound)$", RegexOptions.IgnoreCase);

        private static readonly Regex TrademarkSigns = new Regex(@"\s*(®|™|\(R\)|\(TM\))", RegexOptions.IgnoreCase);
        private static readonly Regex BrandWords = new Regex(@"\b(Core\s+(?=i[3579]\b)|GeForce\s*|Radeon\s*)", RegexOptions.IgnoreCase);
        private static readonly Regex Brackets = new Regex(@"\s*\([^()]*\)");
        private static readonly Regex ClockSpeed = new Regex(@"\s*@\s*\d+(?:[.,]\d+)?\s*[GM]Hz\b", RegexOptions.IgnoreCase);
        private static readonly Regex OrEquivalent = new Regex(@"[,\s]*\bor (?:an )?equivalent\b", RegexOptions.IgnoreCase);
        private static readonly Regex OsLabel = new Regex(@"^(OS|Operating System)$", RegexOptions.IgnoreCase);
        private static readonly Regex Bitness = new Regex(@"[,\s]*\b(?:64|32/64)[- ]?bits?\b", RegexOptions.IgnoreCase);
        private static readonly Regex StorageLabel = new Regex(@"^(Storage|Hard (Drive|Disk)( Space)?|Disk Space|HDD|Hard Disk)$", RegexOptions.IgnoreCase);
        private static readonly Regex AvailableSpace = new Regex(@"\s+(?:of\s+)?(?:free\s+|available\s+)(?:disk\s+)?space\b", RegexOptions.IgnoreCase);

        private static string Shorten(string label, string text)
        {
            text = TrademarkSigns.Replace(text, string.Empty);
            text = BrandWords.Replace(text, string.Empty);
            var full = text;
            for (var before = string.Empty; before != text;)
            {
                before = text;
                text = Brackets.Replace(text, string.Empty);
            }

            text = ClockSpeed.Replace(text, string.Empty);
            text = OrEquivalent.Replace(text, string.Empty);
            if (OsLabel.IsMatch(label))
            {
                text = Bitness.Replace(text, string.Empty);
            }

            if (StorageLabel.IsMatch(label))
            {
                text = AvailableSpace.Replace(text, string.Empty);
            }

            text = Regex.Replace(text, @"\s+([,;.])", "$1");
            text = Regex.Replace(text, @",(\s*,)+", ",");
            text = Regex.Replace(text, @"\s+", " ").Trim().Trim(',', ';', ' ');
            return text.Length > 0 ? text : Regex.Replace(full, @"\s+", " ").Trim();
        }

        private static string Text(string html)
        {
            var text = Regex.Replace(html, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", string.Empty);
            return Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
        }

        private static string StorageOf(List<RequirementLine> lines)
        {
            foreach (var line in lines.Where(a => StorageLabel.IsMatch(a.Label)))
            {
                var size = Regex.Match(line.Value, @"(\d+(?:[.,]\d+)?)\s*(?:[A-Za-z]+\s+)?(TB|GB|MB)\b", RegexOptions.IgnoreCase);
                if (size.Success && double.TryParse(size.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    var unit = size.Groups[2].Value.ToUpperInvariant();
                    var gb = unit == "TB" ? number * 1024 : unit == "MB" ? number / 1024 : number;
                    return gb >= 1 ? $"{Math.Round(gb)} GB" : $"{Math.Round(gb * 1024)} MB";
                }
            }

            return null;
        }

        public class CacheEntry
        {
            public DateTime FetchedUtc { get; set; }
            public string Minimum { get; set; }
            public string Recommended { get; set; }
            [DontSerialize]
            public bool HasData => !string.IsNullOrWhiteSpace(Minimum) || !string.IsNullOrWhiteSpace(Recommended);
        }

        public class AppDetailsResponse
        {
            public bool success { get; set; }
            public AppData data { get; set; }
        }

        public class AppData
        {
            public PcRequirements pc_requirements { get; set; }
        }

        public class PcRequirements
        {
            public string minimum { get; set; }
            public string recommended { get; set; }
        }
    }

    public class RequirementsView
    {
        public IList<RequirementLine> Minimum { get; set; }
        public IList<RequirementLine> Recommended { get; set; }
        public bool HasMinimum => Minimum?.Count > 0;
        public bool HasRecommended => Recommended?.Count > 0;
        public IList<RequirementRow> Rows { get; set; }
        public string RequiredSpace { get; set; }
    }

    public class RequirementRow
    {
        public string Label { get; set; }
        public string Minimum { get; set; }
        public string Recommended { get; set; }
    }

    public class RequirementLine
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public bool HasLabel => !string.IsNullOrEmpty(Label);
    }
}
