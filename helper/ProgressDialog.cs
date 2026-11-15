using Playnite.SDK;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Fluent11Helper
{
    internal static class ProgressDialog
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public static void Apply(Window window)
        {
            try
            {
                if (!(window.Content is Grid body))
                {
                    return;
                }

                var bar = body.Children.OfType<ProgressBar>().FirstOrDefault();
                var cancel = body.Children.OfType<Button>().FirstOrDefault();
                if (bar == null)
                {
                    return;
                }

                bar.Height = 3;
                bar.Margin = new Thickness(0, 16, 0, 0);
                body.Margin = new Thickness(24, 24, 24, 24);

                var dock = new DockPanel { LastChildFill = true };
                if (cancel != null)
                {
                    body.Children.Remove(cancel);
                    cancel.Margin = new Thickness(0);
                    cancel.MinWidth = 120;
                    cancel.HorizontalAlignment = HorizontalAlignment.Right;

                    var footer = new Border { Padding = new Thickness(24, 20, 24, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = cancel };
                    footer.SetResourceReference(Border.BackgroundProperty, "DialogFooterBrush");
                    footer.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeBrush");
                    footer.SetBinding(UIElement.VisibilityProperty, new Binding("Visibility") { Source = cancel });
                    DockPanel.SetDock(footer, Dock.Bottom);
                    dock.Children.Add(footer);
                }

                window.Content = null;
                dock.Children.Add(body);
                window.Content = dock;
                window.Width = 440;
                SeamlessWindow.Apply(window);
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to restyle progress window.");
            }
        }
    }
}
