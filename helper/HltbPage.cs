using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal class HltbPage
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string PageFile = "Views\\Fluent11HltbPage.xaml";

        private readonly IPlayniteAPI api;
        private readonly string themeDir;
        private readonly Func<Fluent11HelperSettings> settings;
        private readonly Action saveSettings;
        private readonly HltbPageModel model = new HltbPageModel();
        private List<HltbGame> allGames = new List<HltbGame>();
        private Control view;
        private string searchText = string.Empty;
        private DispatcherTimer searchTimer;

        public HltbPage(IPlayniteAPI api, string themeDir, Func<Fluent11HelperSettings> settings, Action saveSettings)
        {
            this.api = api;
            this.themeDir = themeDir;
            this.settings = settings;
            this.saveSettings = saveSettings;
        }

        public SidebarItem CreateSidebarItem()
        {
            return new SidebarItem
            {
                Title = "Time to Beat",
                Type = SiderbarItemType.View,
                Icon = CreateIcon(),
                Opened = () =>
                {
                    LoadGames();
                    view = view ?? CreateView();
                    CoverFormat.Apply(view);
                    return view;
                }
            };
        }

        private static object CreateIcon()
        {
            var icon = new System.Windows.Shapes.Path { Stretch = Stretch.Uniform, Width = 20, Height = 20 };
            icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "FluentHourglassGeometry");
            icon.SetBinding(Shape.FillProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
            });
            return icon;
        }

        private Control CreateView()
        {
            var host = PageTools.CreateHost();
            try
            {
                var path = System.IO.Path.Combine(themeDir, PageFile);
                using (var stream = File.OpenRead(path))
                {
                    host.Content = XamlReader.Load(stream);
                }

                host.DataContext = model;
                host.PreviewMouseRightButtonUp += OnRightClick;
                host.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));

                var searchBox = (host.Content as FrameworkElement)?.FindName("SearchBox") as UIElement;
                PageTools.BindPlayniteSettings(host.Content as FrameworkElement);
                searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                searchTimer.Tick += (s, e) =>
                {
                    searchTimer.Stop();
                    ApplyView();
                };
                if (searchBox != null)
                {
                    searchBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) =>
                    {
                        searchText = (e.OriginalSource as TextBox)?.Text ?? string.Empty;
                        searchTimer.Stop();
                        searchTimer.Start();
                    }));
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to load time to beat page.");
                host.Content = new TextBlock
                {
                    Text = "Time to Beat page could not be loaded. See extensions.log for details.",
                    Margin = new Thickness(24, 64, 24, 0)
                };
            }

            return host;
        }

        #region Daten

        private void LoadGames()
        {
            var options = settings();
            var games = new List<HltbGame>();
            try
            {
                HltbGame.PlayedStatusId = ReadPlayedStatus(api);
                if (!(HltbData.AllEntries(HltbData.FindDatabase(api)) is IEnumerable entries))
                {
                    logger.Warn("Fluent 11: HowLongToBeat data not found, time to beat page stays empty.");
                    entries = new object[0];
                }

                foreach (var entry in entries.Cast<object>().ToList())
                {
                    var game = ReadGame(entry);
                    if (game != null && (options.HltbIncludeHidden || !game.IsHidden))
                    {
                        games.Add(game);
                    }
                }

                if (options.HltbIncludeNoData)
                {
                    var withData = new HashSet<Guid>(games.Select(a => a.GameId));
                    foreach (var game in api.Database.Games)
                    {
                        if (!withData.Contains(game.Id) && (options.HltbIncludeHidden || !game.Hidden))
                        {
                            games.Add(new HltbGame(game, 0, GameText.CoverPath(api, game)));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to read HowLongToBeat data.");
            }

            allGames = games;
            UpdateOverview();
            ApplyView();
        }

        internal static Guid ReadPlayedStatus(IPlayniteAPI api)
        {
            try
            {
                var statuses = api.Database.CompletionStatuses;
                var statusSettings = statuses?.GetType().GetMethod("GetSettings", Type.EmptyTypes)?.Invoke(statuses, null);
                return Reflect.GetMember(statusSettings, "PlayedStatus") is Guid id ? id : Guid.Empty;
            }
            catch (Exception e)
            {
                logger.Debug($"Fluent 11: played status not found: {e.Message}");
                return Guid.Empty;
            }
        }

        private HltbGame ReadGame(object entry)
        {
            try
            {
                var times = HltbData.Times(entry);
                if (times == null || !(Reflect.GetMember(entry, "Id") is Guid id))
                {
                    return null;
                }

                var game = api.Database.Games.Get(id);
                var timeToBeat = HltbData.Seconds(times, "TimeToBeat");
                if (game == null || timeToBeat <= 0)
                {
                    return null;
                }

                return new HltbGame(game, timeToBeat, GameText.CoverPath(api, game));
            }
            catch (Exception e)
            {
                logger.Debug($"Fluent 11: skipped HowLongToBeat entry: {e.Message}");
                return null;
            }
        }

        private void UpdateOverview()
        {
            var withData = allGames.Where(a => a.HasData).ToList();
            var unfinished = withData.Where(a => !a.IsFinished).ToList();
            long totalTimeToBeat = withData.Sum(a => a.TimeToBeat);
            long totalProgress = withData.Sum(a => a.TimeToBeat - a.Remaining);

            model.GameCount = withData.Count;
            model.FinishedCount = withData.Count(a => a.IsFinished);
            model.InProgressCount = unfinished.Count(a => a.IsStarted);
            model.NotStartedCount = unfinished.Count(a => !a.IsStarted);
            model.BacklogText = GameText.Hours(unfinished.Sum(a => a.Remaining));
            model.PlayedText = GameText.Hours((long)withData.Sum(a => (double)a.Played));
            model.TimeToBeatText = GameText.Hours(totalTimeToBeat);
            model.OverallProgress = totalTimeToBeat > 0 ? 100.0 * totalProgress / totalTimeToBeat : 0;
            model.AlmostDone = unfinished.Where(a => a.IsStarted && a.IsActive).OrderBy(a => a.Remaining).Take(5).ToList();
            model.Notify(null);
        }

        private void ApplyView()
        {
            var options = settings();
            IEnumerable<HltbGame> games = allGames;
            if (options.HltbInstalledOnly)
            {
                games = games.Where(a => a.IsInstalled);
            }

            if (options.HltbHideFinished)
            {
                games = games.Where(a => !a.IsFinished);
            }

            if (options.HltbHideNotStarted)
            {
                games = games.Where(a => a.IsStarted);
            }

            var search = searchText.Trim();
            if (!string.IsNullOrEmpty(search))
            {
                games = games.Where(a => a.Name?.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }

            Func<HltbGame, object> key;
            switch (options.HltbSort)
            {
                case HltbSort.Progress: key = a => a.Progress; break;
                case HltbSort.Name: key = a => a.SortName; break;
                case HltbSort.Played: key = a => a.Played; break;
                case HltbSort.TimeToBeat: key = a => a.TimeToBeat; break;
                case HltbSort.LastPlayed: key = a => a.LastActivity ?? DateTime.MinValue; break;
                default: key = a => a.IsFinished ? long.MaxValue : a.Remaining; break;
            }

            var timeSort = options.HltbSort == HltbSort.Remaining || options.HltbSort == HltbSort.Progress || options.HltbSort == HltbSort.TimeToBeat;
            var grouped = games.OrderBy(a => timeSort && !a.HasData ? 1 : 0);
            var sorted = options.HltbSortDescending ? grouped.ThenByDescending(key) : grouped.ThenBy(key);
            model.Games = sorted.ThenBy(a => a.SortName).ToList();
            model.SortText = SortNames[options.HltbSort] + (options.HltbSortDescending ? " ↓" : " ↑");
            model.IsFilterActive = options.HltbInstalledOnly || options.HltbHideFinished || options.HltbHideNotStarted
                || options.HltbIncludeHidden || options.HltbIncludeNoData;
            model.Notify(nameof(HltbPageModel.Games));
            model.Notify(nameof(HltbPageModel.SortText));
            model.Notify(nameof(HltbPageModel.IsFilterActive));
        }

        private static readonly Dictionary<HltbSort, string> SortNames = new Dictionary<HltbSort, string>
        {
            { HltbSort.Remaining, "Remaining time" },
            { HltbSort.Progress, "Progress" },
            { HltbSort.Name, "Name" },
            { HltbSort.Played, "Time played" },
            { HltbSort.TimeToBeat, "Time to beat" },
            { HltbSort.LastPlayed, "Last played" }
        };

        #endregion

        #region Bedienung

        private void OnClick(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is ButtonBase button))
            {
                return;
            }

            switch (button.Tag as string)
            {
                case "HltbSortMenu":
                    PageTools.ShowMenu(button, BuildSortMenu());
                    e.Handled = true;
                    break;
                case "HltbFilterMenu":
                    PageTools.ShowMenu(button, BuildFilterMenu());
                    e.Handled = true;
                    break;
                case "HltbOpenGame":
                    OpenGameData(PageTools.FindGameId(button));
                    e.Handled = true;
                    break;
            }
        }

        private void OpenGameData(Guid? gameId)
        {
            var game = gameId.HasValue ? api.Database.Games.Get(gameId.Value) : null;
            var plugin = HltbData.FindPlugin(api);
            if (game == null)
            {
                return;
            }

            if (settings().ReplaceHltbWindow && HltbGameWindow.Show(api, themeDir, game.Id, LoadGames))
            {
                LoadGames();
                return;
            }

            try
            {
                var games = new List<Game> { game };
                var label = ResourceProvider.GetString("LOCHowLongToBeatPluginView");
                var item = plugin?.GetGameMenuItems(new GetGameMenuItemsArgs { Games = games })?
                    .FirstOrDefault(a => a.Description == label);
                if (item?.Action != null)
                {
                    item.Action(new GameMenuItemActionArgs { Games = games, SourceItem = item });
                    LoadGames();
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: failed to open HowLongToBeat data.");
            }

            api.MainView.SwitchToLibraryView();
            api.MainView.SelectGame(game.Id);
        }

        private IEnumerable<object> BuildSortMenu()
        {
            var options = settings();
            foreach (var sort in SortNames)
            {
                var value = sort.Key;
                yield return MenuPolish.Radio(Option(sort.Value, options.HltbSort == value, () => options.HltbSort = value));
            }

            yield return new Separator();
            yield return MenuPolish.Radio(Option("Ascending", !options.HltbSortDescending, () => options.HltbSortDescending = false));
            yield return MenuPolish.Radio(Option("Descending", options.HltbSortDescending, () => options.HltbSortDescending = true));
        }

        private IEnumerable<object> BuildFilterMenu()
        {
            var options = settings();
            yield return Option("Installed games only", options.HltbInstalledOnly, () => options.HltbInstalledOnly = !options.HltbInstalledOnly);
            yield return Option("Hide finished games", options.HltbHideFinished, () => options.HltbHideFinished = !options.HltbHideFinished);
            yield return Option("Hide games not started", options.HltbHideNotStarted, () => options.HltbHideNotStarted = !options.HltbHideNotStarted);
            yield return new Separator();
            yield return Option("Include hidden games", options.HltbIncludeHidden, () => options.HltbIncludeHidden = !options.HltbIncludeHidden, true);
            yield return Option("Include games without data", options.HltbIncludeNoData, () => options.HltbIncludeNoData = !options.HltbIncludeNoData, true);
        }

        private MenuItem Option(string header, bool isChecked, Action apply, bool reload = false)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, StaysOpenOnClick = false };
            item.Click += (s, a) =>
            {
                apply();
                saveSettings();
                if (reload)
                {
                    LoadGames();
                }
                else
                {
                    ApplyView();
                }
            };
            return item;
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            var gameId = PageTools.FindGameId(e.OriginalSource as DependencyObject);
            var game = gameId.HasValue ? api.Database.Games.Get(gameId.Value) : null;
            var plugin = HltbData.FindPlugin(api);
            if (game == null || plugin == null)
            {
                return;
            }

            try
            {
                var games = new List<Game> { game };
                var items = plugin.GetGameMenuItems(new GetGameMenuItemsArgs { Games = games })?.ToList();
                if (items == null || items.Count == 0)
                {
                    return;
                }

                if (settings().ReplaceHltbWindow)
                {
                    var label = ResourceProvider.GetString("LOCHowLongToBeatPluginView");
                    items = items.Select(a => a.Description != label ? a : new GameMenuItem
                    {
                        MenuSection = a.MenuSection,
                        Description = "Time to Beat",
                        Action = x => OpenGameData(game.Id)
                    }).ToList();
                }

                var menu = new ContextMenu { PlacementTarget = sender as UIElement, Placement = PlacementMode.MousePoint };
                foreach (var entry in PageTools.BuildMenu(items, games))
                {
                    menu.Items.Add(entry);
                }

                menu.Closed += (s, a) => LoadGames();
                AcrylicPopups.Prepare(menu);
                menu.IsOpen = true;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: failed to build HowLongToBeat context menu.");
            }
        }

        #endregion
    }

    public class HltbPageModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public List<HltbGame> Games { get; set; } = new List<HltbGame>();
        public List<HltbGame> AlmostDone { get; set; } = new List<HltbGame>();
        public string SortText { get; set; }
        public bool IsFilterActive { get; set; }

        public int GameCount { get; set; }
        public int FinishedCount { get; set; }
        public int InProgressCount { get; set; }
        public int NotStartedCount { get; set; }
        public string BacklogText { get; set; }
        public string PlayedText { get; set; }
        public string TimeToBeatText { get; set; }
        public double OverallProgress { get; set; }

        public void Notify(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public class HltbGame
    {
        public Guid GameId { get; }
        public string Name { get; }
        public string SortName { get; }
        public string Cover { get; }
        public string Info { get; }
        public ulong Played { get; }
        public long TimeToBeat { get; }
        public long Remaining { get; }
        public double Progress { get; }
        public bool IsFinished { get; }
        public bool IsStarted { get; }
        public bool IsInstalled { get; }
        public bool IsHidden { get; }
        public bool HasData { get; }
        public DateTime? LastActivity { get; }
        public string TimesText { get; }
        public string RemainingText { get; }
        public string ProgressText { get; }

        internal static Guid PlayedStatusId;

        public bool IsActive { get; }

        public HltbGame(Game game, long timeToBeat, string cover)
        {
            GameId = game.Id;
            Name = game.Name;
            SortName = string.IsNullOrEmpty(game.SortingName) ? game.Name : game.SortingName;
            Cover = cover;
            Played = game.Playtime;
            TimeToBeat = timeToBeat;
            HasData = timeToBeat > 0;
            IsStarted = game.Playtime > 0;
            var beaten = IsBeatenStatus(game.CompletionStatus?.Name);
            Remaining = beaten ? 0 : Math.Max(0, timeToBeat - (long)Math.Min(game.Playtime, (ulong)long.MaxValue));
            IsFinished = HasData && (beaten || IsStarted && Remaining == 0);
            var status = game.CompletionStatus?.Name;
            IsActive = game.CompletionStatusId == Guid.Empty
                || (PlayedStatusId != Guid.Empty && game.CompletionStatusId == PlayedStatusId)
                || string.Equals(status, "Playing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Played", StringComparison.OrdinalIgnoreCase);
            IsInstalled = game.IsInstalled;
            IsHidden = game.Hidden;
            LastActivity = game.LastActivity;
            Progress = !HasData ? 0 : IsFinished ? 100 : Math.Min(100.0, 100.0 * game.Playtime / timeToBeat);

            Info = game.CompletionStatus?.Name ?? string.Empty;
            if (HasData)
            {
                TimesText = GameText.Time((long)game.Playtime) + " / " + GameText.Time(timeToBeat);
                RemainingText = IsFinished ? "Done" : GameText.Time(Remaining) + " left";
                ProgressText = Math.Floor(Progress) + " %";
            }
            else
            {
                TimesText = GameText.Time((long)game.Playtime);
                RemainingText = "No data";
                ProgressText = string.Empty;
            }
        }

        private static bool IsBeatenStatus(string status)
        {
            return string.Equals(status, "Beaten", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);
        }
    }
}
