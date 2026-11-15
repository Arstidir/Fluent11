using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class ChipsCollapse
    {
        private const string ListTag = "Fluent11Chips";
        private const string ToggleTag = "Fluent11ChipsToggle";
        private const string ChipsName = "DetailsChips";
        private const string Collapsed = "Collapsed";
        private const string Expanded = "Expanded";
        private const string Full = "Full";

        private static Func<bool> showFull = () => false;

        private const double TwoRowsHeight = 62;

        private static readonly List<WeakReference<StackPanel>> boxes = new List<WeakReference<StackPanel>>();

        public static void Register(Func<bool> fullDetails)
        {
            showFull = fullDetails;
            EventManager.RegisterClassHandler(typeof(WrapPanel), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnSizeChanged));
            EventManager.RegisterClassHandler(typeof(StackPanel), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnBoxSizeChanged));
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        }

        public static void OnGameSelected()
        {
            boxes.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in boxes)
            {
                if (!reference.TryGetTarget(out var box))
                {
                    continue;
                }

                if (Expanded.Equals(box.Tag))
                {
                    box.Tag = Collapsed;
                }

                box.Dispatcher.BeginInvoke(new Action(() => Update(box)), DispatcherPriority.ContextIdle);
            }
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.HeightChanged || !(sender is WrapPanel panel) || !(ItemsControl.GetItemsOwner(panel) is ItemsControl list)
                || !ListTag.Equals(list.Tag))
            {
                return;
            }

            var box = FindBox(list);
            if (box != null)
            {
                Remember(box);
                Update(box);
            }
        }

        private static void OnBoxSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.HeightChanged && sender is StackPanel box && box.Name == ChipsName)
            {
                Remember(box);
                Update(box);
            }
        }

        private static void Remember(StackPanel box)
        {
            if (!boxes.Any(reference => reference.TryGetTarget(out var known) && known == box))
            {
                boxes.Add(new WeakReference<StackPanel>(box));
            }
        }

        public static void RefreshAll()
        {
            boxes.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in boxes)
            {
                if (reference.TryGetTarget(out var box))
                {
                    Update(box);
                }
            }
        }

        private static void Update(StackPanel box)
        {
            if (showFull())
            {
                box.Tag = Full;
                return;
            }

            if (Full.Equals(box.Tag))
            {
                box.Tag = null;
            }

            var isLong = Lists(box).Any(panel => panel.IsVisible && panel.ActualHeight > TwoRowsHeight + 1);
            if (!isLong)
            {
                box.Tag = null;
            }
            else if (box.Tag == null)
            {
                box.Tag = Collapsed;
            }
        }

        private static IEnumerable<WrapPanel> Lists(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is WrapPanel panel && ItemsControl.GetItemsOwner(panel) is ItemsControl list && ListTag.Equals(list.Tag))
                {
                    yield return panel;
                    continue;
                }

                foreach (var nested in Lists(child))
                {
                    yield return nested;
                }
            }
        }

        private static StackPanel FindBox(DependencyObject element)
        {
            for (var current = VisualTreeHelper.GetParent(element); current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is StackPanel panel && panel.Name == ChipsName)
                {
                    return panel;
                }
            }

            return null;
        }

        private static void OnClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !ToggleTag.Equals(button.Tag) || !(button.Parent is Panel card))
            {
                return;
            }

            var box = card.Children.OfType<StackPanel>().FirstOrDefault(panel => panel.Name == ChipsName);
            if (box == null)
            {
                return;
            }

            e.Handled = true;
            if (Expanded.Equals(box.Tag))
            {
                box.Tag = Collapsed;
                button.Dispatcher.BeginInvoke(new Action(() => button.BringIntoView()), DispatcherPriority.Loaded);
            }
            else if (Collapsed.Equals(box.Tag))
            {
                box.Tag = Expanded;
            }
        }
    }
}
