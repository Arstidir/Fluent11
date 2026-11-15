using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Fluent11Helper
{
    internal static class FocusAreas
    {
        private const double MaxControlHeight = 120;
        private const double MaxControlArea = 40000;

        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(FrameworkElement), Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(OnPreviewGotFocus), true);
        }

        private static void OnPreviewGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.NewFocus) || !(sender is FrameworkElement element))
            {
                return;
            }

            var large = element.ActualHeight > MaxControlHeight || element.ActualWidth * element.ActualHeight > MaxControlArea;
            if (large && element.FocusVisualStyle != null && !(element is TextBoxBase))
            {
                element.FocusVisualStyle = null;
            }
        }
    }
}
