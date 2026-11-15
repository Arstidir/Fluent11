using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
    internal class AchievementsPage
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string PageFile = "Views\\Fluent11AchievementsPage.xaml";

        private const double RingSize = 96;
        private const double RingThickness = 8;
        private const int ShowcaseCount = 6;
        private const int UnlockDays = 30;
        private const int UnlockMonths = 12;
        private const int ThemeSummaryLimit = 10;
        private const double MinBarFraction = 0.04;

        private readonly IPlayniteAPI api;
        private readonly string themeDir;
        private readonly Func<Fluent11HelperSettings> settings;
        private readonly Action saveSettings;
        private Control view;

        private ItemsControl gameList;
        private string searchText = string.Empty;
        private UIElement filterDot;
        private TextBlock sortText;
        private System.Windows.Shapes.Path ring;
        private TextBlock ringPercent;
        private TextBlock rankText;
        private TextBlock nextRankText;
        private ItemsControl showcase;
        private ItemsControl unlockBars;
        private ToggleButton unlockDaysTab;
        private TextBlock unlockSummary;
        private TextBlock unlockStart;
        private TextBlock unlockEnd;
        private Dictionary<DateTime, int> unlockCounts;

        private object games;
        private ListCollectionView gamesView;
        private object rarityList;
        private DispatcherTimer searchTimer;
        private bool updatePending;

        public AchievementsPage(IPlayniteAPI api, string themeDir, Func<Fluent11HelperSettings> settings, Action saveSettings)
        {
            this.api = api;
            this.themeDir = themeDir;
            this.settings = settings;
            this.saveSettings = saveSettings;
        }

        public static bool IsPlayniteAchievementsInstalled(IPlayniteAPI api)
        {
            return FindPlayniteAchievements(api) != null;
        }

        internal static Plugin FindPlayniteAchievements(IPlayniteAPI api)
        {
            return api.Addons.Plugins.FirstOrDefault(a => a.GetType().Assembly.GetName().Name == "PlayniteAchievements");
        }

        public SidebarItem CreateSidebarItem()
        {
            return new SidebarItem
            {
                Title = "Achievements",
                Type = SiderbarItemType.View,
                Icon = CreateIcon(),
                Opened = () =>
                {
                    RequestLibraryData();
                    view = view ?? CreateView();
                    openView = view;
                    CoverFormat.Apply(view);
                    UpdateData();
                    return view;
                }
            };
        }

        private static object CreateIcon()
        {
            var icon = new System.Windows.Shapes.Path { Stretch = Stretch.Uniform, Width = 20, Height = 20 };
            icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "FluentTrophyGeometry");
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

                host.DataContext = PlayniteInternals.FindSettingsRoot("PlayniteAchievements");
                if (host.DataContext == null)
                {
                    logger.Warn("Fluent 11: Playnite Achievements data not found, achievements page stays empty.");
                }

                host.PreviewMouseRightButtonUp += OnRightClick;
                host.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowClick));
                ConnectElements(host.Content as FrameworkElement);
                if (host.DataContext is INotifyPropertyChanged source)
                {
                    source.PropertyChanged += OnDataChanged;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to load achievements page.");
                host.Content = new TextBlock
                {
                    Text = "Achievements page could not be loaded. See extensions.log for details.",
                    Margin = new Thickness(24, 64, 24, 0)
                };
            }

            return host;
        }

        private const double StaleDelaySeconds = 1.5;
        private static bool dataStale;
        private static bool watching;
        private static DispatcherTimer staleTimer;
        private static Control openView;

        private static void WatchChanges(IPlayniteAPI api)
        {
            if (watching)
            {
                return;
            }

            watching = true;
            try
            {
                var runtime = Reflect.GetMember(FindPlayniteAchievements(api), "RefreshRuntime");
                var changed = runtime?.GetType().GetEvent("CacheInvalidated");
                if (changed == null)
                {
                    logger.Warn("Fluent 11: Playnite Achievements has no CacheInvalidated, achievements page updates after a restart only.");
                    return;
                }

                var handler = Delegate.CreateDelegate(changed.EventHandlerType,
                    typeof(AchievementsPage).GetMethod(nameof(OnCacheInvalidated), BindingFlags.NonPublic | BindingFlags.Static));
                changed.AddEventHandler(runtime, handler);
                staleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(StaleDelaySeconds) };
                staleTimer.Tick += (s, e) =>
                {
                    staleTimer.Stop();
                    if (dataStale && openView?.IsVisible == true)
                    {
                        RequestLibraryData(api, true);
                    }
                };
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to watch Playnite Achievements data changes.");
            }
        }

        private static void OnCacheInvalidated(object sender, EventArgs e)
        {
            dataStale = true;
            staleTimer?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (openView?.IsVisible == true)
                {
                    staleTimer.Stop();
                    staleTimer.Start();
                }
            }));
        }

        private void RequestLibraryData()
        {
            RequestLibraryData(api, true);
        }

        private static void RequestLibraryData(IPlayniteAPI api, bool heavy)
        {
            WatchChanges(api);
            try
            {
                var service = Reflect.GetMember(FindPlayniteAchievements(api), "ThemeIntegrationService");
                var ensure = service?.GetType().GetMethod("EnsureAllGamesThemeDataLoaded");
                if (ensure == null)
                {
                    logger.Warn("Fluent 11: Playnite Achievements has no EnsureAllGamesThemeDataLoaded, library data may stay empty.");
                    return;
                }

                var parameters = ensure.GetParameters();
                var force = dataStale;
                dataStale = false;
                var args = parameters.Select(a => a.Name == "includeHeavyAchievementLists" ? (object)heavy
                    : a.Name == "forceRefresh" ? (object)force
                    : a.HasDefaultValue ? a.DefaultValue : (a.ParameterType.IsValueType ? Activator.CreateInstance(a.ParameterType) : null)).ToArray();
                ensure.Invoke(service, args);
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to request Playnite Achievements library data.");
            }
        }

        internal static bool TryGetGameProgress(IPlayniteAPI api, Guid gameId, out int unlocked, out int total)
        {
            unlocked = 0;
            total = 0;
            try
            {
                if (FindPlayniteAchievements(api) == null)
                {
                    return false;
                }

                var root = PlayniteInternals.FindSettingsRoot("PlayniteAchievements");
                if (dataStale || !(Reflect.GetMember(root, "GameSummariesDesc") is IEnumerable list) || !list.Cast<object>().Any())
                {
                    RequestLibraryData(api, false);
                    list = Reflect.GetMember(root, "GameSummariesDesc") as IEnumerable;
                }

                var summary = list?.Cast<object>().FirstOrDefault(a => Reflect.GetMember(a, "GameId") is Guid id && id == gameId);
                if (summary == null)
                {
                    return false;
                }

                unlocked = Reflect.GetMember(summary, "UnlockedCount") is int u ? u : 0;
                total = Reflect.GetMember(summary, "AchievementCount") is int t ? t : 0;
                return true;
            }
            catch (Exception e)
            {
                logger.Debug($"Fluent 11: no achievements for insights flyout: {e.Message}");
                return false;
            }
        }

        #region Spieleliste, Ring, Vitrine

        private void ConnectElements(FrameworkElement root)
        {
            if (root == null)
            {
                return;
            }

            gameList = root.FindName("GameList") as ItemsControl;
            var searchBox = root.FindName("SearchBox") as UIElement;
            PageTools.BindPlayniteSettings(root);
            filterDot = root.FindName("FilterDot") as UIElement;
            sortText = root.FindName("SortText") as TextBlock;
            ring = root.FindName("ProgressRing") as System.Windows.Shapes.Path;
            ringPercent = root.FindName("RingPercent") as TextBlock;
            rankText = root.FindName("RankText") as TextBlock;
            nextRankText = root.FindName("NextRankText") as TextBlock;
            showcase = root.FindName("Showcase") as ItemsControl;
            unlockBars = root.FindName("UnlockBars") as ItemsControl;
            unlockDaysTab = root.FindName("UnlockDaysTab") as ToggleButton;
            unlockSummary = root.FindName("UnlockSummary") as TextBlock;
            unlockStart = root.FindName("UnlockStart") as TextBlock;
            unlockEnd = root.FindName("UnlockEnd") as TextBlock;
            if (unlockDaysTab != null)
            {
                if (settings().AchUnlocksByMonth && root.FindName("UnlockMonthsTab") is ToggleButton monthsTab)
                {
                    monthsTab.IsChecked = true;
                }

                unlockDaysTab.Checked += (s, e) => UnlockRangeChanged();
                unlockDaysTab.Unchecked += (s, e) => UnlockRangeChanged();
            }

            searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            searchTimer.Tick += (s, e) =>
            {
                searchTimer.Stop();
                gamesView?.Refresh();
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

            UpdateFilterDot();
            UpdateSortText();
        }

        private void OnDataChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case null:
                case "":
                case "GameSummariesDesc":
                case "TotalOverall":
                case "AllAchievementsRarityAsc":
                case "CollectorScore":
                    break;
                default:
                    return;
            }

            if (updatePending || view == null)
            {
                return;
            }

            updatePending = true;
            view.Dispatcher.BeginInvoke(new Action(UpdateData), DispatcherPriority.Background);
        }

        private void UpdateData()
        {
            updatePending = false;
            var data = view?.DataContext;
            if (data == null)
            {
                return;
            }

            try
            {
                UpdateGames(Reflect.GetMember(data, "GameSummariesDesc"));
                UpdateRing(Reflect.GetMember(data, "TotalOverall"));
                UpdateRank(Reflect.GetMember(data, "CollectorScore"));
                UpdateShowcase(data);
                UpdateUnlocks();
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to update achievements page.");
            }
        }

        private void UpdateGames(object source)
        {
            if (gameList == null || ReferenceEquals(source, games))
            {
                return;
            }

            games = source;
            gamesView = source is IList list ? new ListCollectionView(list) : null;
            if (gamesView != null)
            {
                gamesView.Filter = FilterGame;
                gamesView.CustomSort = new GameComparer(settings());
            }

            gameList.ItemsSource = gamesView;
        }

        private bool FilterGame(object item)
        {
            var options = settings();
            if (options.AchCompletedOnly && !(Reflect.GetMember(item, "IsCompleted") is bool completed && completed))
            {
                return false;
            }

            if (!options.AchShowHidden && Reflect.GetMember(item, "GameId") is Guid id && api.Database.Games.Get(id)?.Hidden == true)
            {
                return false;
            }

            var search = searchText.Trim();
            return string.IsNullOrEmpty(search)
                || (Reflect.GetMember(item, "Name") as string)?.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private IEnumerable<object> BuildSortMenu()
        {
            var options = settings();
            foreach (var sort in SortNames)
            {
                var value = sort.Key;
                yield return MenuPolish.Radio(Option(sort.Value, options.AchSort == value, () => options.AchSort = value));
            }

            yield return new Separator();
            yield return MenuPolish.Radio(Option("Ascending", !options.AchSortDescending, () => options.AchSortDescending = false));
            yield return MenuPolish.Radio(Option("Descending", options.AchSortDescending, () => options.AchSortDescending = true));
        }

        private IEnumerable<object> BuildFilterMenu()
        {
            var options = settings();
            yield return Option("Completed games only", options.AchCompletedOnly, () => options.AchCompletedOnly = !options.AchCompletedOnly);
            yield return new Separator();
            yield return Option("Include hidden games", options.AchShowHidden, () => options.AchShowHidden = !options.AchShowHidden);
        }

        private MenuItem Option(string header, bool isChecked, Action apply)
        {
            var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
            item.Click += (s, a) =>
            {
                apply();
                saveSettings();
                UpdateFilterDot();
                UpdateSortText();
                if (gamesView != null)
                {
                    gamesView.CustomSort = new GameComparer(settings());
                }
            };
            return item;
        }

        private void UpdateSortText()
        {
            if (sortText != null)
            {
                var options = settings();
                sortText.Text = SortNames[options.AchSort] + (options.AchSortDescending ? " ↓" : " ↑");
            }
        }

        private void UpdateFilterDot()
        {
            if (filterDot != null)
            {
                var options = settings();
                filterDot.Visibility = options.AchCompletedOnly || options.AchShowHidden ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static readonly Dictionary<AchSort, string> SortNames = new Dictionary<AchSort, string>
        {
            { AchSort.LastUnlocked, "Last unlocked" },
            { AchSort.Progress, "Progress" },
            { AchSort.Name, "Name" }
        };

        private class GameComparer : IComparer
        {
            private readonly AchSort sort;
            private readonly bool descending;

            public GameComparer(Fluent11HelperSettings options)
            {
                sort = options.AchSort;
                descending = options.AchSortDescending;
            }

            public int Compare(object x, object y)
            {
                int result;
                switch (sort)
                {
                    case AchSort.Progress:
                        result = Comparer.Default.Compare(Reflect.GetMember(x, "Progress"), Reflect.GetMember(y, "Progress"));
                        if (result == 0)
                        {
                            result = Comparer.Default.Compare(Reflect.GetMember(x, "UnlockedCount"), Reflect.GetMember(y, "UnlockedCount"));
                        }
                        break;
                    case AchSort.Name:
                        result = 0;
                        break;
                    default:
                        result = Comparer.Default.Compare(Reflect.GetMember(x, "LastUnlockDate"), Reflect.GetMember(y, "LastUnlockDate"));
                        break;
                }

                if (descending)
                {
                    result = -result;
                }

                if (result == 0)
                {
                    result = string.Compare(SortName(x), SortName(y), StringComparison.CurrentCultureIgnoreCase);
                    if (sort == AchSort.Name && descending)
                    {
                        result = -result;
                    }
                }

                return result;
            }

            private static string SortName(object item)
            {
                var name = Reflect.GetMember(item, "SortingName") as string;
                return string.IsNullOrEmpty(name) ? Reflect.GetMember(item, "Name") as string : name;
            }
        }

        private void UpdateRing(object totals)
        {
            var unlocked = Reflect.GetMember(totals, "Unlocked") is int u ? u : 0;
            var total = Reflect.GetMember(totals, "Total") is int t ? t : 0;
            var fraction = total > 0 ? Math.Min(1.0, (double)unlocked / total) : 0;

            if (ringPercent != null)
            {
                ringPercent.Text = total > 0 ? Math.Floor(fraction * 100).ToString(CultureInfo.CurrentCulture) + " %" : "–";
            }

            if (ring == null)
            {
                return;
            }

            var center = RingSize / 2;
            var radius = (RingSize - RingThickness) / 2;
            Geometry geometry = null;
            if (fraction >= 1)
            {
                geometry = new EllipseGeometry(new Point(center, center), radius, radius);
            }
            else if (fraction > 0)
            {
                var angle = fraction * 2 * Math.PI;
                var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
                var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
                figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
                geometry = new PathGeometry(new[] { figure });
            }

            geometry?.Freeze();
            ring.Data = geometry;
        }

        private void UpdateRank(object score)
        {
            if (rankText == null || !(score is int value))
            {
                return;
            }

            var snapshot = CallScoring("AchievementLevelCalculator", "CalculateModern", value);
            var rank = Reflect.GetMember(snapshot, "Rank") as string ?? Reflect.GetMember(view?.DataContext, "CollectorRank") as string;
            var current = CallScoring("AchievementRankPresentation", "FormatRank", rank) as string;
            if (!string.IsNullOrEmpty(current))
            {
                rankText.Text = current;
            }

            if (nextRankText == null)
            {
                return;
            }

            var next = CallScoring("AchievementRankPresentation", "FormatRank", Reflect.GetMember(snapshot, "NextRank") as string) as string;
            var nextLevel = Reflect.GetMember(snapshot, "RankEndLevel") is int end ? end + 1 : 0;
            var isMax = Reflect.GetMember(snapshot, "IsMaxLevel") is bool max && max;
            if (string.IsNullOrEmpty(next) || nextLevel <= 0 || isMax)
            {
                nextRankText.Visibility = Visibility.Collapsed;
                return;
            }

            nextRankText.Text = "Next: " + next + " at level " + nextLevel.ToString(CultureInfo.CurrentCulture);
            nextRankText.Visibility = Visibility.Visible;
        }

        private readonly Dictionary<string, MethodInfo> scoringMethods = new Dictionary<string, MethodInfo>();

        private object CallScoring<T>(string type, string method, T argument)
        {
            if (argument == null)
            {
                return null;
            }

            try
            {
                var key = type + "." + method;
                if (!scoringMethods.TryGetValue(key, out var info))
                {
                    info = FindPlayniteAchievements(api)?.GetType().Assembly
                        .GetType("PlayniteAchievements.Models.Achievements.Scoring." + type)?
                        .GetMethod(method, new[] { typeof(T) });
                    scoringMethods[key] = info;
                }

                return info?.Invoke(null, new object[] { argument });
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to call Playnite Achievements " + type + "." + method + ".");
                return null;
            }
        }

        private void UpdateShowcase(object data)
        {
            if (showcase == null)
            {
                return;
            }

            var all = Reflect.GetMember(data, "AllAchievementsRarityAsc");
            if (all != null && ReferenceEquals(all, rarityList))
            {
                return;
            }

            rarityList = all;
            List<object> items;
            if (all is IEnumerable list)
            {
                items = list.Cast<object>()
                    .Where(a => Reflect.GetMember(a, "Unlocked") is bool unlocked && unlocked)
                    .OrderBy(a => Reflect.GetMember(a, "RaritySortValue") is double value ? value : double.MaxValue)
                    .Take(ShowcaseCount)
                    .ToList();
            }
            else
            {
                items = (Reflect.GetMember(data, "RarestRecentUnlocksTop10") as IEnumerable)?.Cast<object>().Take(ShowcaseCount).ToList()
                    ?? new List<object>();
            }

            showcase.ItemsSource = items;
        }

        internal static Dictionary<DateTime, int> GetUnlockCounts(IPlayniteAPI api)
        {
            var service = Reflect.GetMember(FindPlayniteAchievements(api), "AchievementDataService");
            var summary = FindUnlockSummaryMethod(service)?.Invoke(service, new object[] { ThemeSummaryLimit });
            if (!(Reflect.GetMember(summary, "GlobalUnlockCountsByDate") is IDictionary counts))
            {
                return null;
            }

            var copy = new Dictionary<DateTime, int>();
            foreach (DictionaryEntry entry in counts)
            {
                if (entry.Key is DateTime day && entry.Value is int count)
                {
                    copy[day.Date] = count;
                }
            }

            return copy;
        }

        internal static MethodInfo FindUnlockSummaryMethod(object service)
        {
            return service?.GetType().GetMethod("GetCachedSummaryDataForTheme", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null);
        }

        private void UpdateUnlocks()
        {
            if (unlockBars == null)
            {
                return;
            }

            try
            {
                unlockCounts = GetUnlockCounts(api);
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to read Playnite Achievements unlock history.");
                unlockCounts = null;
            }

            ShowUnlocks();
        }

        private void UnlockRangeChanged()
        {
            ShowUnlocks();
            var byMonth = unlockDaysTab?.IsChecked == false;
            var options = settings();
            if (options.AchUnlocksByMonth != byMonth)
            {
                options.AchUnlocksByMonth = byMonth;
                saveSettings();
            }
        }

        private void ShowUnlocks()
        {
            if (unlockBars == null)
            {
                return;
            }

            var days = unlockDaysTab?.IsChecked != false;
            var culture = CultureInfo.GetCultureInfo("en-US");
            var today = DateTime.Today;
            var points = new List<KeyValuePair<DateTime, int>>();
            if (days)
            {
                for (var day = today.AddDays(1 - UnlockDays); day <= today; day = day.AddDays(1))
                {
                    points.Add(new KeyValuePair<DateTime, int>(day, unlockCounts != null && unlockCounts.TryGetValue(day, out var count) ? count : 0));
                }
            }
            else
            {
                var month = new DateTime(today.Year, today.Month, 1);
                for (var start = month.AddMonths(1 - UnlockMonths); start <= month; start = start.AddMonths(1))
                {
                    var end = start.AddMonths(1);
                    var sum = unlockCounts?.Where(a => a.Key >= start && a.Key < end).Sum(a => a.Value) ?? 0;
                    points.Add(new KeyValuePair<DateTime, int>(start, sum));
                }
            }

            var max = points.Max(a => a.Value);
            unlockBars.ItemsSource = points.Select(a => new UnlockBar(
                max > 0 && a.Value > 0 ? Math.Max(MinBarFraction, (double)a.Value / max) : 0,
                (days ? a.Key.ToString("M", culture) : a.Key.ToString("Y", culture)) + " · " + Achievements(a.Value))).ToList();

            var total = points.Sum(a => a.Value);
            if (unlockSummary != null)
            {
                unlockSummary.Text = unlockCounts == null ? "No data from Playnite Achievements"
                    : Achievements(total) + (days ? " in the last 30 days" : " in the last 12 months");
            }

            if (unlockStart != null)
            {
                unlockStart.Text = days ? points[0].Key.ToString("M", culture) : points[0].Key.ToString("MMM yyyy", culture);
            }

            if (unlockEnd != null)
            {
                unlockEnd.Text = days ? "Today" : "This month";
            }
        }

        private static string Achievements(int count)
        {
            return count.ToString("N0", CultureInfo.CurrentCulture) + (count == 1 ? " achievement" : " achievements");
        }

        public sealed class UnlockBar
        {
            public UnlockBar(double fraction, string tip)
            {
                Filled = new GridLength(fraction, GridUnitType.Star);
                Empty = new GridLength(1 - fraction, GridUnitType.Star);
                Tip = tip;
            }

            public GridLength Filled { get; }
            public GridLength Empty { get; }
            public string Tip { get; }
        }

        #endregion

        private void OnRowClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button barButton && barButton.Tag as string == "AchSortMenu")
            {
                PageTools.ShowMenu(barButton, BuildSortMenu());
                e.Handled = true;
                return;
            }

            if (e.OriginalSource is Button filterButton && filterButton.Tag as string == "AchFilterMenu")
            {
                PageTools.ShowMenu(filterButton, BuildFilterMenu());
                e.Handled = true;
                return;
            }

            if (!(e.OriginalSource is Button button) || button.Tag as string != "OpenGameAchievements")
            {
                return;
            }

            var gameId = PageTools.FindGameId(button);
            if (gameId.HasValue && OpenGameAchievements(api, gameId.Value))
            {
                e.Handled = true;
            }
        }

        private const string DetailsLinkTag = "Fluent11OpenAchievements";

        public static void RegisterDetailsLink(IPlayniteAPI playniteApi)
        {
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                if (e.OriginalSource is Button link && DetailsLinkTag.Equals(link.Tag)
                    && playniteApi.MainView.SelectedGames?.FirstOrDefault() is Game game && OpenGameAchievements(playniteApi, game.Id))
                {
                    e.Handled = true;
                }
            }));
        }

        private static bool OpenGameAchievements(IPlayniteAPI playniteApi, Guid gameId)
        {
            var plugin = FindPlayniteAchievements(playniteApi);
            var open = FindOpenWindowMethod(plugin);
            if (open == null)
            {
                return false;
            }

            try
            {
                var args = open.GetParameters().Select((a, i) => i == 0 ? gameId
                    : a.HasDefaultValue ? a.DefaultValue : (a.ParameterType.IsValueType ? Activator.CreateInstance(a.ParameterType) : null)).ToArray();
                open.Invoke(plugin, args);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: failed to open game achievements.");
                return false;
            }
        }

        internal static MethodInfo FindOpenWindowMethod(object plugin)
        {
            return plugin?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(a => a.Name == "OpenViewAchievementsWindow" && a.GetParameters().FirstOrDefault()?.ParameterType == typeof(Guid)
                    && a.GetParameters().Skip(1).All(p => p.HasDefaultValue));
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            var gameId = PageTools.FindGameId(e.OriginalSource as DependencyObject);
            var game = gameId.HasValue ? api.Database.Games.Get(gameId.Value) : null;
            var plugin = FindPlayniteAchievements(api);
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

                var menu = new ContextMenu { PlacementTarget = sender as UIElement, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
                foreach (var entry in PageTools.BuildMenu(items, games))
                {
                    menu.Items.Add(entry);
                }

                AcrylicPopups.Prepare(menu);
                menu.IsOpen = true;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: failed to build achievements context menu.");
            }
        }
    }
}
