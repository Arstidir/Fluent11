using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Fluent11Helper
{
    internal static class AddonIcons
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static readonly Dictionary<string, string> Icons = new Dictionary<string, string>
        {
            { "emtDownloadIcon", "\uE896" },
            { "emtSteamIcon", "\uE896" },
            { "emtGoogleIcon", "\uF6FA" },
            { "emtYoutubeIcon", "\uE714" },
            { "emtVideoFileIcon", "\uE714" },
            { "emtVideoMicroGenIcon", "\uE714" },
            { "emtImageFileIcon", "\uE7C5" },
            { "emtFileDeleteIcon", "\uE74D" },
            { "emtFolderIcon", "\uE8B7" },
            { "suDownloadIcon", "\uE896" },
            { "suRefreshIcon", "\uE72C" },
            { "suSearchIcon", "\uE721" },
            { "suShowScreenshotsIcon", "\uEF20" },
            { "suFetchIcon", "\uF7ED" },
            { "suIgnoreIcon", "\uE733" }
        };

        public static void Apply()
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
            {
                return;
            }

            foreach (var pair in Icons)
            {
                try
                {
                    if (!resources.Contains(pair.Key))
                    {
                        continue;
                    }

                    var icon = new TextBlock { Text = pair.Value, FontSize = 16 };
                    if (resources["FluentIconFont"] is System.Windows.Media.FontFamily font)
                    {
                        icon.FontFamily = font;
                    }
                    resources[pair.Key] = icon;
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Fluent 11: failed to replace icon {pair.Key}.");
                }
            }
        }

        public const string SteamFriendsGlyph = "\uECF9";

        public static void ApplySteamFriends(IPlayniteAPI api)
        {
            try
            {
                var icon = FindSteamFriendsIcon(api);
                if (icon == null)
                {
                    return;
                }

                icon.Text = "\uE902";
                icon.FontSize = 16;
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "FluentIconFont");
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to replace the Steam friends icon.");
            }
        }

        public static void ApplyAchievementsTrophy(IPlayniteAPI api)
        {
            try
            {
                var plugin = api.Addons.Plugins.FirstOrDefault(a => a.GetType().Assembly.GetName().Name == "PlayniteAchievements");
                if (plugin?.GetTopPanelItems()?.FirstOrDefault()?.Icon is System.Windows.Shapes.Path icon)
                {
                    icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "FluentTrophyGeometry");
                    icon.Width = 16;
                    icon.Height = 16;
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to replace the Playnite Achievements top bar icon.");
            }
        }

        public const string GameActivityGlyph = "\uE97F";
        public const string GameActivityFluentGlyph = "\uE9D2";

        public static void ApplyGameActivity(IPlayniteAPI api)
        {
            try
            {
                foreach (var icon in FindGameActivityIcons(api))
                {
                    icon.Text = GameActivityFluentGlyph;
                    icon.SetResourceReference(TextBlock.FontFamilyProperty, "FluentIconFont");
                    if (icon.FontSize > 16)
                    {
                        icon.FontSize = 16;
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to replace the GameActivity icons.");
            }
        }

        public static List<TextBlock> FindGameActivityIcons(IPlayniteAPI api)
        {
            var plugin = api.Addons.Plugins.FirstOrDefault(a => a.GetType().Assembly.GetName().Name == "GameActivity");
            if (plugin == null)
            {
                return new List<TextBlock>();
            }

            var icons = (plugin.GetSidebarItems() ?? Enumerable.Empty<SidebarItem>()).Select(a => a?.Icon)
                .Concat((plugin.GetTopPanelItems() ?? Enumerable.Empty<TopPanelItem>()).Select(a => a?.Icon));
            return icons.OfType<TextBlock>().Where(a => a.Text == GameActivityGlyph).ToList();
        }

        public static TextBlock FindSteamFriendsIcon(IPlayniteAPI api)
        {
            var steam = api.Addons.Plugins.FirstOrDefault(a => a.GetType().Assembly.GetName().Name == "SteamLibrary");
            return steam?.GetTopPanelItems()?.Select(a => a?.Icon as TextBlock).FirstOrDefault(a => a?.Text == SteamFriendsGlyph);
        }
    }
}
