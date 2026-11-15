using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Reflection;

namespace Fluent11Helper
{
    internal static class HltbSidebarEntry
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string SettingName = "EnableIntegrationButtonSide";

        public static bool? Get(IPlayniteAPI api)
        {
            try
            {
                var settings = GetSettings(HltbData.FindPlugin(api));
                return settings?.GetType().GetProperty(SettingName)?.GetValue(settings) as bool?;
            }
            catch (Exception e)
            {
                logger.Warn(e, "Fluent 11: could not read the HowLongToBeat sidebar setting.");
                return null;
            }
        }

        public static void Set(IPlayniteAPI api, bool show)
        {
            try
            {
                var plugin = HltbData.FindPlugin(api);
                var settings = GetSettings(plugin);
                var property = settings?.GetType().GetProperty(SettingName);
                if (property == null)
                {
                    logger.Warn("Fluent 11: HowLongToBeat sidebar setting not found, left unchanged.");
                    return;
                }

                property.SetValue(settings, show);
                if (Reflect.GetMember(plugin, "SidebarItem") is SidebarItem item)
                {
                    item.Visible = show;
                }

                typeof(Plugin).GetMethod(nameof(Plugin.SavePluginSettings), BindingFlags.Public | BindingFlags.Instance)
                    ?.MakeGenericMethod(settings.GetType()).Invoke(plugin, new[] { settings });
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to change the HowLongToBeat sidebar setting.");
            }
        }

        private static object GetSettings(Plugin plugin)
        {
            return Reflect.GetMember(Reflect.GetMember(plugin, "PluginSettingsViewModel"), "Settings");
        }
    }
}
