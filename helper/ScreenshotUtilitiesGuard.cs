using Playnite.SDK;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Fluent11Helper
{
    internal static class ScreenshotUtilitiesGuard
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        internal const string PluginTypeName = "ScreenshotUtilities.ScreenshotUtilities";
        private static readonly TimeSpan PendingWindow = TimeSpan.FromMilliseconds(700);
        private static IPlayniteAPI api;
        private static DateTime lastSingleSelection = DateTime.MinValue;
        private static bool lookedUp;
        private static object plugin;
        private static PropertyInfo timerProperty;
        private static FieldInfo lastGameField;

        public static void Register(IPlayniteAPI playniteApi)
        {
            api = playniteApi;
        }

        public static void OnGameSelected(int count)
        {
            if (count == 1)
            {
                lastSingleSelection = DateTime.Now;
                return;
            }

            if (count > 0 || DateTime.Now - lastSingleSelection > PendingWindow)
            {
                return;
            }

            lastSingleSelection = DateTime.MinValue;
            try
            {
                if (!lookedUp)
                {
                    lookedUp = true;
                    plugin = api?.Addons.Plugins.FirstOrDefault(a => a.GetType().FullName == PluginTypeName);
                    var type = plugin?.GetType();
                    timerProperty = type?.GetProperty("Timer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    lastGameField = type?.GetField("_lastSelectedGame", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (plugin != null && (timerProperty == null || lastGameField == null))
                    {
                        logger.Warn("Fluent 11: Screenshot Utilities changed, crash guard inactive.");
                    }
                }

                if (plugin == null || timerProperty == null || lastGameField == null)
                {
                    return;
                }

                (timerProperty.GetValue(plugin) as Timer)?.Change(Timeout.Infinite, Timeout.Infinite);
                lastGameField.SetValue(plugin, Guid.Empty);
            }
            catch (Exception e)
            {
                logger.Warn(e, "Fluent 11: could not stop the Screenshot Utilities timer.");
            }
        }
    }
}
