using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fluent11Helper
{
    internal static class TrayMenu
    {
        private const double IconSize = 20;

        private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>
        {
            { "LOCQuickFilterFavorites", "\uEB51" },
            { "LOCOpenPlaynite", "\uE8A7" },
            { "LOCMenuOpenFullscreen", "\uE740" },
            { "LOCMenuClients", "\uE7FC" },
            { "LOCMenuTools", "\uE90F" },
            { "LOCExitAppLabel", "\uE7E8" }
        };

        private static int schemeVersion;
        private static int appliedVersion = -1;

        public static bool Is(ContextMenu menu) => menu.GetType().Name == "TrayContextMenu";

        public static void SchemeChanged() => schemeVersion++;

        public static void Prepare(ContextMenu menu)
        {
            if (appliedVersion == schemeVersion)
            {
                return;
            }

            appliedVersion = schemeVersion;
            var app = Application.Current.Resources;
            var colors = new ResourceDictionary();
            foreach (var key in app.Keys.Cast<object>().ToList())
            {
                var value = app[key];
                if (value is Brush || value is Color)
                {
                    colors[key] = value;
                }
            }

            menu.Resources = colors;
            menu.Resources = new ResourceDictionary { { "MenuIconSize", IconSize } };
        }

        public static void StyleIcons(ContextMenu menu)
        {
            try
            {
                var glyphs = new Dictionary<string, string>();
                foreach (var pair in Glyphs)
                {
                    var text = ResourceProvider.GetString(pair.Key);
                    if (!string.IsNullOrEmpty(text) && !glyphs.ContainsKey(text))
                    {
                        glyphs[text] = pair.Value;
                    }
                }

                StyleIcons(menu.Items, ResourceProvider.GetString("LOCMenuClients"), ResourceProvider.GetResource("DefaultGameIcon"), glyphs);
            }
            catch (Exception e)
            {
                LogManager.GetLogger().Error(e, "Fluent 11: failed to style tray menu icons.");
            }
        }

        private static void StyleIcons(ItemCollection items, string clients, object placeholder, Dictionary<string, string> glyphs)
        {
            foreach (var item in items.OfType<MenuItem>())
            {
                if (item.Icon == null && item.Header is string header && glyphs.TryGetValue(header, out var glyph))
                {
                    item.Icon = Glyph(glyph);
                }

                if (item.HasItems)
                {
                    if (item.Header as string != clients)
                    {
                        StyleIcons(item.Items, clients, placeholder, glyphs);
                    }

                    continue;
                }

                if (item.Icon is Image image)
                {
                    item.Icon = null;
                    item.Icon = Rounded(image, image.Source == placeholder);
                }
            }
        }

        private static FrameworkElement Glyph(string glyph)
        {
            var text = new TextBlock
            {
                Text = glyph,
                FontSize = 16,
                Width = 16,
                Height = 16,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FluentIconFont");
            var host = new Grid { Width = IconSize, Height = IconSize };
            host.Children.Add(text);
            return host;
        }

        private static FrameworkElement Rounded(Image image, bool isPlaceholder)
        {
            var radius = Application.Current.TryFindResource("ControlCornerRadius") is CornerRadius corner ? corner.TopLeft : 4;
            var clip = new RectangleGeometry(new Rect(0, 0, IconSize, IconSize), radius, radius);
            clip.Freeze();
            image.Width = IconSize;
            image.Height = IconSize;
            image.Clip = clip;

            var host = new Grid { Width = IconSize, Height = IconSize };
            host.Children.Add(image);
            if (!isPlaceholder)
            {
                var border = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius) };
                border.SetResourceReference(Border.BorderBrushProperty, "ImageStrokeBrush");
                host.Children.Add(border);
            }

            return host;
        }
    }
}
