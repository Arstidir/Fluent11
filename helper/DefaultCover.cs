using Playnite.SDK;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Fluent11Helper
{
    internal static class DefaultCover
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string ResourceKey = "DefaultGameCover";

        public static void Apply(string themeDir, bool dark)
        {
            try
            {
                var path = Path.Combine(themeDir, "Images", dark ? "custom_cover_background_dark.png" : "custom_cover_background.png");
                if (!File.Exists(path))
                {
                    return;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                Application.Current.Resources[ResourceKey] = image;

                var cache = Type.GetType("Playnite.ImageSourceManager, Playnite")?
                    .GetField("Cache", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                cache?.GetType().GetMethods()
                    .FirstOrDefault(a => a.Name == "TryRemove" && a.GetParameters().Length == 1)?
                    .Invoke(cache, new object[] { ResourceKey });
            }
            catch (Exception e)
            {
                logger.Error(e, "Fluent 11: failed to set default cover.");
            }
        }
    }
}
