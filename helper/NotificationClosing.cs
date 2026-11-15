using Playnite.SDK;
using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluent11Helper
{
    internal static class NotificationClosing
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static object appSettings;
        private static PropertyInfo visibleProperty;

        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnMouseDown));
            EventManager.RegisterClassHandler(typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnKeyDown));
        }

        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (IsOpen() && !IsInside(e.OriginalSource as DependencyObject))
            {
                Close();
            }
        }

        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && IsOpen())
            {
                Close();
                e.Handled = true;
            }
        }

        private static bool IsInside(DependencyObject element)
        {
            while (element != null)
            {
                if (element is FrameworkElement framework
                    && (framework.Name == "PART_Notifications" || "Fluent11NotificationsToggle".Equals(framework.Tag)))
                {
                    return true;
                }

                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }

            return false;
        }

        private static bool IsOpen()
        {
            if (visibleProperty == null)
            {
                try
                {
                    appSettings = PlayniteInternals.AppSettings;
                    visibleProperty = appSettings?.GetType().GetProperty("NotificationPanelVisible");
                }
                catch (Exception e)
                {
                    logger.Error(e, "Fluent 11: notification panel setting not found.");
                }

                if (visibleProperty == null)
                {
                    return false;
                }
            }

            return true.Equals(visibleProperty.GetValue(appSettings));
        }

        private static void Close()
        {
            visibleProperty?.SetValue(appSettings, false);
        }
    }
}
