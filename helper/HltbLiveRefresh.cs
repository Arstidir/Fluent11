using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class HltbLiveRefresh
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static object database;
        private static DispatcherTimer timer;

        public static void Register(IPlayniteAPI api)
        {
            try
            {
                database = HltbData.FindDatabase(api);
                if (database == null)
                {
                    return;
                }

                var handler = typeof(HltbLiveRefresh).GetMethod(nameof(OnChanged), BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var name in new[] { "DatabaseItemUpdated", "DatabaseItemCollectionChanged" })
                {
                    var ev = database.GetType().GetEvent(name);
                    ev?.AddEventHandler(database, Delegate.CreateDelegate(ev.EventHandlerType, handler));
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to watch HowLongToBeat data.");
            }
        }

        private static void OnChanged(object sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (timer == null)
                {
                    timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    timer.Tick += (s, a) =>
                    {
                        timer.Stop();
                        Apply();
                    };
                }

                timer.Stop();
                timer.Start();
            }));
        }

        private static void Apply()
        {
            try
            {
                if (database.GetType().GetProperty("GameContext")?.GetValue(database) is Game game)
                {
                    database.GetType().GetMethod("SetThemesResources", new[] { typeof(Game) })?.Invoke(database, new object[] { game });
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to refresh HowLongToBeat values.");
            }
        }
    }
}
