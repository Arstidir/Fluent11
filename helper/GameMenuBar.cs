using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Fluent11Helper
{
    internal static class GameMenuBar
    {
        private const string BarTag = "Fluent11MenuActionBar";

        private static readonly BarAction[] Actions =
        {
            new BarAction("LOCPlayGame", "\uE768", "Play"),
            new BarAction("LOCInstallGame", "\uE896", "Install"),
            new BarAction("LOCEditGame", "\uE932", "Edit"),
            new BarAction("LOCFavoriteGame", "\uEB51", "Favorite"),
            new BarAction("LOCRemoveFavoriteGame", null, "Unfavorite"),
            new BarAction("LOCHideGame", "\uED1A", "Hide"),
            new BarAction("LOCUnHideGame", "\uE890", "Unhide")
        };

        private class BarAction
        {
            public BarAction(string key, string glyph, string label)
            {
                Key = key;
                Glyph = glyph;
                Label = label;
            }

            public string Key { get; }
            public string Glyph { get; }
            public string Label { get; }
        }

        public static void Apply(ContextMenu menu, Func<string, FrameworkElement> createIcon, Func<FrameworkElement> createUnfavoriteIcon,
            Action<ItemsControl> tidySeparators)
        {
            if (menu.Items.OfType<MenuItem>().Any(a => a.Tag as string == BarTag))
            {
                return;
            }

            var items = menu.Items.OfType<MenuItem>().ToList();
            var buttons = new List<Button>();
            foreach (var action in Actions)
            {
                var text = ResourceProvider.GetString(action.Key);
                var item = items.FirstOrDefault(a => a.Header as string == text && a.Visibility == Visibility.Visible);
                if (item == null)
                {
                    continue;
                }

                var button = new Button
                {
                    Content = action.Glyph != null ? createIcon(action.Glyph) : createUnfavoriteIcon(),
                    Tag = action.Label,
                    ToolTip = text.TrimEnd('…', '.')
                };
                button.SetResourceReference(FrameworkElement.StyleProperty, "Fluent11MenuActionButton");
                button.Click += (s, e) =>
                {
                    menu.IsOpen = false;
                    if (item.Command != null)
                    {
                        if (item.Command.CanExecute(item.CommandParameter))
                        {
                            item.Command.Execute(item.CommandParameter);
                        }
                    }
                    else
                    {
                        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
                    }
                };
                buttons.Add(button);
                item.Visibility = Visibility.Collapsed;
            }

            if (buttons.Count == 0)
            {
                return;
            }

            var row = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            buttons.ForEach(a => row.Children.Add(a));
            var dividers = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, IsHitTestVisible = false };
            for (var i = 0; i < buttons.Count; i++)
            {
                var divider = new Border
                {
                    Width = 1,
                    Height = 24,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = i < buttons.Count - 1 ? Visibility.Visible : Visibility.Hidden
                };
                divider.SetResourceReference(Border.BackgroundProperty, "ControlStrokeBrush");
                dividers.Children.Add(divider);
            }

            var host = new Grid();
            host.Children.Add(row);
            host.Children.Add(dividers);
            var bar = new MenuItem { Header = host, Tag = BarTag };
            bar.SetResourceReference(FrameworkElement.StyleProperty, "Fluent11MenuActionBar");
            menu.Items.Insert(0, bar);
            menu.Items.Insert(1, new Separator());
            tidySeparators(menu);
        }
    }
}
