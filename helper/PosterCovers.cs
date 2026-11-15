using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fluent11Helper
{
    internal static class PosterCovers
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp" };
        private const int CacheSize = 120;

        private static Func<Fluent11HelperSettings> getSettings;
        private static string folder;
        private static readonly Dictionary<Guid, string> files = new Dictionary<Guid, string>();
        private static readonly Dictionary<Guid, int> revisions = new Dictionary<Guid, int>();
        private static readonly List<WeakReference<Image>> tracked = new List<WeakReference<Image>>();
        private static readonly List<WeakReference<Image>> trackedDetails = new List<WeakReference<Image>>();
        private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapSource>>> cache =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, BitmapSource>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<KeyValuePair<string, BitmapSource>> cacheOrder = new LinkedList<KeyValuePair<string, BitmapSource>>();
        private static DispatcherTimer refreshTimer;

        private static readonly DependencyProperty IsTrackedProperty =
            DependencyProperty.RegisterAttached("Fluent11PosterTracked", typeof(bool), typeof(PosterCovers), new PropertyMetadata(false));

        public static void Register(string dataPath, Func<Fluent11HelperSettings> helperSettings)
        {
            getSettings = helperSettings;
            folder = Path.Combine(dataPath, "Covers");
            try
            {
                Directory.CreateDirectory(folder);
                foreach (var file in Directory.GetFiles(folder))
                {
                    if (Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id) && IsImage(file))
                    {
                        files[id] = file;
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to read the poster covers folder.");
            }

            EventManager.RegisterClassHandler(typeof(Image), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnImageSizeChanged));
        }

        private static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        public static bool HasCover(Guid id) => files.ContainsKey(id);

        public static string FileFilter => "Images|*.jpg;*.jpeg;*.png;*.bmp";

        public static void SetCover(Guid id, string source)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source) || !IsImage(source))
            {
                return;
            }

            try
            {
                var data = File.ReadAllBytes(source);
                DeleteFiles(id);
                var target = Path.Combine(folder, id + Path.GetExtension(source).ToLowerInvariant());
                File.WriteAllBytes(target, data);
                files[id] = target;
                revisions[id] = revisions.TryGetValue(id, out var revision) ? revision + 1 : 1;
                Forget(target);
                RefreshAll();
            }
            catch (Exception e)
            {
                logger.Error(e, $"Fluent 11: failed to set the poster cover for {id}.");
            }
        }

        public static void RemoveCovers(IEnumerable<Guid> ids)
        {
            foreach (var id in ids)
            {
                try
                {
                    DeleteFiles(id);
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Fluent 11: failed to remove the poster cover for {id}.");
                }
            }

            RefreshAll();
        }

        private static void DeleteFiles(Guid id)
        {
            if (files.TryGetValue(id, out var old))
            {
                Forget(old);
            }

            files.Remove(id);
            foreach (var file in Directory.GetFiles(folder, id + ".*"))
            {
                File.Delete(file);
            }
        }

        private static void Forget(string path)
        {
            lock (cache)
            {
                foreach (var key in cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    cacheOrder.Remove(cache[key]);
                    cache.Remove(key);
                }
            }
        }

        public static void RefreshAll()
        {
            Refresh(tracked);
            Refresh(trackedDetails);
        }

        private static void Refresh(List<WeakReference<Image>> list)
        {
            list.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (var reference in list)
            {
                if (reference.TryGetTarget(out var image))
                {
                    Update(image);
                }
            }
        }

        public static void RefreshAfterZoom()
        {
            if (refreshTimer == null)
            {
                refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                refreshTimer.Tick += (s, e) =>
                {
                    refreshTimer.Stop();
                    lock (cache)
                    {
                        cache.Clear();
                        cacheOrder.Clear();
                    }

                    RefreshAll();
                };
            }

            refreshTimer.Stop();
            refreshTimer.Start();
        }

        private static void OnImageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!(sender is Image image) || image.Name != "PART_ImageCover" || (bool)image.GetValue(IsTrackedProperty)
                || !(Host(image) is Grid host) || (host.Name != "CoverHost" && host.Name != "DetailsCoverHost"))
            {
                return;
            }

            image.SetValue(IsTrackedProperty, true);
            image.DataContextChanged += (s, a) => Update(image);
            (host.Name == "DetailsCoverHost" ? trackedDetails : tracked).Add(new WeakReference<Image>(image));
            Update(image);
        }

        private static Grid Host(Image image) => VisualTreeHelper.GetParent(image) as Grid;

        private static Guid? GameId(object entry)
        {
            if (Reflect.GetMember(entry, "Id") is Guid id)
            {
                return id;
            }

            var game = Reflect.GetMember(entry, "Game");
            return game is Game playniteGame ? playniteGame.Id : game != null && game != entry ? GameId(game) : null;
        }

        private static Guid? selectedGame;

        public static void OnGameSelected(Guid? id)
        {
            selectedGame = id;
            Refresh(trackedDetails);
        }

        private static void Update(Image image)
        {
            if (!(Host(image) is Grid host))
            {
                return;
            }

            var details = host.Name == "DetailsCoverHost";
            var settings = getSettings();
            var enabled = settings?.PosterTiles == true && (!details || settings.PosterDetails) && files.Count > 0;
            var id = !enabled ? null : details ? selectedGame : GameId(image.DataContext);
            if (id == null || !files.TryGetValue(id.Value, out var path))
            {
                if (host.Tag is PosterImage)
                {
                    host.Tag = null;
                    if (details)
                    {
                        host.ClearValue(FrameworkElement.WidthProperty);
                    }
                }

                return;
            }

            if (details)
            {
                var height = double.IsNaN(image.Height) ? image.ActualHeight : image.Height;
                host.Width = Math.Round(height * 3 / 4);
            }

            var dpi = VisualTreeHelper.GetDpi(host).DpiScaleX;
            var width = Math.Max(64, (int)Math.Ceiling(host.Width * dpi / 64) * 64);
            revisions.TryGetValue(id.Value, out var revision);
            var key = path + "|" + revision + "|" + width;
            if (host.Tag is PosterImage current && current.Key == key)
            {
                return;
            }

            var cached = FromCache(key);
            if (cached != null)
            {
                host.Tag = new PosterImage(cached, key);
                return;
            }

            host.Tag = new PosterImage(null, key);
            Task.Run(() =>
            {
                var bitmap = Load(path, width, key);
                image.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!(host.Tag is PosterImage pending) || pending.Key != key)
                    {
                        return;
                    }

                    host.Tag = bitmap != null ? new PosterImage(bitmap, key) : null;
                    if (bitmap == null && details)
                    {
                        host.ClearValue(FrameworkElement.WidthProperty);
                    }
                }));
            });
        }

        private static BitmapSource FromCache(string key)
        {
            lock (cache)
            {
                if (cache.TryGetValue(key, out var node))
                {
                    cacheOrder.Remove(node);
                    cacheOrder.AddFirst(node);
                    return node.Value.Value;
                }
            }

            return null;
        }

        private static BitmapSource Load(string path, int width, string key)
        {
            BitmapSource image;
            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                {
                    var original = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).PixelWidth;
                    stream.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (original > width)
                    {
                        bitmap.DecodePixelWidth = width;
                    }

                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
            catch (Exception e)
            {
                logger.Warn($"Fluent 11: poster cover {path} could not be loaded: {e.Message}");
                return null;
            }

            lock (cache)
            {
                if (!cache.ContainsKey(key))
                {
                    cache[key] = cacheOrder.AddFirst(new KeyValuePair<string, BitmapSource>(key, image));
                    while (cacheOrder.Count > CacheSize)
                    {
                        cache.Remove(cacheOrder.Last.Value.Key);
                        cacheOrder.RemoveLast();
                    }
                }
            }

            return image;
        }
    }

    public class PosterImage
    {
        public PosterImage(ImageSource image, string key)
        {
            Image = image;
            Key = key;
        }

        public ImageSource Image { get; }
        internal string Key { get; }
    }
}
