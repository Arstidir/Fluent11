using Microsoft.Win32;
using Playnite.SDK;
using System;
using System.Windows.Media;

namespace Fluent11Helper
{
    internal static class WindowsAccent
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
        private const int Light2 = 1;
        private const int Dark1 = 4;

        public static Color? Read(bool dark)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AccentKey))
                {
                    if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
                    {
                        var i = (dark ? Light2 : Dark1) * 4;
                        return Color.FromRgb(palette[i], palette[i + 1], palette[i + 2]);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warn(e, "Fluent 11: could not read the Windows accent color.");
            }

            return null;
        }
    }
}
