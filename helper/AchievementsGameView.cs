using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class AchievementsGameView
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string ViewFile = "Views\\Fluent11AchievementsGameView.xaml";

        internal static readonly string[] CopiedBrushKeys =
        {
            "GlyphBrush", "HighlightGlyphBrush", "NormalBorderBrush", "PopupBackgroundBrush", "PopupBorderBrush",
            "StandardWindowBackgroundBrush", "StandardWindowBorderBrush", "TextBrush", "TextBrushDark", "TextBrushDarker",
            "WindowBackgourndBrush", "WindowBaseBackgroundBrush"
        };

        public static void TryApply(Window window, string themeDir)
        {
            try
            {
                var control = FindControl(window.Content as DependencyObject, 4);
                if (control == null)
                {
                    return;
                }

                var path = Path.Combine(themeDir, ViewFile);
                if (!File.Exists(path))
                {
                    logger.Warn($"Fluent 11: achievements view not found in theme: {path}");
                    return;
                }

                object content;
                using (var stream = File.OpenRead(path))
                {
                    content = XamlReader.Load(stream);
                }

                control.Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
                CoverFormat.Apply(control);
                if (content is FrameworkElement view)
                {
                    foreach (var type in new[] { typeof(ScrollViewer), typeof(ScrollBar), typeof(ProgressBar), typeof(Button),
                        typeof(ToggleButton), typeof(TextBox), typeof(ToolTip) })
                    {
                        if (Application.Current.TryFindResource(type) is Style style)
                        {
                            view.Resources[type] = style;
                        }
                    }

                    if (view.FindName("AchProgressPercent") is TextBlock percent)
                    {
                        var binding = new MultiBinding { Converter = new PercentConverter() };
                        binding.Bindings.Add(new Binding("SummaryItems[0].UnlockedAchievements"));
                        binding.Bindings.Add(new Binding("SummaryItems[0].TotalAchievements"));
                        percent.SetBinding(TextBlock.TextProperty, binding);
                    }

                    if (view.FindName("AchLockedCount") is System.Windows.Documents.Run lockedCount)
                    {
                        var binding = new MultiBinding { Converter = new LockedCountConverter() };
                        binding.Bindings.Add(new Binding("SummaryItems[0].TotalAchievements"));
                        binding.Bindings.Add(new Binding("SummaryItems[0].UnlockedAchievements"));
                        lockedCount.SetBinding(System.Windows.Documents.Run.TextProperty, binding);
                    }

                    SetupTabs(view);
                }

                control.Content = content;
                SeamlessWindow.Apply(window);
                window.MinWidth = 600;
                window.MinHeight = 480;
                window.GetType().GetProperty("ShowMinimizeButton")?.SetValue(window, false);
                window.GetType().GetProperty("ShowMaximizeButton")?.SetValue(window, false);

                foreach (var key in CopiedBrushKeys)
                {
                    window.Resources.Remove(key);
                }
                control.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!HideTitleBarRefresh(window))
                    {
                        window.Dispatcher.BeginInvoke(new Action(() => HideTitleBarRefresh(window)), DispatcherPriority.ApplicationIdle);
                    }
                }), DispatcherPriority.Loaded);
                logger.Info("Fluent 11: achievements window restyled.");
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to restyle achievements window, keeping the original.");
            }
        }

        private const string TitleBarRefreshName = "PlayAch_TitleBarRefreshButton";

        private static bool HideTitleBarRefresh(Window window)
        {
            try
            {
                var button = FindByName(window, TitleBarRefreshName);
                if (button != null)
                {
                    button.Visibility = Visibility.Collapsed;
                    return true;
                }
            }
            catch (Exception e)
            {
                logger.Debug($"Fluent 11: title bar refresh not hidden: {e.Message}");
            }

            return false;
        }

        private static FrameworkElement FindByName(DependencyObject element, string name)
        {
            if (element is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var found = FindByName(VisualTreeHelper.GetChild(element, i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private class PercentConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                try
                {
                    var unlocked = System.Convert.ToDouble(values[0], CultureInfo.InvariantCulture);
                    var total = System.Convert.ToDouble(values[1], CultureInfo.InvariantCulture);
                    return total > 0 ? Math.Floor(100 * unlocked / total).ToString(CultureInfo.CurrentCulture) + " %" : string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private class LockedCountConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                try
                {
                    var total = System.Convert.ToInt32(values[0], CultureInfo.InvariantCulture);
                    var unlocked = System.Convert.ToInt32(values[1], CultureInfo.InvariantCulture);
                    return Math.Max(0, total - unlocked).ToString(CultureInfo.CurrentCulture);
                }
                catch
                {
                    return string.Empty;
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private static readonly string[] TabNames = { "AchTabAll", "AchTabUnlocked", "AchTabLocked" };

        private static void SetupTabs(FrameworkElement view)
        {
            var tabs = TabNames.Select(a => view.FindName(a) as RadioButton).ToArray();
            if (tabs.Any(a => a == null))
            {
                return;
            }

            for (var i = 0; i < tabs.Length; i++)
            {
                var showUnlocked = i != 2;
                var showLocked = i != 1;
                tabs[i].Checked += (s, e) => SetUnlockFilters(view.DataContext, showUnlocked, showLocked);
            }

            view.Loaded += (s, e) =>
            {
                var unlocked = FindToggle(view.DataContext, "Unlocked");
                var locked = FindToggle(view.DataContext, "Locked");
                if (unlocked == null || locked == null)
                {
                    if (view.FindName("AchTabs") is UIElement bar)
                    {
                        bar.Visibility = Visibility.Collapsed;
                    }

                    return;
                }

                var showUnlocked = true.Equals(Reflect.GetMember(unlocked, "IsChecked"));
                var showLocked = true.Equals(Reflect.GetMember(locked, "IsChecked"));
                var index = showUnlocked && !showLocked ? 1 : showLocked && !showUnlocked ? 2 : 0;
                tabs[index].IsChecked = true;
            };
        }

        private static object FindToggle(object model, string icon)
        {
            if (Reflect.GetMember(Reflect.GetMember(model, "AchievementsControlBar"), "Items") is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (Reflect.GetMember(item, "Icon")?.ToString() == icon && Reflect.GetMember(item, "IsChecked") is bool)
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        private static void SetUnlockFilters(object model, bool showUnlocked, bool showLocked)
        {
            try
            {
                var unlocked = FindToggle(model, "Unlocked");
                var locked = FindToggle(model, "Locked");
                if (unlocked == null || locked == null)
                {
                    return;
                }

                if (showUnlocked)
                {
                    SetToggle(unlocked, true);
                    SetToggle(locked, showLocked);
                }
                else
                {
                    SetToggle(locked, true);
                    SetToggle(unlocked, false);
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to switch achievements tab.");
            }
        }

        private static void SetToggle(object toggle, bool value)
        {
            var property = toggle.GetType().GetProperty("IsChecked");
            if (property?.CanWrite == true && !value.Equals(property.GetValue(toggle)))
            {
                property.SetValue(toggle, value);
            }
        }

        private static void ShowMoreMenu(UIElement target, Guid gameId, object model)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            var edit = new MenuItem { Header = "Edit achievements" };
            edit.Click += (s, a) => CallPlugin("OpenManageAchievementsView", gameId, "Editor");
            menu.Items.Add(edit);
            if (true.Equals(Reflect.GetMember(model, "CanEditLocalAchievements")))
            {
                var file = new MenuItem { Header = "Edit local file" };
                file.Click += (s, a) => CallPlugin("OpenLocalAchievementsEditorView", gameId, null);
                menu.Items.Add(file);
            }

            menu.Closed += (s, a) => Mouse.Synchronize();
            AcrylicPopups.Prepare(menu);
            menu.IsOpen = true;
        }

        private static readonly string[] FilterIcons = { "Hidden", "Favorite" };

        private static void ShowFilterMenu(UIElement target, object model)
        {
            var bar = Reflect.GetMember(model, "AchievementsControlBar");
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            if (Reflect.GetMember(bar, "Items") is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    var icon = Reflect.GetMember(item, "Icon")?.ToString();
                    if (!FilterIcons.Contains(icon) || false.Equals(Reflect.GetMember(item, "EffectiveIsVisible"))
                        || !(Reflect.GetMember(item, "IsChecked") is bool))
                    {
                        continue;
                    }

                    var entry = new MenuItem { Header = Reflect.GetMember(item, "Content")?.ToString() ?? icon, IsCheckable = true };
                    entry.SetBinding(MenuItem.IsCheckedProperty, new Binding("IsChecked") { Source = item, Mode = BindingMode.TwoWay });
                    entry.StaysOpenOnClick = true;
                    menu.Items.Add(entry);
                }
            }

            if (menu.Items.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "No filters available", IsEnabled = false });
            }

            menu.Closed += (s, a) => Mouse.Synchronize();
            AcrylicPopups.Prepare(menu);
            menu.IsOpen = true;
        }

        private static UserControl FindControl(DependencyObject element, int depth)
        {
            if (element == null || depth < 0)
            {
                return null;
            }

            if (element is UserControl user && element.GetType().Name == "ViewAchievementsControl")
            {
                return user;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
            {
                var found = FindControl(child, depth - 1);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void OnClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement control) || !(e.OriginalSource is ButtonBase button))
            {
                return;
            }

            var model = control.DataContext;
            var gameId = (Reflect.GetMember(model, "GameId") ?? Reflect.GetMember(model, "_gameId")) is Guid id ? id : Guid.Empty;
            if (button.Uid == "AchSortMenu")
            {
                ShowSortMenu(button, model);
                e.Handled = true;
                return;
            }

            if (button.Uid == "AchFilterMenu")
            {
                ShowFilterMenu(button, model);
                e.Handled = true;
                return;
            }

            switch (button.Tag as string)
            {
                case "AchRefresh":
                    if (Reflect.GetMember(model, "RefreshGameCommand") is ICommand command && command.CanExecute(null))
                    {
                        command.Execute(null);
                    }
                    else
                    {
                        logger.Warn("Fluent 11: refresh of Playnite Achievements not found.");
                    }

                    button.Dispatcher.BeginInvoke(new Action(Mouse.Synchronize), DispatcherPriority.Input);
                    e.Handled = true;
                    break;
                case "AchMoreMenu":
                    ShowMoreMenu(button, gameId, model);
                    e.Handled = true;
                    break;
            }
        }

        private static void CallPlugin(string name, Guid gameId, string tab)
        {
            try
            {
                var pluginType = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "PlayniteAchievements")?
                    .GetType("PlayniteAchievements.PlayniteAchievementsPlugin");
                var plugin = pluginType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var method = pluginType?.GetMethods().FirstOrDefault(a => a.Name == name && a.GetParameters().FirstOrDefault()?.ParameterType == typeof(Guid));
                if (plugin == null || method == null)
                {
                    logger.Warn($"Fluent 11: Playnite Achievements has no {name}.");
                    return;
                }

                var args = method.GetParameters().Select((p, i) =>
                    i == 0 ? gameId
                    : p.ParameterType.IsEnum && tab != null ? Enum.Parse(p.ParameterType, tab)
                    : p.HasDefaultValue ? p.DefaultValue
                    : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray();
                method.Invoke(plugin, args);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Fluent 11: failed to call Playnite Achievements {name}.");
            }
        }

        private static void ShowSortMenu(UIElement target, object model)
        {
            var current = Reflect.GetMember(model, "CurrentSortPath") as string;
            var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
            menu.Items.Add(SortItem("Default", null, ListSortDirection.Descending, current == null, model));
            menu.Items.Add(new Separator());
            menu.Items.Add(SortItem("Unlock date", "UnlockTime", ListSortDirection.Descending, current == "UnlockTime", model));
            menu.Items.Add(SortItem("Rarity", "RaritySortValue", ListSortDirection.Ascending, current == "RaritySortValue", model));
            menu.Items.Add(SortItem("Name", "DisplayName", ListSortDirection.Ascending, current == "DisplayName", model));
            AcrylicPopups.Prepare(menu);
            menu.IsOpen = true;
        }

        private static MenuItem SortItem(string header, string path, ListSortDirection direction, bool isChecked, object model)
        {
            var item = MenuPolish.Radio(new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked });
            item.Click += (s, a) =>
            {
                try
                {
                    if (path == null)
                    {
                        model.GetType().GetMethod("ResetSortToDefault", Type.EmptyTypes)?.Invoke(model, null);
                    }
                    else
                    {
                        model.GetType().GetMethod("SortDataGrid", new[] { typeof(string), typeof(ListSortDirection) })?
                            .Invoke(model, new object[] { path, direction });
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e, "Fluent 11: failed to sort achievements.");
                }
            };
            return item;
        }
    }
}
