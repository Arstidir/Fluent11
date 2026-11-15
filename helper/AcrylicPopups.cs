using Playnite.SDK;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class AcrylicPopups
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMSBT_TRANSIENTWINDOW = 3;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);
        private const string TemplateKey = "Fluent11AcrylicContextMenuTemplate";
        private const string AcrylicBrushKey = "PopupAcrylicBrush";

        internal static readonly FieldInfo ParentPopupField =
            typeof(ContextMenu).GetField("_parentPopup", BindingFlags.NonPublic | BindingFlags.Instance);
        internal static readonly MethodInfo HookupParentPopupMethod =
            typeof(ContextMenu).GetMethod("HookupParentPopup", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Func<bool> enabled;
        private static Func<bool> dark;

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins
        {
            public int Left, Right, Top, Bottom;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        private const int WM_NCACTIVATE = 0x0086;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly ConditionalWeakTable<ContextMenu, object> themeMenus = new ConditionalWeakTable<ContextMenu, object>();
        private static readonly ConditionalWeakTable<ContextMenu, object> foreignMenus = new ConditionalWeakTable<ContextMenu, object>();
        private static readonly ConditionalWeakTable<ContextMenu, object> watched = new ConditionalWeakTable<ContextMenu, object>();

        public static void Register(Func<bool> isEnabled, Func<bool> isDark)
        {
            if (!MicaBackdrop.IsSupported())
            {
                return;
            }

            enabled = isEnabled;
            dark = isDark;
            EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler(OnContextMenuOpening));
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseButton));
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnMouseButton));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnOpened));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnClosed));
            EventManager.RegisterClassHandler(typeof(MenuItem), UIElement.MouseEnterEvent, new MouseEventHandler(OnItemEnter));
            EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
            SyncResource();
        }

        public static bool IsAcrylic(ContextMenu menu)
        {
            return ParentPopupField?.GetValue(menu) is Popup popup && !popup.AllowsTransparency;
        }

        private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            SyncResource();
            Prepare((sender as FrameworkElement)?.ContextMenu);
        }

        private static void OnMouseButton(object sender, MouseButtonEventArgs e)
        {
            SyncResource();
            var current = e.OriginalSource as DependencyObject;
            var menuFound = false;
            var popupFound = false;
            while (current != null && !(current is Window) && !(menuFound && popupFound))
            {
                if (!menuFound && current is FrameworkElement element && element.ContextMenu != null)
                {
                    Prepare(element.ContextMenu);
                    menuFound = true;
                }

                if (!popupFound && current is Control control && FindDropdownPopup(control) is Popup dropdown)
                {
                    WatchPopup(dropdown);
                    popupFound = true;
                }

                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
        }

        internal static void Prepare(ContextMenu menu)
        {
            try
            {
                if (enabled == null || menu == null || menu.IsOpen)
                {
                    return;
                }

                if (!enabled())
                {
                    Restore(menu);
                    return;
                }

                if (!IsThemeMenu(menu))
                {
                    return;
                }

                if (ParentPopupField?.GetValue(menu) == null)
                {
                    HookupParentPopupMethod?.Invoke(menu, null);
                }

                SetTransparent(menu, false);
                Watch(menu);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: failed to prepare acrylic menu.");
            }
        }

        private static void Watch(ContextMenu menu)
        {
            if (!watched.TryGetValue(menu, out _))
            {
                watched.Add(menu, null);
                PresentationSource.AddSourceChangedHandler(menu, OnSourceChanged);
            }
        }

        private static bool IsThemeMenu(ContextMenu menu)
        {
            if (themeMenus.TryGetValue(menu, out _))
            {
                return true;
            }

            if (foreignMenus.TryGetValue(menu, out _))
            {
                return false;
            }

            var isTheme = CheckThemeMenu(menu);
            (isTheme ? themeMenus : foreignMenus).Add(menu, null);
            return isTheme;
        }

        private static bool CheckThemeMenu(ContextMenu menu)
        {
            bool isTheme;
            if (menu.Template == null)
            {
                isTheme = menu.TryFindResource(menu.GetType()) is Style style && HasTemplateSetter(style);
            }
            else
            {
                menu.ApplyTemplate();
                isTheme = VisualTreeHelper.GetChildrenCount(menu) > 0 && VisualTreeHelper.GetChild(menu, 0) is Grid root &&
                    root.Style != null && root.Style == menu.TryFindResource("PopupRoot");
            }

            return isTheme && menu.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue;
        }

        private static bool HasTemplateSetter(Style style)
        {
            for (var current = style; current != null; current = current.BasedOn)
            {
                foreach (var setter in current.Setters)
                {
                    if (setter is Setter s && s.Property == Control.TemplateProperty)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void OnSourceChanged(object sender, SourceChangedEventArgs e)
        {
            if (sender is ContextMenu menu && e.NewSource is HwndSource source && enabled() && IsAcrylic(menu))
            {
                Apply(source, menu);
            }
        }

        private static void OnOpened(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            if (!enabled())
            {
                return;
            }

            menu.Dispatcher.BeginInvoke(new Action(() => Diagnose(menu)), DispatcherPriority.Loaded);
            if (!IsAcrylic(menu))
            {
                return;
            }

            if (PresentationSource.FromVisual(menu) is HwndSource source)
            {
                if (source.CompositionTarget != null && source.CompositionTarget.BackgroundColor != Colors.Transparent)
                {
                    Apply(source, menu);
                }
                else
                {
                    SendMessage(source.Handle, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
                }
            }
        }

        private const string TransparentKey = "Fluent11PopupTransparent";
        private static bool? resourceAcrylic;

        private static void SyncResource()
        {
            var on = enabled();
            if (resourceAcrylic == on || Application.Current == null)
            {
                return;
            }

            resourceAcrylic = on;
            if (on)
            {
                Application.Current.Resources[TransparentKey] = false;
            }
            else
            {
                Application.Current.Resources.Remove(TransparentKey);
            }
        }

        private static readonly ConditionalWeakTable<Popup, object> watchedPopups = new ConditionalWeakTable<Popup, object>();

        private static Popup FindDropdownPopup(Control control)
        {
            var template = control.Template;
            if (template == null)
            {
                return null;
            }

            var popup = template.FindName("PART_Popup", control) as Popup ?? template.FindName("Popup", control) as Popup;
            return popup?.Tag as string == "Fluent11Acrylic" ? popup : null;
        }

        private static Popup FindSubmenuPopup(MenuItem item)
        {
            return item.Template?.FindName("Fluent11SubmenuPopup", item) as Popup;
        }

        private static void OnItemEnter(object sender, MouseEventArgs e)
        {
            if (sender is MenuItem item && item.HasItems)
            {
                SyncResource();
                WatchPopup(FindSubmenuPopup(item));
            }
        }

        internal static void WatchPopup(Popup popup)
        {
            if (enabled == null || popup?.Child == null || watchedPopups.TryGetValue(popup, out _))
            {
                return;
            }

            watchedPopups.Add(popup, null);
            PresentationSource.AddSourceChangedHandler(popup.Child, (s, e) =>
            {
                if (!(e.NewSource is HwndSource source))
                {
                    return;
                }

                var acrylic = enabled() && !popup.AllowsTransparency;
                if (popup.Child is Calendar calendar)
                {
                    calendar.Tag = acrylic ? "Fluent11Acrylic" : null;
                }

                if (acrylic)
                {
                    Apply(source, null);
                }
            });
            popup.Opened += (s, e) =>
            {
                if (enabled() && !popup.AllowsTransparency && PresentationSource.FromVisual(popup.Child) is HwndSource source &&
                    source.CompositionTarget != null && source.CompositionTarget.BackgroundColor != Colors.Transparent)
                {
                    Apply(source, null);
                }
            };
        }

        private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (!(e.OriginalSource is MenuItem item) || item != sender || !enabled())
            {
                return;
            }

            var popup = FindSubmenuPopup(item);
            WatchPopup(popup);
            if (popup?.Child != null && popup.IsOpen && !popup.AllowsTransparency &&
                PresentationSource.FromVisual(popup.Child) is HwndSource source &&
                source.CompositionTarget != null && source.CompositionTarget.BackgroundColor != Colors.Transparent)
            {
                Apply(source, null);
            }
        }

        private const int DWMWA_CLOAK = 13;

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        private static void CloakUntilRendered(HwndSource source)
        {
            var hwnd = source.Handle;
            if (IsWindowVisible(hwnd))
            {
                return;
            }

            var cloak = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref cloak, sizeof(int)) != 0)
            {
                return;
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            EventHandler rendered = null;
            void Show()
            {
                timer.Stop();
                source.ContentRendered -= rendered;
                if (!source.IsDisposed)
                {
                    var uncloak = 0;
                    DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref uncloak, sizeof(int));
                }
            }

            rendered = (s, e) => Show();
            timer.Tick += (s, e) => Show();
            source.ContentRendered += rendered;
            timer.Start();
        }

        private static void Apply(HwndSource source, ContextMenu menu)
        {
            try
            {
                if (source.CompositionTarget == null)
                {
                    return;
                }

                var hwnd = source.Handle;
                var corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                var isDark = dark() ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref isDark, sizeof(int));
                var noBorder = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                var backdrop = DWMSBT_TRANSIENTWINDOW;
                var result = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                if (result != 0)
                {
                    logger.Warn($"Fluent 11 acrylic check: Windows refused the acrylic backdrop (0x{result:X8}) for {Describe(menu)}.");
                    if (menu != null)
                    {
                        RestoreBackground(menu);
                    }

                    return;
                }

                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                CloakUntilRendered(source);
                SendMessage(hwnd, WM_NCACTIVATE, new IntPtr(1), IntPtr.Zero);
                menu?.SetResourceReference(Control.BackgroundProperty, AcrylicBrushKey);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11: failed to set acrylic menu.");
            }
        }

        private static void OnClosed(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            menu.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!menu.IsOpen)
                    {
                        if (enabled() && IsThemeMenu(menu))
                        {
                            SetTransparent(menu, false);
                            Watch(menu);
                        }
                        else
                        {
                            Restore(menu);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Fluent 11: failed to reset acrylic menu.");
                }
            }));
        }

        private static readonly ConditionalWeakTable<ContextMenu, object> diagnosed = new ConditionalWeakTable<ContextMenu, object>();

        private static void Diagnose(ContextMenu menu)
        {
            try
            {
                if (!menu.IsOpen || !enabled() || diagnosed.TryGetValue(menu, out _))
                {
                    return;
                }

                string problem = null;
                var source = PresentationSource.FromVisual(menu) as HwndSource;
                if (foreignMenus.TryGetValue(menu, out _))
                {
                    if (CheckThemeMenu(menu))
                    {
                        problem = "was classified as not a theme menu earlier, looks like one now";
                    }
                }
                else if (!IsAcrylic(menu))
                {
                    problem = ParentPopupField?.GetValue(menu) == null
                        ? "has no popup yet (not prepared)"
                        : $"popup still transparent (prepared: {watched.TryGetValue(menu, out _)}, theme menu: {themeMenus.TryGetValue(menu, out _)})";
                }
                else if (source?.CompositionTarget != null && source.CompositionTarget.BackgroundColor != Colors.Transparent)
                {
                    problem = "window was not set up for acrylic";
                }
                else if (menu.Template != menu.TryFindResource(TemplateKey))
                {
                    problem = "acrylic template not applied";
                }

                if (problem != null)
                {
                    diagnosed.Add(menu, null);
                    logger.Warn($"Fluent 11 acrylic check: {Describe(menu)} opened without acrylic: {problem}.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Fluent 11 acrylic check failed.");
            }
        }

        private static string Describe(ContextMenu menu)
        {
            if (menu == null)
            {
                return "a popup";
            }

            var target = menu.PlacementTarget as FrameworkElement;
            var where = target == null ? "no target" : string.IsNullOrEmpty(target.Name) ? target.GetType().Name : target.Name;
            return $"{menu.GetType().Name} ({where}, {menu.Items.Count} items)";
        }

        private static void SetTransparent(ContextMenu menu, bool transparent)
        {
            if (!(ParentPopupField?.GetValue(menu) is Popup popup) || popup.IsOpen)
            {
                return;
            }

            if (!transparent && !(menu.TryFindResource(TemplateKey) is ControlTemplate))
            {
                return;
            }

            if (popup.AllowsTransparency != transparent)
            {
                popup.AllowsTransparency = transparent;
                menu.CoerceValue(ContextMenu.HasDropShadowProperty);
            }

            if (!transparent)
            {
                menu.SetResourceReference(Control.TemplateProperty, TemplateKey);
            }
        }

        private static void Restore(ContextMenu menu)
        {
            if (!IsAcrylic(menu))
            {
                return;
            }

            SetTransparent(menu, true);
            if (menu.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue &&
                menu.Template == menu.TryFindResource(TemplateKey))
            {
                menu.ClearValue(Control.TemplateProperty);
            }

            RestoreBackground(menu);
        }

        private static void RestoreBackground(ContextMenu menu)
        {
            if (menu.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue &&
                menu.Background == menu.TryFindResource(AcrylicBrushKey))
            {
                menu.ClearValue(Control.BackgroundProperty);
            }
        }
    }
}
