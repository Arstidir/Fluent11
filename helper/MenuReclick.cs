using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluent11Helper
{
    internal static class MenuReclick
    {
        private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(400);

        private static readonly ConditionalWeakTable<ContextMenu, WeakReference<UIElement>> owners = new ConditionalWeakTable<ContextMenu, WeakReference<UIElement>>();
        private static WeakReference<UIElement> closedTarget;
        private static WeakReference<ContextMenu> openMenu;
        private static DateTime closedAt;
        private static bool swallowing;
        private static WeakReference<DependencyObject> lastClicked;

        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened));
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnMenuClosed));
            EventManager.RegisterClassHandler(typeof(System.Windows.Window), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseDown));
            EventManager.RegisterClassHandler(typeof(System.Windows.Window), UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseUp));
        }

        private static void OnMenuOpened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ContextMenu menu))
            {
                return;
            }

            DependencyObject clicked = null;
            lastClicked?.TryGetTarget(out clicked);
            var owner = menu.PlacementTarget as UIElement ?? FindOwner(menu, clicked) ?? FindOwner(menu, Mouse.DirectlyOver as DependencyObject);
            owners.Remove(menu);
            if (owner != null)
            {
                owners.Add(menu, new WeakReference<UIElement>(owner));
            }

            openMenu = new WeakReference<ContextMenu>(menu);
        }

        public static UIElement GetOwner(ContextMenu menu)
        {
            return menu != null && owners.TryGetValue(menu, out var owner) && owner.TryGetTarget(out var element) ? element : null;
        }

        private static void OnMenuClosed(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu menu && owners.TryGetValue(menu, out var owner))
            {
                closedTarget = owner;
                closedAt = DateTime.UtcNow;
            }

            if (openMenu != null && openMenu.TryGetTarget(out var open) && open == sender)
            {
                openMenu = null;
            }
        }

        private static UIElement FindOwner(ContextMenu menu, DependencyObject start)
        {
            for (var current = start; current != null; current = Parent(current))
            {
                if (current is FrameworkElement element && element.ContextMenu == menu)
                {
                    return element;
                }
            }

            return null;
        }

        private static DependencyObject Parent(DependencyObject current)
        {
            return current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            Remember(e.OriginalSource as DependencyObject);
            swallowing = IsReclick(e.OriginalSource as DependencyObject);
            if (swallowing)
            {
                e.Handled = true;
            }
        }

        private static void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            Remember(e.OriginalSource as DependencyObject);

            if (openMenu != null && openMenu.TryGetTarget(out var menu) && owners.TryGetValue(menu, out var owner)
                && owner.TryGetTarget(out var ownerElement) && IsWithin(e.OriginalSource as DependencyObject, ownerElement))
            {
                e.Handled = true;
                ReleaseCapture();
                menu.IsOpen = false;
                openMenu = null;
                swallowing = false;
                return;
            }

            if (swallowing || IsReclick(e.OriginalSource as DependencyObject))
            {
                e.Handled = true;
                ReleaseCapture();
                closedTarget = null;
            }

            swallowing = false;
        }

        private static void ReleaseCapture()
        {
            if (Mouse.Captured is System.Windows.Controls.Primitives.ButtonBase button)
            {
                button.ReleaseMouseCapture();
            }
        }

        private static void Remember(DependencyObject source)
        {
            if (source != null)
            {
                lastClicked = new WeakReference<DependencyObject>(source);
            }
        }

        private static bool IsReclick(DependencyObject source)
        {
            if (closedTarget == null || DateTime.UtcNow - closedAt > Window || !closedTarget.TryGetTarget(out var target))
            {
                return false;
            }

            return IsWithin(source, target);
        }

        private static bool IsWithin(DependencyObject source, DependencyObject target)
        {
            for (var current = source; current != null; current = Parent(current))
            {
                if (current == target)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
