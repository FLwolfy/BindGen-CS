namespace BGCS.Core.Collections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    /// <summary>
    /// Initializes null, writable collection properties before generator configuration is consumed.
    /// </summary>
    public static class CollectionNormalizer
    {
        /// <summary>
        /// Replaces null List, HashSet and Dictionary properties with empty instances.
        /// Other collection types and construction failures leave the property unchanged.
        /// </summary>
        /// <typeparam name="T">The configuration type whose public properties are inspected.</typeparam>
        /// <param name="obj">The configuration to modify, or null to perform no work.</param>
        public static void Normalize<T>(T obj)
        {
            if (obj == null)
                return;
            var props = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.CanWrite && p.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(p.PropertyType));
            foreach (var prop in props)
            {
                var current = prop.GetValue(obj);
                if (current == null)
                {
                    var type = prop.PropertyType;
                    // Handle common collection types
                    if (type.IsGenericType)
                    {
                        Type genericDef = type.GetGenericTypeDefinition();
                        if (genericDef == typeof(List<>) || genericDef == typeof(HashSet<>) || genericDef == typeof(Dictionary<,>))
                        {
                            try
                            {
                                var instance = Activator.CreateInstance(type);
                                prop.SetValue(obj, instance);
                            }
                            catch
                            {
                                // Ignore non-instantiable types
                            }
                        }
                    }
                }
            }
        }
    }
}
