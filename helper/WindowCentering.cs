using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class WindowCentering
    {
        private const double MinSize = 120;
        private const double OffScreen = -10000;
        private const double SearchBottomGap = 16;

        public static void Apply(Window window)
        {
            Center(window);
            window.Dispatcher.BeginInvoke(new Action(() => Center(window)), DispatcherPriority.Normal);
        }

        private static void Center(Window window)
        {
            try
            {
                var main = Application.Current?.MainWindow;
                if (main == null || window == main || !main.IsVisible || main.WindowState == WindowState.Minimized
                    || window.WindowState != WindowState.Normal || window.AllowsTransparency || window.Topmost)
                {
                    return;
                }

                var source = PresentationSource.FromVisual(main);
                if (source?.CompositionTarget == null || window.ActualWidth < MinSize || window.ActualHeight < MinSize
                    || window.Left < OffScreen || window.Top < OffScreen)
                {
                    return;
                }

                var topLeft = main.PointToScreen(new Point(0, 0));
                var bottomRight = main.PointToScreen(new Point(main.ActualWidth, main.ActualHeight));
                Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
                var a = fromDevice.Transform(topLeft);
                var b = fromDevice.Transform(bottomRight);

                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = Math.Round(a.X + (b.X - a.X - window.ActualWidth) / 2);
                window.Top = window.GetType().Name == "SearchWindow"
                    ? Math.Round(Math.Max(a.Y, Math.Min(a.Y + (b.Y - a.Y) / 5, b.Y - window.ActualHeight - SearchBottomGap)))
                    : Math.Round(a.Y + (b.Y - a.Y - window.ActualHeight) / 2);
            }
            catch
            {
            }
        }
    }
}
