using Playnite.SDK;
using Playnite.SDK.Models;
using System;

namespace Fluent11Helper
{
    internal static class GameText
    {
        public static string CoverPath(IPlayniteAPI api, Game game)
        {
            var file = !string.IsNullOrEmpty(game?.CoverImage) ? game.CoverImage : game?.Icon;
            if (string.IsNullOrEmpty(file))
            {
                return null;
            }

            return file.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? file : api.Database.GetFullFilePath(file);
        }

        public static string Time(ulong seconds)
        {
            var minutes = seconds / 60;
            return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
        }

        public static string Time(long seconds)
        {
            return Time((ulong)Math.Max(0, seconds));
        }

        public static string RoundedHours(long seconds)
        {
            if (seconds < 3600)
            {
                return Time(seconds);
            }

            return $"{Math.Round(seconds / 3600.0, MidpointRounding.AwayFromZero):N0}h";
        }

        public static string Hours(double seconds)
        {
            return $"{Math.Round(seconds / 3600.0):N0} h";
        }
    }
}
