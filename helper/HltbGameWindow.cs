using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Shell;

namespace Fluent11Helper
{
    internal static class HltbGameWindow
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string WindowFile = "Views\\Fluent11HltbGameWindow.xaml";

        public static bool Show(IPlayniteAPI api, string themeDir, Guid gameId, Action changed = null)
        {
            var game = api.Database.Games.Get(gameId);
            var database = HltbData.FindDatabase(api);
            if (game == null || database == null)
            {
                return false;
            }

            try
            {
                var model = new HltbGameModel();
                if (!Load(api, database, game, model))
                {
                    if (!Search(database, game) || !Load(api, database, game, model))
                    {
                        return true;
                    }
                }

                object content;
                using (var stream = File.OpenRead(Path.Combine(themeDir, WindowFile)))
                {
                    content = XamlReader.Load(stream);
                }

                var window = api.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowCloseButton = true,
                    ShowMaximizeButton = false,
                    ShowMinimizeButton = false
                });
                window.Title = "Time to Beat";
                SeamlessWindow.Apply(window);
                window.Width = 640;
                window.MinWidth = 640;
                window.MaxWidth = 640;
                window.SizeToContent = SizeToContent.Height;
                window.ResizeMode = ResizeMode.NoResize;
                window.SourceInitialized += (s, e) =>
                {
                    if (WindowChrome.GetWindowChrome(window) is WindowChrome chrome)
                    {
                        var fixedChrome = (WindowChrome)chrome.Clone();
                        fixedChrome.ResizeBorderThickness = new Thickness(0);
                        WindowChrome.SetWindowChrome(window, fixedChrome);
                    }
                };
                window.Owner = api.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                window.Content = content;
                CoverFormat.Apply(window);
                window.DataContext = model;

                var busy = false;
                var closing = false;
                window.Closing += (s, e) => closing = true;
                window.Deactivated += (s, e) => window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!busy && !closing && !window.IsActive)
                    {
                        window.Close();
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
                var owner = window.Owner;
                if (owner != null)
                {
                    var swallowUp = false;
                    MouseButtonEventHandler down = (s, e) =>
                    {
                        if (closing)
                        {
                            return;
                        }

                        swallowUp = true;
                        e.Handled = true;
                        window.Close();
                    };
                    MouseButtonEventHandler up = null;
                    MouseButtonEventHandler reset = null;
                    up = (s, e) =>
                    {
                        if (!swallowUp)
                        {
                            return;
                        }

                        swallowUp = false;
                        e.Handled = true;
                        owner.PreviewMouseUp -= up;
                        owner.PreviewMouseDown -= reset;
                    };
                    reset = (s, e) =>
                    {
                        owner.PreviewMouseUp -= up;
                        owner.PreviewMouseDown -= reset;
                    };
                    owner.PreviewMouseDown += down;
                    owner.PreviewMouseUp += up;
                    window.Closed += (s, e) =>
                    {
                        owner.PreviewMouseDown -= down;
                        if (swallowUp)
                        {
                            owner.PreviewMouseDown += reset;
                        }
                        else
                        {
                            owner.PreviewMouseUp -= up;
                        }
                    };
                }

                window.PreviewKeyDown += (s, e) =>
                {
                    if (e.Key == Key.Escape && !closing)
                    {
                        window.Close();
                        e.Handled = true;
                    }
                };

                window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((s, e) =>
                {
                    var tag = (e.OriginalSource as FrameworkElement)?.Tag as string;
                    if (tag == "TtbRefresh")
                    {
                        busy = true;
                        try
                        {
                            Invoke(database, "Refresh", game.Id);
                            Load(api, database, game, model);
                            changed?.Invoke();
                        }
                        finally
                        {
                            busy = false;
                        }

                        if (!closing)
                        {
                            window.Activate();
                        }

                        e.Handled = true;
                    }
                    else if (tag == "TtbDelete")
                    {
                        Invoke(database, "Remove", game.Id);
                        changed?.Invoke();
                        window.Close();
                        e.Handled = true;
                    }
                }));

                window.Show();
                return true;
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to open time to beat window.");
                return false;
            }
        }

        private static bool Search(object database, Game game)
        {
            var searchApi = Reflect.GetMember(database, "HowLongToBeatApi");
            var search = searchApi?.GetType().GetMethods().FirstOrDefault(a => a.Name == "SearchData" && a.GetParameters().Length >= 1
                && a.GetParameters()[0].ParameterType == typeof(Game));
            if (search == null)
            {
                logger.Warn("Fluent 11: HowLongToBeat search not found.");
                return false;
            }

            var args = search.GetParameters().Select((a, i) => i == 0 ? game : (a.HasDefaultValue ? a.DefaultValue : null)).ToArray();
            var result = search.Invoke(searchApi, args);
            if (result == null)
            {
                return false;
            }

            result.GetType().GetProperty("DateLastRefresh")?.SetValue(result, DateTime.Now);
            var save = database.GetType().GetMethods().FirstOrDefault(a => a.Name == "AddOrUpdate" && a.GetParameters().Length == 1
                && a.GetParameters()[0].ParameterType.IsInstanceOfType(result));
            save?.Invoke(database, new[] { result });
            return save != null;
        }

        private static void Invoke(object database, string name, Guid id)
        {
            var method = database.GetType().GetMethods()
                .FirstOrDefault(a => a.Name == name && a.GetParameters().Length == 1 && a.GetParameters()[0].ParameterType == typeof(Guid));
            method?.Invoke(database, new object[] { id });
        }

        private static bool Load(IPlayniteAPI api, object database, Game game, HltbGameModel model)
        {
            var times = HltbData.Times(HltbData.CachedEntry(database, game.Id));
            if (times == null)
            {
                return false;
            }

            var multi = Reflect.GetMember(times, "GameType")?.ToString() == "Multi";
            var t1 = HltbData.Seconds(times, multi ? "Solo" : "MainStory");
            var t2 = HltbData.Seconds(times, multi ? "CoOp" : "MainExtra");
            var t3 = HltbData.Seconds(times, multi ? "Vs" : "Completionist");
            var timeToBeat = HltbData.Seconds(times, "TimeToBeat");
            var played = (long)Math.Min(game.Playtime, (ulong)long.MaxValue);

            model.Name = game.Name;
            model.Cover = GameText.CoverPath(api, game);
            model.Label1 = multi ? "Solo" : "Main story";
            model.Label2 = multi ? "Co-Op" : "Main + extra";
            model.Label3 = multi ? "Vs" : "Completionist";
            model.Value1 = Format(t1);
            model.Value2 = Format(t2);
            model.Value3 = Format(t3);
            model.PlayedText = GameText.Time(played);

            var total = multi ? Math.Max(t1, Math.Max(t2, t3)) : Math.Max(t3, Math.Max(t2, t1));
            if (multi)
            {
                model.Segment1 = Star(t1);
                model.Segment2 = Star(t2);
                model.Segment3 = Star(t3);
                total = t1 + t2 + t3;
            }
            else
            {
                model.Segment1 = Star(t1);
                model.Segment2 = Star(Math.Max(0, t2 - t1));
                model.Segment3 = Star(Math.Max(0, t3 - Math.Max(t1, t2)));
            }

            var part = total > 0 ? Math.Min(0.98, Math.Max(0.02, (double)played / total)) : 0.02;
            model.PlayedPart = new GridLength(part, GridUnitType.Star);
            model.RestPart = new GridLength(1 - part, GridUnitType.Star);
            model.ProgressText = timeToBeat > 0 ? Math.Min(100, Math.Floor(100.0 * played / timeToBeat)) + " % of time to beat" : string.Empty;
            model.Notify(null);
            return true;
        }

        private static string Format(long seconds)
        {
            return seconds > 0 ? GameText.Time(seconds) : "--";
        }

        private static GridLength Star(long value)
        {
            return new GridLength(Math.Max(0, value), GridUnitType.Star);
        }
    }

    public class HltbGameModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; set; }
        public string Cover { get; set; }
        public string PlayedText { get; set; }
        public string Label1 { get; set; }
        public string Label2 { get; set; }
        public string Label3 { get; set; }
        public string Value1 { get; set; }
        public string Value2 { get; set; }
        public string Value3 { get; set; }
        public GridLength Segment1 { get; set; }
        public GridLength Segment2 { get; set; }
        public GridLength Segment3 { get; set; }
        public GridLength PlayedPart { get; set; }
        public GridLength RestPart { get; set; }
        public string ProgressText { get; set; }

        public void Notify(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
