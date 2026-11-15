using Playnite.SDK;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Shapes;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class HltbSelectDialog
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public const string ControlName = "HowLongToBeatSelect";

        private const string LinkTemplateXaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
    <StackPanel Orientation='Horizontal' Background='Transparent'>
        <TextBlock Text='{TemplateBinding Content}' FontSize='{TemplateBinding FontSize}' Foreground='{TemplateBinding Foreground}' />
        <TextBlock Text='&#xE8A7;' FontFamily='{DynamicResource FluentIconFont}' FontSize='12' Margin='6,1,0,0'
                   VerticalAlignment='Center' Foreground='{TemplateBinding Foreground}' />
    </StackPanel>
</ControlTemplate>";

        public static readonly string[] ElementNames = { "SelectableContent", "PART_Tabs", "ButtonSelect", "lbSelectable", "PART_SearchPane", "PART_ManualPane", "PART_SelectPlatform", "SearchElement", "PART_BtSearch", "PART_Vndb", "PART_ManualMainStoryH", "PART_ManualHltbId", "PART_BtOpenWebsite" };

        private const string ItemStyleXaml = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='ListBoxItem'>
    <Setter Property='Margin' Value='0,0,0,8' />
    <Setter Property='Padding' Value='0' />
    <Setter Property='HorizontalContentAlignment' Value='Stretch' />
    <Setter Property='FocusVisualStyle' Value='{x:Null}' />
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='ListBoxItem'>
                <Grid>
                    <Border x:Name='Card' Background='{DynamicResource SurfaceCardBrush}' BorderBrush='{DynamicResource CardStrokeBrush}'
                            BorderThickness='1' CornerRadius='{DynamicResource CardCornerRadius}' />
                    <Border x:Name='Hover' Visibility='Collapsed'
                            Background='{DynamicResource SubtleHoverBrush}' CornerRadius='{DynamicResource CardCornerRadius}' />
                    <ContentPresenter />
                </Grid>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsMouseOver' Value='True'>
                        <Setter TargetName='Hover' Property='Visibility' Value='Visible' />
                    </Trigger>
                    <Trigger Property='IsSelected' Value='True'>
                        <Setter TargetName='Card' Property='BorderBrush' Value='{DynamicResource AccentBrush}' />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>";

        public static void TryApply(Window window)
        {
            try
            {
                if (!(window.Content is UserControl control) || control.GetType().Name != ControlName)
                {
                    return;
                }

                var content = control.FindName("SelectableContent") as DockPanel;
                var tabs = control.FindName("PART_Tabs") as TabControl;
                var select = control.FindName("ButtonSelect") as Button;
                var list = control.FindName("lbSelectable") as ListBox;
                var searchPane = control.FindName("PART_SearchPane") as FrameworkElement;
                var manualPane = control.FindName("PART_ManualPane") as FrameworkElement;
                var buttons = select?.Parent as Grid;
                if (content == null || tabs == null || list == null || buttons == null || searchPane == null || manualPane == null
                    || buttons.Parent != content || tabs.Parent != content)
                {
                    logger.Warn("Fluent 11: HowLongToBeat selection window kept as is, layout changed.");
                    return;
                }

                var title = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                title.SetResourceReference(TextBlock.TextProperty, "LOCSelection");
                title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLarger");
                Text(title, "TextBrush");
                var game = new TextBlock { Text = GameName(window.Title), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
                Text(game, "TextBrushDarker");
                var header = new StackPanel { Margin = new Thickness(24, 24, 24, 8) };
                header.Children.Add(title);
                header.Children.Add(game);

                content.Children.Remove(buttons);
                buttons.Margin = new Thickness(0);
                var buttonStyle = Application.Current.TryFindResource(typeof(Button)) as Style;
                foreach (var button in buttons.Children.OfType<Button>())
                {
                    if (buttonStyle != null)
                    {
                        button.Style = buttonStyle;
                    }

                    if (button == select || button.Name == "ButtonConfirmManual")
                    {
                        button.Tag = "Fluent11Primary";
                    }
                }
                var footer = new Border { Padding = new Thickness(24, 20, 24, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
                footer.SetResourceReference(Border.BackgroundProperty, "DialogFooterBrush");
                footer.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");

                content.Children.Remove(tabs);
                tabs.Margin = new Thickness(24, 0, 24, 0);
                searchPane.Margin = new Thickness(0, 12, 0, 12);
                manualPane.Margin = new Thickness(0, 12, 0, 12);

                StyleSearch(control);
                StyleFilters(control);
                StyleManual(control, manualPane);

                var dock = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(header, Dock.Top);
                DockPanel.SetDock(footer, Dock.Bottom);
                dock.Children.Add(header);
                dock.Children.Add(footer);
                dock.Children.Add(tabs);
                control.Content = dock;

                list.ItemContainerStyle = (Style)XamlReader.Parse(ItemStyleXaml);
                list.ItemContainerGenerator.StatusChanged += (s, a) =>
                {
                    if (list.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
                    {
                        list.Dispatcher.BeginInvoke(new Action(() => StyleItems(list)), DispatcherPriority.Loaded);
                    }
                };
                list.Dispatcher.BeginInvoke(new Action(() => StyleItems(list)), DispatcherPriority.Loaded);

                var main = Application.Current?.MainWindow;
                var height = main != null && main != window && main.ActualHeight > 0 ? main.ActualHeight * 0.8 : 680;
                window.MinWidth = 720;
                window.MinHeight = 560;
                window.Width = 720;
                window.Height = Math.Round(Math.Max(600, Math.Min(900, height)));
                window.UpdateLayout();
                SeamlessWindow.Apply(window);
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to restyle HowLongToBeat selection window.");
            }
        }

        private static void StyleSearch(UserControl control)
        {
            if (!(control.FindName("SearchElement") is UserControl search) || !(control.FindName("PART_BtSearch") is Button button)
                || !(search.Parent is Grid row) || button.Parent != row || row.ColumnDefinitions.Count != 3)
            {
                return;
            }

            foreach (var name in new[] { "PART_ClearTextIcon", "PART_SeachIcon" })
            {
                if (search.FindName(name) is FrameworkElement icon && icon.Parent is Panel panel)
                {
                    panel.Children.Remove(icon);
                }
            }

            if (search.FindName("PART_TextInpuText") is TextBox text)
            {
                text.ClearValue(Control.PaddingProperty);
                var padding = text.Padding;
                text.Padding = new Thickness(padding.Left, padding.Top, 36, padding.Bottom);
            }

            row.ColumnDefinitions[1].Width = new GridLength(0);
            row.ColumnDefinitions[2].Width = new GridLength(0);
            button.Style = (Style)XamlReader.Parse(WindowPolish.SearchButtonStyle);
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.HorizontalAlignment = HorizontalAlignment.Right;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.ToolTip = button.Content;
            button.Content = null;
            Grid.SetColumn(button, 0);
            Panel.SetZIndex(button, 1);
        }

        private static void StyleFilters(UserControl control)
        {
            if (!(control.FindName("PART_SelectPlatform") is ComboBox platforms))
            {
                return;
            }

            if (Application.Current.TryFindResource(typeof(ComboBox)) is Style comboStyle)
            {
                platforms.Style = comboStyle;
            }

            if (!(platforms.Parent is Grid row) || row.ColumnDefinitions.Count != 5 || !(control.FindName("PART_Vndb") is CheckBox vndb) || vndb.Parent != row)
            {
                return;
            }

            platforms.Width = 240;
            row.ColumnDefinitions[2].Width = GridLength.Auto;
            row.ColumnDefinitions[3].Width = new GridLength(16);
            row.ColumnDefinitions[4].Width = GridLength.Auto;
            foreach (var label in row.Children.OfType<TextBlock>())
            {
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrushDarker");
            }
        }

        private static void StyleManual(UserControl control, FrameworkElement pane)
        {
            if (!(control.FindName("PART_ManualMainStoryH") is TextBox first) || !(first.Parent is Grid fields)
                || fields.ColumnDefinitions.Count != 5 || fields.RowDefinitions.Count != 9
                || !(control.FindName("PART_BtOpenWebsite") is Button website) || !(website.Parent is Grid outer)
                || !(outer.Parent is Border card) || card.Parent != pane)
            {
                return;
            }

            card.VerticalAlignment = VerticalAlignment.Top;
            card.Padding = new Thickness(20, 16, 20, 16);
            card.BorderThickness = new Thickness(1);
            card.SetResourceReference(Border.BackgroundProperty, "SurfaceCardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "CardStrokeBrush");
            card.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
            fields.Margin = new Thickness(0);

            var heads = fields.Children.OfType<TextBlock>().Where(a => Grid.GetRow(a) == 0).OrderBy(Grid.GetColumn).ToList();
            if (heads.Count != 2)
            {
                return;
            }

            foreach (var head in heads)
            {
                head.Visibility = Visibility.Collapsed;
            }

            fields.RowDefinitions[1].Height = new GridLength(0);
            fields.ColumnDefinitions[1].Width = new GridLength(64);
            fields.ColumnDefinitions[2].Width = new GridLength(40);
            fields.ColumnDefinitions[3].Width = new GridLength(64);

            foreach (var box in fields.Children.OfType<TextBox>().Where(a => Grid.GetColumnSpan(a) == 1).ToList())
            {
                var column = Grid.GetColumn(box);
                box.Margin = new Thickness(0, 3, 0, 3);
                var unit = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                unit.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = column == 1 ? heads[0] : heads[1] });
                Text(unit, "TextBrushDarker");
                Grid.SetRow(unit, Grid.GetRow(box));
                Grid.SetColumn(unit, column + 1);
                fields.Children.Add(unit);
            }

            fields.RowDefinitions[5].Height = new GridLength(25);
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            line.SetResourceReference(Border.BackgroundProperty, "CardStrokeBrush");
            Grid.SetRow(line, 5);
            Grid.SetColumnSpan(line, fields.ColumnDefinitions.Count);
            fields.Children.Add(line);

            outer.Children.Remove(website);
            website.Style = new Style(typeof(Button), Application.Current.TryFindResource("SimpleButton") as Style)
            {
                Setters = { new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse(LinkTemplateXaml)) }
            };
            website.MinWidth = 0;
            website.HorizontalAlignment = HorizontalAlignment.Left;
            website.VerticalAlignment = VerticalAlignment.Center;
            website.Margin = new Thickness(16, 6, 0, 0);
            Grid.SetRow(website, 8);
            Grid.SetColumn(website, 4);
            fields.Children.Add(website);
        }

        private static string GameName(string windowTitle)
        {
            var name = windowTitle ?? string.Empty;
            var prefix = (Application.Current?.TryFindResource("LOCSelection") as string) + " - ";
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name.Substring(prefix.Length);
            }

            var end = name.LastIndexOf(" - ", StringComparison.Ordinal);
            return end > 0 ? name.Substring(0, end) : name;
        }

        private static void StyleItems(ListBox list)
        {
            for (var i = 0; i < list.Items.Count; i++)
            {
                if (!(list.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item) || (bool)item.GetValue(StyledProperty))
                {
                    continue;
                }

                item.ApplyTemplate();
                var presenter = FindChild<ContentPresenter>(item);
                presenter?.ApplyTemplate();
                if (presenter == null || VisualTreeHelper.GetChildrenCount(presenter) == 0
                    || !(VisualTreeHelper.GetChild(presenter, 0) is Border inner))
                {
                    continue;
                }

                item.SetValue(StyledProperty, true);
                inner.Background = Brushes.Transparent;
                inner.BorderThickness = new Thickness(0);
                inner.Padding = new Thickness(16, 12, 16, 12);

                foreach (var image in FindChildren<Image>(inner))
                {
                    image.SizeChanged += (s, a) => Fluent11Helper.ApplyRoundClip(image);
                    Fluent11Helper.ApplyRoundClip(image);
                }

                foreach (var name in FindChildren<TextBlock>(inner).Where(a => a.Name == "HltbName"))
                {
                    name.FontWeight = FontWeights.SemiBold;
                }

                foreach (var table in FindChildren<Grid>(inner).Where(a => a.ColumnDefinitions.Count >= 5 && a.RowDefinitions.Count >= 3).ToList())
                {
                    StyleTable(table, list);
                }
            }
        }

        private static void StyleTable(Grid table, ListBox list)
        {
            var columns = new System.Collections.Generic.Dictionary<int, object>();
            foreach (var rect in table.Children.OfType<Rectangle>().ToList())
            {
                if (!(rect.Style?.Triggers.OfType<Trigger>().FirstOrDefault() is Trigger trigger))
                {
                    continue;
                }

                var column = Grid.GetColumn(rect);
                columns[column] = trigger.Value;
                rect.Style = Selected(new Style(typeof(Rectangle)), trigger.Value, new Setter(Shape.FillProperty, new DynamicResourceExtension("SubtleSelectedBrush")));
                rect.RadiusX = 4;
                rect.RadiusY = 4;
                Grid.SetRow(rect, 1);
                Grid.SetRowSpan(rect, Math.Max(1, table.RowDefinitions.Count - 3));

                var mark = new Border { Height = 3, Width = 16, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6), IsHitTestVisible = false };
                mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                mark.Style = Selected(new Style(typeof(Border)) { Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Collapsed) } },
                    trigger.Value, new Setter(UIElement.VisibilityProperty, Visibility.Visible));
                mark.SetBinding(FrameworkElement.TagProperty, new Binding("Tag") { Source = list });
                Grid.SetColumn(mark, column);
                table.Children.Add(mark);
            }

            var last = table.ColumnDefinitions.Count - 1;
            if (columns.Count > 0 && !table.Children.OfType<UIElement>().Any(a => Grid.GetColumn(a) == last))
            {
                table.ColumnDefinitions[last].Width = new GridLength(0);
            }

            foreach (var text in table.Children.OfType<TextBlock>().ToList())
            {
                var row = Grid.GetRow(text);
                var column = Grid.GetColumn(text);
                var span = Grid.GetColumnSpan(text);
                var style = new Style(typeof(TextBlock), text.Style ?? text.TryFindResource(typeof(TextBlock)) as Style);
                text.ClearValue(TextBlock.FontWeightProperty);
                if (span > 1 || (column == 0 && table.Children.OfType<TextBlock>().Any(a => a != text && Grid.GetRow(a) == row && Grid.GetColumnSpan(a) > 1)))
                {
                    style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Normal));
                    text.Margin = new Thickness(0, 8, 0, 0);
                    style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrushDarker")));
                    style.Setters.Add(new Setter(TextBlock.FontSizeProperty, new DynamicResourceExtension("FontSizeSmall")));
                    if (span > 1)
                    {
                        text.HorizontalAlignment = HorizontalAlignment.Left;
                        Grid.SetColumn(text, columns.Count > 0 ? columns.Keys.Min() : column);
                    }
                }
                else if (row == 0)
                {
                    text.Margin = new Thickness(text.Margin.Left, text.Margin.Top, text.Margin.Right, 9);
                    style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Normal));
                    style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrushDarker")));
                    style.Setters.Add(new Setter(TextBlock.FontSizeProperty, new DynamicResourceExtension("FontSizeSmall")));
                    if (columns.TryGetValue(column, out var value))
                    {
                        text.SetBinding(FrameworkElement.TagProperty, new Binding("Tag") { Source = list });
                        Selected(style, value, new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrush")),
                            new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                    }
                }
                else if (column == 0)
                {
                    style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Normal));
                    style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextBrushDarker")));
                }
                else if (columns.TryGetValue(column, out var value))
                {
                    text.SetBinding(FrameworkElement.TagProperty, new Binding("Tag") { Source = list });
                    Selected(style, value, new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                }

                text.Style = style;
            }
        }

        private static Style Selected(Style style, object value, params Setter[] setters)
        {
            var trigger = new Trigger { Property = FrameworkElement.TagProperty, Value = value };
            foreach (var setter in setters)
            {
                trigger.Setters.Add(setter);
            }

            style.Triggers.Add(trigger);
            return style;
        }

        private static readonly DependencyProperty StyledProperty =
            DependencyProperty.RegisterAttached("HltbSelectStyled", typeof(bool), typeof(HltbSelectDialog), new PropertyMetadata(false));

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            return FindChildren<T>(parent).FirstOrDefault();
        }

        private static System.Collections.Generic.IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (var nested in FindChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static void Text(TextBlock text, string brush)
        {
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily");
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }
    }
}
