using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Fluent11Helper
{
    internal static class ToolTipPlacement
    {
        private const double GapAbove = 4;
        private const double GapBelow = 20;

        private static readonly ConditionalWeakTable<ToolTip, object> placed = new ConditionalWeakTable<ToolTip, object>();

        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(OnOpened));
        }

        private static void OnOpened(object sender, RoutedEventArgs e)
        {
            if (!(sender is ToolTip tip) || !(tip.PlacementTarget is FrameworkElement target) || target.ActualWidth <= 0 || !target.IsMouseOver)
            {
                return;
            }

            var ours = placed.TryGetValue(tip, out _);
            if (tip.Placement != PlacementMode.Mouse && !(ours && tip.Placement == PlacementMode.Custom))
            {
                return;
            }

            var mouse = Mouse.GetPosition(target);
            tip.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            {
                var scale = targetSize.Width / target.ActualWidth;
                var x = mouse.X * scale - popupSize.Width / 2;
                var y = mouse.Y * scale;
                return new[]
                {
                    new CustomPopupPlacement(new Point(x, y - GapAbove * scale - popupSize.Height), PopupPrimaryAxis.Horizontal),
                    new CustomPopupPlacement(new Point(x, y + GapBelow * scale), PopupPrimaryAxis.Horizontal)
                };
            };
            if (ours)
            {
                tip.Placement = PlacementMode.Mouse;
            }
            else
            {
                placed.Add(tip, null);
            }

            tip.Placement = PlacementMode.Custom;
        }
    }
}
