using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class DescriptionCollapse
    {
        private const string DescriptionTag = "Fluent11Description";
        private const string ToggleTag = "Fluent11DescriptionToggle";
        private const string Collapsed = "Collapsed";
        private const string Expanded = "Expanded";

        private const double CollapsedHeight = 400;
        private const double MinHidden = 40;

        private static readonly List<WeakReference<StackPanel>> clips = new List<WeakReference<StackPanel>>();

        private static Func<bool> showFull = () => false;

        public static void Register(Func<bool> fullDescription)
        {
            showFull = fullDescription;
            EventManager.RegisterClassHandler(typeof(StackPanel), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnSizeChanged));
            EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        }

        public static void OnGameSelected()
        {
            clips.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in clips)
            {
                if (reference.TryGetTarget(out var clip) && Expanded.Equals(clip.Tag))
                {
                    clip.Tag = Collapsed;
                }
            }
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.HeightChanged || !(sender is StackPanel description) || !DescriptionTag.Equals(description.Tag)
                || !(description.Parent is StackPanel clip))
            {
                return;
            }

            if (!clips.Any(reference => reference.TryGetTarget(out var known) && known == clip))
            {
                clips.Add(new WeakReference<StackPanel>(clip));
            }

            Update(clip, e.NewSize.Height);
        }

        public static void RefreshAll()
        {
            clips.RemoveAll(reference => !reference.TryGetTarget(out _));
            foreach (var reference in clips)
            {
                if (reference.TryGetTarget(out var clip)
                    && clip.Children.OfType<StackPanel>().FirstOrDefault(a => DescriptionTag.Equals(a.Tag)) is StackPanel description)
                {
                    Update(clip, description.ActualHeight);
                }
            }
        }

        private static void Update(StackPanel clip, double height)
        {
            var isLong = !showFull() && height > CollapsedHeight + MinHidden;
            if (!isLong)
            {
                clip.Tag = null;
            }
            else if (clip.Tag == null)
            {
                clip.Tag = Collapsed;
            }
        }

        private static void OnClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !ToggleTag.Equals(button.Tag) || !(button.Parent is Panel card))
            {
                return;
            }

            var clip = card.Children.OfType<StackPanel>().FirstOrDefault(panel => panel.Name == "DescriptionClip");
            if (clip == null)
            {
                return;
            }

            e.Handled = true;
            if (Expanded.Equals(clip.Tag))
            {
                clip.Tag = Collapsed;
                button.Dispatcher.BeginInvoke(new Action(() => button.BringIntoView()), DispatcherPriority.Loaded);
            }
            else if (Collapsed.Equals(clip.Tag))
            {
                clip.Tag = Expanded;
            }
        }
    }
}
