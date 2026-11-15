using System;
using System.Collections.Generic;
using System.Reflection;

namespace Fluent11Helper
{
    internal static class Reflect
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> cache = new Dictionary<Type, Dictionary<string, MemberInfo>>();

        public static object GetMember(object target, string name)
        {
            if (target == null)
            {
                return null;
            }

            switch (Find(target.GetType(), name))
            {
                case PropertyInfo property:
                    return property.GetValue(target);
                case FieldInfo field:
                    return field.GetValue(target);
                default:
                    return null;
            }
        }

        public static bool HasMember(Type type, string name)
        {
            return type != null && Find(type, name) != null;
        }

        private static MemberInfo Find(Type type, string name)
        {
            lock (cache)
            {
                if (!cache.TryGetValue(type, out var members))
                {
                    members = new Dictionary<string, MemberInfo>();
                    cache[type] = members;
                }

                if (members.TryGetValue(name, out var cached))
                {
                    return cached;
                }

                MemberInfo found = null;
                for (var current = type; current != null && found == null; current = current.BaseType)
                {
                    found = (MemberInfo)current.GetProperty(name, Flags) ?? current.GetField(name, Flags);
                }

                members[name] = found;
                return found;
            }
        }
    }
}
