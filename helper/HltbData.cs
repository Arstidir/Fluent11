using Playnite.SDK;
using Playnite.SDK.Plugins;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace Fluent11Helper
{
    internal static class HltbData
    {
        public static Plugin FindPlugin(IPlayniteAPI api)
        {
            return api.Addons.Plugins.FirstOrDefault(a => a.GetType().Assembly.GetName().Name == "HowLongToBeat");
        }

        public static bool IsInstalled(IPlayniteAPI api)
        {
            return FindPlugin(api) != null;
        }

        public static object FindDatabase(IPlayniteAPI api)
        {
            return FindDatabase(FindPlugin(api));
        }

        public static object FindDatabase(Plugin plugin)
        {
            for (var type = plugin?.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty("PluginDatabase", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (property != null)
                {
                    return property.GetValue(null);
                }
            }

            return null;
        }

        public static IEnumerable AllEntries(object database)
        {
            return database?.GetType().GetMethod("GetAllCache", Type.EmptyTypes)?.Invoke(database, null) as IEnumerable;
        }

        public static object CachedEntry(object database, Guid gameId)
        {
            var get = database?.GetType().GetMethods()
                .FirstOrDefault(a => a.Name == "Get" && a.GetParameters().Length == 3 && a.GetParameters()[0].ParameterType == typeof(Guid));
            return get?.Invoke(database, new object[] { gameId, true, false });
        }

        public static object Times(object entry)
        {
            if (entry == null || !true.Equals(Reflect.GetMember(entry, "HasData")) || true.Equals(Reflect.GetMember(entry, "HasDataEmpty")))
            {
                return null;
            }

            var data = entry.GetType().GetMethod("GetData", Type.EmptyTypes)?.Invoke(entry, null);
            return Reflect.GetMember(data, "GameHltbData");
        }

        public static long Seconds(object times, string name)
        {
            return Reflect.GetMember(times, name) is long value ? value : 0;
        }
    }
}
