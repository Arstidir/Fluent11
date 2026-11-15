using System.Windows;

namespace Fluent11Helper
{
    internal static class SeamlessWindow
    {
        public const string Tag = "Fluent11Seamless";

        public static void Apply(Window window)
        {
            window.Tag = Tag;
            if (window.IsLoaded)
            {
                HideTitle(window);
            }
            else
            {
                window.Loaded += (s, e) => HideTitle(window);
            }
        }

        private static void HideTitle(Window window)
        {
            window.ApplyTemplate();
            if (window.Template?.FindName("PART_TextTitle", window) is UIElement title)
            {
                title.Visibility = Visibility.Collapsed;
            }
        }
    }
}
