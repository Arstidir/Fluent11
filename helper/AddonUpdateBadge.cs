using Playnite.SDK;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class AddonUpdateBadge
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string MessageId = "AddonUpdateAvailable";
        private static int shown;
        private static bool found;
        private static Func<bool> enabled = () => true;

        public static void Register(IPlayniteAPI api, Func<bool> isEnabled)
        {
            enabled = isEnabled;
            try
            {
                var messages = api.Notifications.Messages;
                foreach (var message in messages.ToList())
                {
                    Check(message);
                }

                messages.CollectionChanged += (s, e) =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
                    {
                        foreach (var message in e.NewItems.OfType<NotificationMessage>())
                        {
                            Check(message);
                        }
                    }
                };
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to watch add-on update notifications.");
            }
        }

        private static void Check(NotificationMessage message)
        {
            if (message?.Id != MessageId || !enabled())
            {
                return;
            }

            Show(CountUpdates(message.ActivationAction));
        }

        private static void Show(int count)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (shown > 0 && count <= shown)
                {
                    return;
                }

                shown = Math.Max(shown, count);
                found = true;
                Application.Current.Resources["Fluent11AddonUpdateCount"] = shown > 0 ? shown.ToString() : string.Empty;
                Application.Current.Resources["Fluent11AddonUpdateBadgeVisibility"] = Visibility.Visible;
            }));
        }

        public static void Watch(Window window)
        {
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!(window.DataContext is INotifyPropertyChanged model))
                    {
                        return;
                    }

                    var type = model.GetType();
                    var countProperty = type.GetProperty("UpdateAddonCount");
                    if (countProperty?.PropertyType != typeof(int))
                    {
                        logger.Warn("Fluent 11: add-ons window has no update count, badge not refreshed.");
                        return;
                    }

                    void Take()
                    {
                        var count = (int)countProperty.GetValue(model);
                        if (count > 0 && enabled())
                        {
                            Show(count);
                        }
                    }

                    PropertyChangedEventHandler handler = (s, e) =>
                    {
                        if (e.PropertyName == "UpdateAddonCount")
                        {
                            Take();
                        }
                    };
                    model.PropertyChanged += handler;
                    window.Closed += (s, e) => model.PropertyChanged -= handler;

                    if (!(type.GetProperty("IsUpdateListLoading")?.GetValue(model) is true)
                        && type.GetProperty("UpdateAddonList")?.GetValue(model) != null)
                    {
                        Take();
                    }
                }
                catch (Exception e)
                {
                    logger.Warn(e, "Fluent 11: could not read add-on updates from the add-ons window.");
                }
            }), DispatcherPriority.Loaded);
        }

        public static void Update(IPlayniteAPI api)
        {
            if (!enabled())
            {
                Application.Current.Resources["Fluent11AddonUpdateBadgeVisibility"] = Visibility.Collapsed;
                return;
            }

            if (found)
            {
                Application.Current.Resources["Fluent11AddonUpdateBadgeVisibility"] = Visibility.Visible;
                return;
            }

            foreach (var message in api.Notifications.Messages.ToList())
            {
                Check(message);
            }
        }

        private static int CountUpdates(Action action)
        {
            try
            {
                var target = action?.Target;
                if (target == null)
                {
                    return 0;
                }

                var fields = target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var field = fields.FirstOrDefault(a => a.Name == "updates" && typeof(ICollection).IsAssignableFrom(a.FieldType));
                return (field?.GetValue(target) as ICollection)?.Count ?? 0;
            }
            catch (Exception e)
            {
                logger.Warn(e, "Fluent 11: could not count add-on updates.");
                return 0;
            }
        }
    }
}
