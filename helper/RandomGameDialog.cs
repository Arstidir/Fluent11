using Playnite.SDK;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Fluent11Helper
{
    internal static class RandomGameDialog
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public const string WindowType = "RandomGameSelectWindow";

        public static void Apply(Window window, Action onDetails)
        {
            try
            {
                if (!(window.Content is StackPanel body))
                {
                    return;
                }

                var cover = body.Children.OfType<Image>().FirstOrDefault();
                var name = body.Children.OfType<TextBlock>().FirstOrDefault();
                var limit = body.Children.OfType<CheckBox>().FirstOrDefault();
                var row = body.Children.OfType<StackPanel>().FirstOrDefault();
                var play = body.Children.OfType<Button>().LastOrDefault();
                var others = row?.Children.OfType<Button>().ToList();
                if (cover == null || name == null || limit == null || play == null || others == null || others.Count != 2)
                {
                    logger.Warn("Fluent 11: random game window kept as is, layout changed.");
                    return;
                }

                others[0].Click += (s, a) => window.Dispatcher.BeginInvoke(onDetails, System.Windows.Threading.DispatcherPriority.Background);

                body.Children.Clear();
                row.Children.Clear();

                var title = new TextBlock
                {
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(24, 24, 24, 20)
                };
                title.SetResourceReference(TextBlock.TextProperty, "LOCMenuSelectRandomGame");
                title.SetResourceReference(TextBlock.FontSizeProperty, "FontSizeLarger");
                title.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily");
                title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                cover.Height = 240;
                cover.SizeChanged += (s, a) => Fluent11Helper.ApplyRoundClip(cover);
                var stroke = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
                stroke.SetResourceReference(Border.BorderBrushProperty, "CardStrokeBrush");
                stroke.SetResourceReference(Border.CornerRadiusProperty, "CardCornerRadius");
                var coverHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(24, 0, 24, 0) };
                coverHost.Children.Add(cover);
                coverHost.Children.Add(stroke);

                name.Margin = new Thickness(24, 12, 24, 16);
                name.FontWeight = FontWeights.SemiBold;
                name.TextWrapping = TextWrapping.Wrap;
                name.TextAlignment = TextAlignment.Center;
                limit.Margin = new Thickness(24, 0, 24, 24);

                var content = new StackPanel();
                content.Children.Add(title);
                content.Children.Add(coverHost);
                content.Children.Add(name);
                content.Children.Add(limit);

                var buttons = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(-4, 0, -4, 0) };
                foreach (var button in new[] { play, others[1], others[0] })
                {
                    button.Margin = new Thickness(4, 0, 4, 0);
                    button.MinWidth = 0;
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    buttons.Children.Add(button);
                }

                var footer = new Border { Padding = new Thickness(24, 20, 24, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
                footer.SetResourceReference(Border.BackgroundProperty, "DialogFooterBrush");
                footer.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");

                var dock = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(footer, Dock.Bottom);
                dock.Children.Add(footer);
                dock.Children.Add(content);

                window.Content = null;
                window.Content = dock;
                content.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ActualWidth)) { Source = footer });
                window.ClearValue(FrameworkElement.WidthProperty);
                window.MinWidth = 440;
                window.MaxWidth = 640;
                window.SizeToContent = SizeToContent.WidthAndHeight;
                SeamlessWindow.Apply(window);
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to restyle random game window.");
            }
        }
    }
}
