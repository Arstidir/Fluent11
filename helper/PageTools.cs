using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Fluent11Helper
{
    internal static class PageTools
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private static readonly ControlTemplate HostTemplate = CreateHostTemplate();

        private static ControlTemplate CreateHostTemplate()
        {
            var template = new ControlTemplate(typeof(UserControl)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            template.Seal();
            return template;
        }

        internal static UserControl CreateHost()
        {
            return new UserControl
            {
                Template = HostTemplate,
                Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)
            };
        }

        internal static void BindPlayniteSettings(FrameworkElement root)
        {
            BindVisibility(root?.FindName("SearchBox") as UIElement, "ShowTopPanelSearchBox");
            BindVisibility(root?.FindName("PanelSeparator") as UIElement, "ShowPanelSeparators");
            SmoothScroll.AttachPage(root);
        }

        private static void BindVisibility(UIElement element, string setting)
        {
            if (element == null)
            {
                return;
            }

            try
            {
                var appSettings = PlayniteInternals.AppSettings;
                if (appSettings?.GetType().GetProperty(setting) == null)
                {
                    return;
                }

                BindingOperations.SetBinding(element, UIElement.VisibilityProperty, new Binding(setting)
                {
                    Source = appSettings,
                    Converter = new BooleanToVisibilityConverter()
                });
            }
            catch (Exception e)
            {
                logger.Error(e, $"Fluent 11: failed to bind visibility to {setting}.");
            }
        }

        internal static void ShowMenu(UIElement target, IEnumerable<object> items)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            foreach (var item in items)
            {
                menu.Items.Add(item);
            }

            OpenBarMenu(menu, target);
        }

        internal static void OpenBarMenu(ContextMenu menu, UIElement target)
        {
            AcrylicPopups.Prepare(menu);
            if (target is FrameworkElement owner && PageBar(owner) is Grid root)
            {
                PlaceBelowBar(menu, owner, () => root.RowDefinitions[0].ActualHeight - owner.TranslatePoint(new Point(0, 0), root).Y);
            }

            menu.IsOpen = true;
        }

        private static Grid PageBar(FrameworkElement owner)
        {
            for (DependencyObject current = owner; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is UserControl host && host.Content is Grid root && root.RowDefinitions.Count > 0)
                {
                    return root;
                }
            }

            return null;
        }

        private const double BarMenuGap = 4;

        internal static void PlaceBelowBar(ContextMenu menu, FrameworkElement owner, Func<double> barBottom)
        {
            SetPlacement(menu, owner, (popupSize, targetSize, scale) =>
            {
                var top = barBottom() + BarMenuGap - (AcrylicPopups.IsAcrylic(menu) ? 0 : 4);
                return new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, top * scale.DpiScaleY), PopupPrimaryAxis.Horizontal);
            });
        }

        internal static void PlaceBesideBar(ContextMenu menu, FrameworkElement owner, Func<double> barRight)
        {
            SetPlacement(menu, owner, (popupSize, targetSize, scale) =>
            {
                var acrylic = AcrylicPopups.IsAcrylic(menu);
                var left = barRight() + BarMenuGap - (acrylic ? 0 : 8);
                var bottom = acrylic ? 0 : 12;
                return new CustomPopupPlacement(new Point(left * scale.DpiScaleX, targetSize.Height - popupSize.Height + bottom * scale.DpiScaleY),
                    PopupPrimaryAxis.Vertical);
            });
        }

        private static void SetPlacement(ContextMenu menu, FrameworkElement owner, Func<Size, Size, DpiScale, CustomPopupPlacement> place)
        {
            menu.PlacementTarget = owner;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[] { place(popupSize, targetSize, VisualTreeHelper.GetDpi(owner)) };
            menu.Placement = PlacementMode.Custom;
        }

        internal static Guid? FindGameId(DependencyObject element)
        {
            for (var current = element; current != null; current = GetParent(current))
            {
                if (current is FrameworkElement fe && fe.DataContext != null)
                {
                    var property = fe.DataContext.GetType().GetProperty("GameId");
                    if (property?.GetValue(fe.DataContext) is Guid id)
                    {
                        return id;
                    }
                }
            }

            return null;
        }

        private static DependencyObject GetParent(DependencyObject element)
        {
            return element is Visual || element is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        internal static IEnumerable<object> BuildMenu(List<GameMenuItem> items, List<Game> games)
        {
            var paths = items.Select(a => (a.MenuSection ?? string.Empty).TrimStart('@')
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim()).ToList()).ToList();
            var skipFirst = paths.All(a => a.Count > 0) && paths.Select(a => a[0]).Distinct().Count() == 1;

            var root = new List<object>();
            var submenus = new Dictionary<string, MenuItem>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var path = skipFirst ? paths[i].Skip(1).ToList() : paths[i];

                IList<object> target = root;
                MenuItem parent = null;
                var key = string.Empty;
                foreach (var part in path)
                {
                    key += "|" + part;
                    if (!submenus.TryGetValue(key, out var sub))
                    {
                        sub = new MenuItem { Header = part };
                        submenus[key] = sub;
                        if (parent == null)
                        {
                            root.Add(sub);
                        }
                        else
                        {
                            parent.Items.Add(sub);
                        }
                    }

                    parent = sub;
                }

                object entry;
                if (item.Description == "-")
                {
                    entry = new Separator();
                }
                else
                {
                    var menuItem = new MenuItem { Header = item.Description };
                    var source = item;
                    menuItem.Click += (s, a) => source.Action?.Invoke(new GameMenuItemActionArgs { Games = games, SourceItem = source });
                    entry = menuItem;
                }

                if (parent == null)
                {
                    root.Add(entry);
                }
                else
                {
                    parent.Items.Add(entry);
                }
            }

            return root;
        }
    }
}
