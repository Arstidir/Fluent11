using Playnite.SDK;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Fluent11Helper
{
    internal static class AchievementsRefreshDialog
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public const string ControlName = "RefreshProgressControl";

        public static readonly string[] ModelMembers =
        {
            "ProgressMessage", "ProgressPercent", "RefreshRunningNote", "ShowInProgressButtons", "ShowCompleteButtons",
            "ShowOpenViewAchievementsButton", "HideCommand", "CancelCommand", "ContinueCommand", "OpenViewAchievementsCommand"
        };

        public static void TryApply(Window window, bool micaActive)
        {
            try
            {
                if (!(window.Content is UserControl control) || control.GetType().Name != ControlName)
                {
                    return;
                }

                var model = control.DataContext;
                var missing = ModelMembers.Where(a => !Reflect.HasMember(model?.GetType(), a)).ToList();
                if (missing.Count > 0)
                {
                    logger.Warn($"Fluent 11: achievements refresh window kept as is, missing: {string.Join(", ", missing)}.");
                    return;
                }

                var title = new TextBlock
                {
                    Text = window.Title,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24, 24, 24, 12)
                };
                title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLarger");
                Text(title, "TextBrush");

                var message = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
                message.SetBinding(TextBlock.TextProperty, new Binding("ProgressMessage"));
                Text(message, "TextBrush");

                var percent = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 36, TextAlignment = TextAlignment.Right };
                percent.SetBinding(TextBlock.TextProperty, new Binding("ProgressPercent") { StringFormat = "{0:0} %" });
                Text(percent, "TextBrushDarker");
                var bar = new ProgressBar { Height = 3, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
                bar.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty, new Binding("ProgressPercent") { Mode = BindingMode.OneWay });
                var progress = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
                DockPanel.SetDock(percent, Dock.Right);
                progress.Children.Add(percent);
                progress.Children.Add(bar);

                var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
                note.SetBinding(TextBlock.TextProperty, new Binding("RefreshRunningNote"));
                note.SetBinding(UIElement.VisibilityProperty, Visible("ShowInProgressButtons"));
                note.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeSmall");
                Text(note, "TextBrushDarker");

                var body = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
                body.Children.Add(message);
                body.Children.Add(progress);
                body.Children.Add(note);

                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                buttons.Children.Add(MakeButton("LOCPlayAch_Common_Hide", "HideCommand", "ShowInProgressButtons", false));
                buttons.Children.Add(MakeButton("LOCPlayAch_Common_Cancel", "CancelCommand", "ShowInProgressButtons", false));
                buttons.Children.Add(MakeButton("LOCPlayAch_Common_Continue", "ContinueCommand", "ShowCompleteButtons", true));
                buttons.Children.Add(MakeButton("LOCPlayAch_Menu_ViewAchievements", "OpenViewAchievementsCommand", "ShowOpenViewAchievementsButton", false));
                var footer = new Border { Padding = new Thickness(24, 20, 24, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
                footer.SetResourceReference(Border.BackgroundProperty, "DialogFooterBrush");
                footer.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");

                var dock = new DockPanel { LastChildFill = true };
                foreach (var type in new[] { typeof(Button), typeof(ProgressBar), typeof(TextBlock) })
                {
                    if (Application.Current.TryFindResource(type) is Style style)
                    {
                        dock.Resources[type] = style;
                    }
                }

                DockPanel.SetDock(title, Dock.Top);
                DockPanel.SetDock(footer, Dock.Bottom);
                dock.Children.Add(title);
                dock.Children.Add(footer);
                dock.Children.Add(body);

                control.Content = dock;
                control.Margin = new Thickness(0);

                foreach (var key in AchievementsGameView.CopiedBrushKeys)
                {
                    window.Resources.Remove(key);
                }

                if (!micaActive)
                {
                    window.ClearValue(Control.BackgroundProperty);
                }

                window.Width = 440;
                window.SizeToContent = SizeToContent.Height;
                SeamlessWindow.Apply(window);
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to restyle achievements refresh window, keeping the original.");
            }
        }

        private static void Text(TextBlock text, string brush)
        {
            text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily");
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
        }

        private static Binding Visible(string path) => new Binding(path) { Converter = new BooleanToVisibilityConverter() };

        private static Button MakeButton(string text, string command, string visible, bool isDefault)
        {
            var button = new Button { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault };
            button.SetResourceReference(ContentControl.ContentProperty, text);
            button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(command));
            button.SetBinding(UIElement.VisibilityProperty, Visible(visible));
            return button;
        }
    }
}
