using Playnite.SDK;
using System;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class AutoColumns
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public const int MinColumns = 2;
        public const int MaxColumns = 12;
        private const double MinWidth = 100;
        private const double MaxWidth = 500;

        private static Fluent11HelperSettingsViewModel model;
        private static Fluent11HelperSettings watched;
        private static object appSettings;
        private static WeakReference<Panel> lastPanel;
        private static WeakReference<ListBox> lastList;
        private static DispatcherTimer timer;
        private static Panel waitingPanel;
        private static WeakReference<ListBox> hookedList;
        private static WeakReference<ItemsPresenter> hookedPresenter;
        private static double baseListWidth;
        private static double baseItemsWidth;
        private static int baseColumns;
        private static int baseMargin;

        public static void Register(Fluent11HelperSettingsViewModel settingsModel)
        {
            try
            {
                appSettings = PlayniteInternals.AppSettings;
                if (appSettings?.GetType().GetProperty("GridItemWidth")?.CanWrite != true)
                {
                    logger.Warn("Fluent 11: Playnite GridItemWidth not writable, Fit window is not available.");
                    return;
                }

                model = settingsModel;
                model.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(Fluent11HelperSettingsViewModel.Settings))
                    {
                        Watch();
                        Schedule();
                    }
                };
                Watch();
                if (appSettings is INotifyPropertyChanged notify)
                {
                    notify.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == "GridItemSpacing" || e.PropertyName == "GridItemWidth")
                        {
                            Schedule();
                        }
                    };
                }

                EventManager.RegisterClassHandler(typeof(Panel), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnPanelSizeChanged));
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to set up Fit window.");
            }
        }

        private static void Watch()
        {
            if (watched != null)
            {
                watched.PropertyChanged -= OnSettingChanged;
            }

            watched = model.Settings;
            watched.PropertyChanged += OnSettingChanged;
        }

        private static void OnSettingChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Fluent11HelperSettings.GridColumns) || e.PropertyName == nameof(Fluent11HelperSettings.GridFitWindow))
            {
                Schedule();
            }
        }

        private static void OnPanelSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || e.NewSize.Width <= 0 || sender.GetType().Name != "VirtualizingWrapPanel" || !(sender is Panel panel) || !panel.IsItemsHost)
            {
                return;
            }

            var owner = (panel.TemplatedParent as ItemsPresenter)?.TemplatedParent;
            var list = owner is GroupItem group ? ItemsControl.ItemsControlFromItemContainer(group) as ListBox : owner as ListBox;
            if (list == null || list.Name != "PART_ListGames" || list.TemplatedParent?.GetType().Name != "LibraryGridView" || (owner == list && list.IsGrouping))
            {
                return;
            }

            lastPanel = new WeakReference<Panel>(panel);
            lastList = new WeakReference<ListBox>(list);
            HookList(list);
            Schedule();
        }

        private static void HookList(ListBox list)
        {
            if (hookedList != null && hookedList.TryGetTarget(out var hooked))
            {
                if (hooked == list)
                {
                    return;
                }

                hooked.SizeChanged -= OnListSizeChanged;
            }

            hookedList = new WeakReference<ListBox>(list);
            hookedPresenter = null;
            baseListWidth = 0;
            list.SizeChanged += OnListSizeChanged;
        }

        private static void OnListSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged || !(sender is ListBox list) || model?.Settings?.GridFitWindow != true || !list.IsVisible
                || baseListWidth <= 0 || baseItemsWidth <= 0)
            {
                return;
            }

            var presenter = HostPresenter(list);
            var margins = (double)baseColumns * baseMargin;
            var scale = (baseItemsWidth + list.ActualWidth - baseListWidth - margins) / (baseItemsWidth - margins);
            if (presenter == null || baseItemsWidth - margins <= 0 || scale <= 0.1)
            {
                return;
            }

            presenter.Width = baseItemsWidth;
            if (presenter.LayoutTransform is ScaleTransform transform && !transform.IsFrozen)
            {
                transform.ScaleX = scale;
                transform.ScaleY = scale;
            }
            else
            {
                presenter.LayoutTransform = new ScaleTransform(scale, scale);
            }

            Schedule();
        }

        private static ItemsPresenter HostPresenter(ListBox list)
        {
            if (hookedPresenter != null && hookedPresenter.TryGetTarget(out var cached) && cached.TemplatedParent == list)
            {
                return cached;
            }

            var found = FindPresenter(list, list);
            hookedPresenter = found == null ? null : new WeakReference<ItemsPresenter>(found);
            return found;
        }

        private static ItemsPresenter FindPresenter(DependencyObject parent, ListBox list)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ItemsPresenter presenter && presenter.TemplatedParent == list)
                {
                    return presenter;
                }

                var found = FindPresenter(child, list);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool ResetScale()
        {
            if (hookedPresenter == null || !hookedPresenter.TryGetTarget(out var presenter) || presenter.ReadLocalValue(FrameworkElement.WidthProperty) == DependencyProperty.UnsetValue)
            {
                return false;
            }

            presenter.ClearValue(FrameworkElement.WidthProperty);
            presenter.ClearValue(FrameworkElement.LayoutTransformProperty);
            return true;
        }

        private static void Schedule()
        {
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    Apply();
                };
            }

            timer.Stop();
            timer.Start();
        }

        private static void Apply()
        {
            try
            {
                if (ResetScale() && lastList != null && lastList.TryGetTarget(out var scaledList))
                {
                    scaledList.UpdateLayout();
                }

                var settings = model?.Settings;
                if (settings == null || !settings.GridFitWindow || lastPanel == null || !lastPanel.TryGetTarget(out var panel))
                {
                    return;
                }

                if (!panel.IsVisible)
                {
                    WaitUntilVisible(panel);
                    return;
                }

                var spacing = Reflect.GetMember(appSettings, "GridItemSpacing") is int value ? value : 0;
                baseMargin = spacing / 2 * 2;
                var width = Compute(panel.ActualWidth, settings.GridColumns, baseMargin, out baseColumns);
                var current = Convert.ToDouble(Reflect.GetMember(appSettings, "GridItemWidth"));
                if (Math.Abs(current - width) >= 1)
                {
                    appSettings.GetType().GetProperty("GridItemWidth").SetValue(appSettings, width);
                    RemeasurePanels();
                }

                RememberWidths();
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to fit the grid to the window.");
            }
        }

        private static void RememberWidths()
        {
            baseListWidth = 0;
            if (lastList == null || !lastList.TryGetTarget(out var list) || !(HostPresenter(list) is ItemsPresenter presenter))
            {
                return;
            }

            baseListWidth = list.ActualWidth;
            baseItemsWidth = presenter.ActualWidth;
        }

        private static void WaitUntilVisible(Panel panel)
        {
            if (waitingPanel != null)
            {
                waitingPanel.IsVisibleChanged -= OnWaitingPanelVisible;
            }

            waitingPanel = panel;
            panel.IsVisibleChanged += OnWaitingPanelVisible;
        }

        private static void OnWaitingPanelVisible(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool visible) || !visible || !(sender is Panel panel))
            {
                return;
            }

            panel.IsVisibleChanged -= OnWaitingPanelVisible;
            waitingPanel = null;
            panel.Dispatcher.BeginInvoke(new Action(Apply), DispatcherPriority.Render);
        }

        private static void RemeasurePanels()
        {
            if (lastList == null || !lastList.TryGetTarget(out var list))
            {
                return;
            }

            list.UpdateLayout();
            foreach (var panel in TilePanels(list))
            {
                var field = SizeOfFirstItemField(panel.GetType());
                if (field == null)
                {
                    return;
                }

                field.SetValue(panel, Size.Empty);
                panel.InvalidateMeasure();
            }
        }

        private static System.Collections.Generic.IEnumerable<Panel> TilePanels(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Panel panel && panel.IsItemsHost && panel.GetType().Name == "VirtualizingWrapPanel")
                {
                    yield return panel;
                    continue;
                }

                foreach (var found in TilePanels(child))
                {
                    yield return found;
                }
            }
        }

        internal static FieldInfo SizeOfFirstItemField(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField("sizeOfFirstItem", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null && field.FieldType == typeof(Size))
                {
                    return field;
                }
            }

            return null;
        }

        private static double Compute(double available, int targetColumns, int margin, out int resultColumns)
        {
            double Width(int n) => Math.Floor(available / n) - margin - 1;

            var columns = Math.Max(MinColumns, Math.Min(MaxColumns, targetColumns));
            while (columns > 1 && Width(columns) < MinWidth)
            {
                columns--;
            }

            while (Width(columns) > MaxWidth)
            {
                columns++;
            }

            resultColumns = columns;
            return Math.Max(60, Math.Min(700, Width(columns)));
        }
    }
}
