using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class SteamReviews
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly TimeSpan SelectDelay = TimeSpan.FromMilliseconds(300);
        private static readonly TimeSpan Refresh = TimeSpan.FromDays(7);
        private static readonly TimeSpan RateLimitPause = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan UnexpectedPause = TimeSpan.FromHours(1);
        private const int MaxReviewLength = 700;

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
            cacheDir = Path.Combine(dataPath, "SteamReviews");
        }

        public static void OnGameSelected(Game game)
        {
            generation++;
            timer?.Stop();
            var appId = settings.SteamReviewsCard ? SystemRequirements.SteamAppId(game) : null;
            if (appId == null)
            {
                Publish(null);
                return;
            }

            var cached = ReadCache(appId);
            Publish(cached != null ? View(appId, cached) : null);
            if (cached != null && DateTime.UtcNow - cached.FetchedUtc < Refresh)
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
                    var view = View(appId, entry);
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
                    logger.Warn(e, $"Fluent 11: failed to get Steam reviews for app {appId}.");
                }
                finally
                {
                    oneAtATime.Release();
                }
            });
        }

        private static CacheEntry Download(string appId)
        {
            const string url = "https://store.steampowered.com/ajaxappreviews/{0}?json=1&num_per_page=1&purchase_type=all&{1}";
            const string helpful = "filter=all&language=english&day_range=365&review_type=";
            try
            {
                using (var client = new SteamWebClient())
                {
                    var summary = Parse(client.DownloadString(string.Format(url, appId, "language=all")));
                    var entry = new CacheEntry { FetchedUtc = DateTime.UtcNow, Summary = summary.query_summary };
                    if (entry.Summary?.total_reviews > 0)
                    {
                        entry.Positive = First(Parse(client.DownloadString(string.Format(url, appId, helpful + "positive"))));
                        entry.Critical = First(Parse(client.DownloadString(string.Format(url, appId, helpful + "negative"))));
                    }

                    return entry;
                }
            }
            catch (UnexpectedAnswerException)
            {
                pausedUntil = DateTime.UtcNow + UnexpectedPause;
                logger.Warn("Fluent 11: unexpected answer from Steam's reviews API (ajaxappreviews), pausing review requests for an hour.");
                return null;
            }
            catch (WebException e) when ((e.Response as HttpWebResponse)?.StatusCode == (HttpStatusCode)429)
            {
                pausedUntil = DateTime.UtcNow + RateLimitPause;
                logger.Info("Fluent 11: Steam asks to slow down, pausing review requests.");
                return null;
            }
            catch (WebException)
            {
                return null;
            }
        }

        private static ReviewsResponse Parse(string json)
        {
            if (SteamWebClient.IsUnexpected(json))
            {
                throw new UnexpectedAnswerException();
            }

            try
            {
                return Serialization.FromJson<ReviewsResponse>(json) ?? throw new UnexpectedAnswerException();
            }
            catch (Exception e) when (!(e is UnexpectedAnswerException))
            {
                throw new UnexpectedAnswerException();
            }
        }

        private static Review First(ReviewsResponse response)
        {
            return response.reviews?.Count > 0 ? response.reviews[0] : null;
        }

        private class UnexpectedAnswerException : Exception
        {
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

        private static void Publish(ReviewsView view)
        {
            var target = settings;
            if (target != null)
            {
                target.Reviews = view;
            }
        }

        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

        private static ReviewsView View(string appId, CacheEntry entry)
        {
            var summary = entry.Summary;
            if (summary == null || summary.total_reviews <= 0)
            {
                return null;
            }

            var share = (double)summary.total_positive / summary.total_reviews;
            return new ReviewsView
            {
                Percent = Math.Round(share * 100).ToString(English) + "%",
                Rating = summary.review_score_desc,
                Count = ShortCount(summary.total_reviews) + " reviews",
                ExactCount = summary.total_reviews.ToString("N0", English) + " reviews",
                PositiveBar = new GridLength(share, GridUnitType.Star),
                NegativeBar = new GridLength(1 - share, GridUnitType.Star),
                Positive = Item(appId, entry.Positive),
                Critical = Item(appId, entry.Critical),
                AllUrl = $"https://store.steampowered.com/app/{appId}/#app_reviews_hash"
            };
        }

        private static string ShortCount(int count)
        {
            if (count < 10000)
            {
                return count.ToString("N0", English);
            }

            if (count < 1000000)
            {
                return (count / 1000) + "K";
            }

            return (Math.Floor(count / 100000.0) / 10).ToString("0.#", English) + "M";
        }

        private static ReviewItem Item(string appId, Review review)
        {
            var text = Clean(review?.review);
            if (text.Length == 0)
            {
                return null;
            }

            if (text.Length > MaxReviewLength)
            {
                var cut = text.LastIndexOf(' ', MaxReviewLength);
                text = text.Substring(0, cut > MaxReviewLength / 2 ? cut : MaxReviewLength).TrimEnd() + "…";
            }

            var hours = (review.author?.playtime_at_review ?? 0) / 60.0;
            var date = DateTimeOffset.FromUnixTimeSeconds(review.timestamp_created).LocalDateTime;
            return new ReviewItem
            {
                IsPositive = review.voted_up,
                Text = text,
                Meta = $"{hours.ToString(hours < 10 ? "0.#" : "N0", English)} h on record · {date.ToString("MMM d, yyyy", English)}",
                Url = $"https://steamcommunity.com/profiles/{review.author?.steamid}/recommended/{appId}/"
            };
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            text = Regex.Replace(text, @"\[\*\]", "\n• ");
            text = Regex.Replace(text, @"\[/?[a-zA-Z0-9]+(=[^\]]*)?\]", string.Empty);
            text = Regex.Replace(text, @"[ \t]+", " ");
            text = Regex.Replace(text, @"\s*\n\s*", "\n");
            return text.Trim();
        }

        public class CacheEntry
        {
            public DateTime FetchedUtc { get; set; }
            public QuerySummary Summary { get; set; }
            public Review Positive { get; set; }
            public Review Critical { get; set; }
        }

        public class ReviewsResponse
        {
            public QuerySummary query_summary { get; set; }
            public List<Review> reviews { get; set; }
        }

        public class QuerySummary
        {
            public string review_score_desc { get; set; }
            public int total_positive { get; set; }
            public int total_negative { get; set; }
            public int total_reviews { get; set; }
        }

        public class Review
        {
            public Author author { get; set; }
            public string review { get; set; }
            public long timestamp_created { get; set; }
            public bool voted_up { get; set; }
        }

        public class Author
        {
            public string steamid { get; set; }
            public int playtime_at_review { get; set; }
        }
    }

    public class ReviewsView
    {
        public string Percent { get; set; }
        public string Rating { get; set; }
        public string Count { get; set; }
        public string ExactCount { get; set; }
        public GridLength PositiveBar { get; set; }
        public GridLength NegativeBar { get; set; }
        public ReviewItem Positive { get; set; }
        public ReviewItem Critical { get; set; }
        public string AllUrl { get; set; }
    }

    public class ReviewItem
    {
        public bool IsPositive { get; set; }
        public string Text { get; set; }
        public string Meta { get; set; }
        public string Url { get; set; }
        public bool Expanded { get; set; }
    }
}
