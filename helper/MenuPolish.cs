using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Fluent11Helper
{
    internal static class MenuPolish
    {
        private const string UninstallGlyph = "\uEC91";
        private const string CategoryGlyph = "\uE8EC";
        private const string CompletionGlyph = "\uE930";
        private const string HeartGlyph = "\uEB51";
        private const string AddonFallbackGlyph = "\uE74C";

        private static readonly Dictionary<string, string> AddonIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Extra Metadata", "\uEB9F" },
            { "Fluent 11", "outline:Fluent11IconLines|Fluent11IconSparkle" },
            { "HowLongToBeat", "geometry:FluentHourglassGeometry" },
            { "Ludusavi", "\uE753" },
            { "Playnite Achievements", "geometry:FluentTrophyGeometry" },
            { "PS5 Experience", "\uE967" },
            { "Screenshot Utilities", "\uF7ED" }
        };

        private static readonly Dictionary<string, string> AddonIconsByKey = new Dictionary<string, string>
        {
            { "LOCGameActivity", global::Fluent11Helper.AddonIcons.GameActivityFluentGlyph }
        };

        private static readonly Dictionary<string, string> AddonItemIcons = new Dictionary<string, string>
        {
            { "LOCGameActivityViewGameActivity", global::Fluent11Helper.AddonIcons.GameActivityFluentGlyph }
        };

        private static readonly Dictionary<string, string> MainMenuIcons = new Dictionary<string, string>
        {
            { "LOCLibrary", "\uE8F1" },
            { "LOCMenuView", "\uE8FC" },
            { "LOCMenuClients", "\uE7FC" },
            { "LOCMenuTools", "\uE90F" },
            { "LOCExtensions", "\uE943" },
            { "LOCMenuLinksTitle", "\uE71B" },
            { "LOCMenuHelpTitle", "\uE897" },
            { "LOCCheckForUpdates", "\uE895" },
            { "LOCGameActivity", global::Fluent11Helper.AddonIcons.GameActivityFluentGlyph }
        };

        public class MenuEntry
        {
            public MenuEntry(string id, bool gameMenu, string label, params string[] keys)
            {
                Id = id;
                GameMenu = gameMenu;
                Label = label;
                Keys = keys;
            }

            public string Id { get; }
            public bool GameMenu { get; }
            public string Label { get; }
            public string[] Keys { get; }
            public string DisplayName => Label ?? ResourceProvider.GetString(Keys.Last().Split(':').Last());
        }

        public const string LudusaviEntry = "LudusaviTags";

        public static readonly MenuEntry[] MenuEntries =
        {
            new MenuEntry("MainLinks", false, null, "MainMenu:LOCMenuLinksTitle"),
            new MenuEntry("RandomGame", false, null, "MainMenu:LOCMenuSelectRandomGame"),
            new MenuEntry("Clients", false, null, "TrayContextMenu:LOCMenuClients", "MainMenu:LOCMenuClients"),
            new MenuEntry("GameLinks", true, null, "GameMenu:LOCLinksLabel"),
            new MenuEntry("DesktopShortcut", true, null, "GameMenu:LOCCreateDesktopShortcut"),
            new MenuEntry("InstallSize", true, null, "GameMenu:LOCCalculateInstallSize", "GameMenu:LOCInstallSizeMenuLabel"),
            new MenuEntry("Category", true, null, "GameMenu:LOCSetGameCategory"),
            new MenuEntry("CompletionStatus", true, null, "GameMenu:LOCSetCompletionStatus"),
            new MenuEntry(LudusaviEntry, true, "Ludusavi tags")
        };

        public static void Apply(ContextMenu menu, ICollection<string> hiddenEntries, bool addonsInSidebar)
        {
            var name = menu.GetType().Name;
            var prefix = name + ":";
            var entries = MenuEntries.Where(a => a.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal))).ToList();
            if (entries.Count > 0)
            {
                foreach (var entry in entries)
                {
                    var texts = entry.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(k => ResourceProvider.GetString(k.Substring(prefix.Length))).ToList();
                    var hidden = hiddenEntries.Contains(entry.Id);
                    foreach (var item in menu.Items.OfType<MenuItem>().Where(a => texts.Contains(a.Header as string)))
                    {
                        item.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
                    }
                }

                TidySeparators(menu);
            }

            if (name == "MainMenu")
            {
                ApplyMainMenu(menu, addonsInSidebar);
                return;
            }

            if (name != "GameMenu")
            {
                return;
            }

            HideLudusaviTagItems(menu, hiddenEntries.Contains(LudusaviEntry));

            var items = menu.Items.OfType<MenuItem>().ToList();
            var sample = items.Select(a => a.Icon).OfType<FrameworkElement>().FirstOrDefault();
            var playniteSubmenus = new[] { "LOCLinksLabel", "LOCSetCompletionStatus", "LOCInstallSizeMenuLabel" }
                .Select(ResourceProvider.GetString).ToList();
            var uninstall = ResourceProvider.GetString("LOCUninstallGame");
            var category = ResourceProvider.GetString("LOCSetGameCategory");
            var completion = ResourceProvider.GetString("LOCSetCompletionStatus");
            var unfavorite = ResourceProvider.GetString("LOCRemoveFavoriteGame");
            var addonIconsByText = Translate(AddonIconsByKey);
            var addonItemIcons = Translate(AddonItemIcons);

            if (addonItemIcons.Count > 0)
            {
                ReplaceItemIcons(menu, addonItemIcons, sample);
            }

            foreach (var item in items)
            {
                if (item.FontWeight == FontWeights.Bold)
                {
                    item.FontWeight = FontWeights.Normal;
                }

                if (item.Header as string == unfavorite && !(item.Icon is Grid))
                {
                    item.Icon = CreateStruckIcon(HeartGlyph, sample, item);
                    continue;
                }

                if (item.Items.Count > 0 && addonItemIcons.Count > 0)
                {
                    ReplaceItemIcons(item, addonItemIcons, sample);
                }

                if (item.Header as string == completion)
                {
                    foreach (var status in item.Items.OfType<MenuItem>())
                    {
                        Radio(status);
                    }
                }

                if (item.Icon != null || !(item.Header is string header))
                {
                    continue;
                }

                if (header == uninstall)
                {
                    item.Icon = CreateIcon(UninstallGlyph, sample, owner: item);
                }
                else if (header == category)
                {
                    item.Icon = CreateIcon(CategoryGlyph, sample, owner: item);
                }
                else if (header == completion)
                {
                    item.Icon = CreateIcon(CompletionGlyph, sample, owner: item);
                }
                else if (item.Items.Count > 0 && !playniteSubmenus.Contains(header))
                {
                    var glyph = AddonIcons.TryGetValue(header, out var byName) ? byName
                        : addonIconsByText.TryGetValue(header, out var byKey) ? byKey : AddonFallbackGlyph;
                    item.Icon = CreateIcon(glyph, sample, owner: item);
                }
            }

            var barSample = new TextBlock { FontSize = 16, Width = 16, Height = 16 };
            GameMenuBar.Apply(menu, glyph => CreateIcon(glyph, barSample), () => CreateStruckIcon(HeartGlyph, barSample), TidySeparators);
        }

        private static readonly ConditionalWeakTable<ListBox, object> watchedSearchLists = new ConditionalWeakTable<ListBox, object>();

        private const string SearchFill = "TextBrush";

        public static void WatchSearch(Window window)
        {
            var list = FindListBox(window);
            if (list == null || watchedSearchLists.TryGetValue(list, out _))
            {
                return;
            }

            watchedSearchLists.Add(list, null);
            ((INotifyCollectionChanged)list.Items).CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    ApplySearch(list.ItemsSource);
                }
            };
            ApplySearch(list.ItemsSource);
        }

        private static ListBox FindListBox(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var found = child as ListBox ?? FindListBox(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void ApplySearch(IEnumerable results)
        {
            if (results == null)
            {
                return;
            }

            try
            {
                var items = results.OfType<SearchItem>().Select(a => Reflect.GetMember(a, "Item") as SearchItem ?? a).ToList();
                var edit = ResourceProvider.GetString("LOCEditGame");
                var category = ResourceProvider.GetString("LOCSetGameCategory");
                if (!items.Any(a => a.Name == edit) || !items.Any(a => a.Name == category))
                {
                    return;
                }

                var uninstall = ResourceProvider.GetString("LOCUninstallGame");
                var completion = ResourceProvider.GetString("LOCSetCompletionStatus");
                var addonIconsByText = Translate(AddonIconsByKey);
                var addonItemIcons = Translate(AddonItemIcons);
                foreach (var item in items)
                {
                    var name = item.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var last = name.Split(new[] { " > " }, StringSplitOptions.None).Last();
                    if (addonItemIcons.TryGetValue(last, out var replacement) && !(item.Icon is TextBlock) && !(item.Icon is Viewbox))
                    {
                        item.Icon = CreateIcon(replacement, null, SearchFill);
                        continue;
                    }

                    if (item.Icon != null)
                    {
                        continue;
                    }

                    if (name == uninstall)
                    {
                        item.Icon = CreateIcon(UninstallGlyph, null, SearchFill);
                    }
                    else if (name == category)
                    {
                        item.Icon = CreateIcon(CategoryGlyph, null, SearchFill);
                    }
                    else if (name == completion)
                    {
                        item.Icon = CreateIcon(CompletionGlyph, null, SearchFill);
                    }
                    else if (name.Contains(" > "))
                    {
                        var section = name.Substring(0, name.IndexOf(" > ", StringComparison.Ordinal));
                        var glyph = AddonIcons.TryGetValue(section, out var byName) ? byName
                            : addonIconsByText.TryGetValue(section, out var byKey) ? byKey : AddonFallbackGlyph;
                        item.Icon = CreateIcon(glyph, null, SearchFill);
                    }
                }
            }
            catch
            {
            }
        }

        private static Dictionary<string, string> Translate(Dictionary<string, string> byKey)
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in byKey)
            {
                var text = ResourceProvider.GetString(pair.Key);
                if (!string.IsNullOrEmpty(text) && text != pair.Key && !text.StartsWith("<!", StringComparison.Ordinal))
                {
                    result[text] = pair.Value;
                }
            }

            return result;
        }

        private static void ReplaceItemIcons(ItemsControl submenu, Dictionary<string, string> icons, FrameworkElement sample)
        {
            foreach (var child in submenu.Items.OfType<MenuItem>())
            {
                if (child.Header is string text && icons.TryGetValue(text, out var glyph) && !(child.Icon is TextBlock) && !(child.Icon is Viewbox))
                {
                    child.Icon = CreateIcon(glyph, sample, owner: child);
                }
            }
        }

        private const string LudusaviTagMarker = "[Ludusavi] ";

        private static void HideLudusaviTagItems(ItemsControl menu, bool hide)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Header is string text && text.Contains(LudusaviTagMarker))
                {
                    item.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
                }
                else if (item.Items.Count > 0)
                {
                    HideLudusaviTagItems(item, hide);
                    TidySeparators(item);
                }
            }
        }

        private static void TidySeparators(ItemsControl menu)
        {
            Separator pending = null;
            var anyVisible = false;
            foreach (var element in menu.Items.OfType<FrameworkElement>())
            {
                if (element is Separator separator)
                {
                    separator.Visibility = Visibility.Collapsed;
                    if (anyVisible)
                    {
                        pending = separator;
                    }
                }
                else if (element.Visibility == Visibility.Visible)
                {
                    if (pending != null)
                    {
                        pending.Visibility = Visibility.Visible;
                        pending = null;
                    }

                    anyVisible = true;
                }
            }
        }

        private static void ApplyMainMenu(ContextMenu menu, bool addonsInSidebar)
        {
            var items = menu.Items.OfType<MenuItem>().ToList();
            var settingsText = ResourceProvider.GetString("LOCMenuPlayniteSettingsTitle");
            var addonsText = ResourceProvider.GetString("LOCMenuAddons");
            foreach (var item in items)
            {
                if (item.Header is string text && (text == settingsText || text == addonsText))
                {
                    var inSidebar = text == settingsText || addonsInSidebar;
                    item.Visibility = inSidebar ? Visibility.Collapsed : Visibility.Visible;
                }
            }

            TidySeparators(menu);
            HideSidebarMenu(menu);

            var sample = items.Select(a => a.Icon).OfType<FrameworkElement>().FirstOrDefault();
            var byText = MainMenuIcons.GroupBy(a => ResourceProvider.GetString(a.Key) ?? a.Key)
                .ToDictionary(a => a.Key, a => a.First().Value);
            foreach (var item in items)
            {
                if (item.Icon == null && item.Header is string header && byText.TryGetValue(header, out var glyph))
                {
                    item.Icon = CreateIcon(glyph, sample, owner: item);
                }
            }
        }

        public const string RadioTag = "Fluent11Radio";

        public static MenuItem Radio(MenuItem item)
        {
            item.Tag = RadioTag;
            return item;
        }

        public static void MarkRadioItems(ItemsControl menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag == null && item.IsCheckable
                    && BindingOperations.GetBinding(item, MenuItem.IsCheckedProperty)?.ConverterParameter is Enum)
                {
                    item.Tag = RadioTag;
                }

                if (item.HasItems)
                {
                    MarkRadioItems(item);
                }
            }
        }

        private static void HideSidebarMenu(ItemsControl menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (!item.HasItems)
                {
                    continue;
                }

                if (item.Items.OfType<MenuItem>().Any(a => IsSidebarSetting(BindingOperations.GetBinding(a, MenuItem.IsCheckedProperty)?.Path?.Path)))
                {
                    item.Visibility = Visibility.Collapsed;
                    TidySeparators(menu);
                }
                else
                {
                    HideSidebarMenu(item);
                }
            }
        }

        private static bool IsSidebarSetting(string path)
        {
            return path == "ShowSidebar" || path == "SidebarPosition";
        }

        private static FrameworkElement CreateIcon(string glyph, FrameworkElement sample, string fillKey = null, Control owner = null)
        {
            if (glyph.StartsWith("geometry:", StringComparison.Ordinal))
            {
                return CreateGeometryIcon(glyph.Substring("geometry:".Length), sample, fillKey, owner);
            }

            if (glyph.StartsWith("outline:", StringComparison.Ordinal))
            {
                return CreateOutlineIcon(glyph.Substring("outline:".Length), sample, fillKey, owner) ?? CreateIcon(AddonFallbackGlyph, sample);
            }

            var text = new TextBlock { Text = glyph };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FluentIconFont");
            if (sample is Viewbox box)
            {
                return new Viewbox { Width = box.Width, Height = box.Height, Child = text };
            }

            if (sample is TextBlock other)
            {
                text.FontSize = other.FontSize;
                text.Width = other.Width;
                text.Height = other.Height;
                text.TextAlignment = TextAlignment.Center;
            }

            return text;
        }

        private static FrameworkElement CreateStruckIcon(string glyph, FrameworkElement sample, Control owner = null)
        {
            var size = sample != null && sample.Width > 0 ? sample.Width : 16;
            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(CreateIcon(glyph, sample, owner: owner));

            Line Stroke(double thickness)
            {
                return new Line
                {
                    X1 = size * 0.12, Y1 = size * 0.12, X2 = size * 0.88, Y2 = size * 0.88,
                    StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
            }

            var gap = Stroke(size * 0.22);
            gap.SetResourceReference(Shape.StrokeProperty, "PopupBackgroundBrush");
            var line = Stroke(size * 0.08);
            BindToForeground(line, Shape.StrokeProperty, owner);
            grid.Children.Add(gap);
            grid.Children.Add(line);
            return grid;
        }

        private static FrameworkElement CreateGeometryIcon(string key, FrameworkElement sample, string fillKey = null, Control owner = null)
        {
            var size = sample != null && sample.Width > 0 ? sample.Width : 16;
            var path = new Path { Stretch = System.Windows.Media.Stretch.Uniform, Width = size, Height = size };
            if (fillKey != null)
            {
                path.Data = Application.Current?.TryFindResource(key) as Geometry;
                path.SetResourceReference(Shape.FillProperty, fillKey);
                return path;
            }

            path.SetResourceReference(Path.DataProperty, key);
            BindToForeground(path, Shape.FillProperty, owner);
            return path;
        }

        private static readonly Dictionary<string, Geometry> outlineIcons = new Dictionary<string, Geometry>();

        private static FrameworkElement CreateOutlineIcon(string keys, FrameworkElement sample, string fillKey = null, Control owner = null)
        {
            if (!outlineIcons.TryGetValue(keys, out var geometry))
            {
                var parts = keys.Split('|');
                var lines = Application.Current?.TryFindResource(parts[0]) as Geometry;
                var fill = parts.Length > 1 ? Application.Current?.TryFindResource(parts[1]) as Geometry : null;
                if (lines == null)
                {
                    return null;
                }

                var pen = new Pen(Brushes.Black, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var group = new GeometryGroup { FillRule = FillRule.Nonzero };
                group.Children.Add(lines.GetWidenedPathGeometry(pen));
                if (fill != null)
                {
                    group.Children.Add(fill);
                }

                group.Freeze();
                geometry = group;
                outlineIcons[keys] = geometry;
            }

            var size = sample != null && sample.Width > 0 ? sample.Width : 16;
            var path = new Path { Stretch = System.Windows.Media.Stretch.Uniform, Width = size, Height = size, Data = geometry };
            if (fillKey != null)
            {
                path.SetResourceReference(Shape.FillProperty, fillKey);
                return path;
            }

            BindToForeground(path, Shape.FillProperty, owner);
            return path;
        }

        private static void BindToForeground(Shape shape, DependencyProperty property, Control owner)
        {
            var binding = new Binding("Foreground");
            if (owner != null)
            {
                binding.Source = owner;
            }
            else
            {
                binding.RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1);
            }

            shape.SetBinding(property, binding);
        }
    }
}
