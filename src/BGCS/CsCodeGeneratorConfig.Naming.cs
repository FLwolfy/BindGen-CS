using System.Collections.Concurrent;
using System.Text;

namespace BGCS
;
    /// <summary>
    /// Defines the public class <c>CsCodeGeneratorConfig</c>.
    /// </summary>
    public partial class CsCodeGeneratorConfig
    {
        private readonly ConcurrentDictionary<string, string> nameCache = new();

        /// <summary>
        /// Returns computed data from <c>GetCsCleanName</c>.
        /// </summary>
        public string GetCsCleanName(string name)
        {
            if (TypeMappings.TryGetValue(name, out string? mappedName))
            {
                return mappedName;
            }

            string cacheKey = $"clean:{name}";
            if (nameCache.TryGetValue(cacheKey, out string? cacheEntry))
            {
                return cacheEntry;
            }

            StringBuilder sb = new();
            bool isCaps = name.IsCaps();
            bool wasLowerCase = false;
            bool wasNumber = false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_')
                {
                    wasLowerCase = true;
                    continue;
                }

                if (isCaps)
                {
                    c = char.ToLower(c);
                }

                if (i == 0)
                {
                    c = char.ToUpper(c);
                }

                if (wasLowerCase || wasNumber)
                {
                    c = char.ToUpper(c);
                    wasLowerCase = false;
                }

                sb.Append(c);
                wasNumber = char.IsDigit(c);
            }

            if (sb.Length > 1 && sb[^1] == 'T')
            {
                var c = sb[^2];
                if (char.IsLower(c) || c == '_')
                {
                    sb.Remove(sb.Length - 1, 1);
                }
            }

            string newName = sb.ToString();

            foreach (var item in NameMappings)
            {
                newName = newName.Replace(item.Key, item.Value, StringComparison.InvariantCultureIgnoreCase);
            }

            nameCache.TryAdd(cacheKey, newName);

            return newName;
        }

        /// <summary>
        /// Returns computed data from <c>GetCsCleanNameWithConvention</c>.
        /// </summary>
        public string GetCsCleanNameWithConvention(string name, NamingConvention convention, bool removeTailingT = true)
        {
            if (TypeMappings.TryGetValue(name, out string? mappedName))
            {
                return mappedName;
            }

            string cacheKey = $"conv:{convention}:{removeTailingT}:{name}";
            if (nameCache.TryGetValue(cacheKey, out string? cacheEntry))
            {
                return cacheEntry;
            }

            string newName = NamingHelper.ConvertTo(name, convention);

            if (removeTailingT)
            {
                if (newName.Length > 1 && newName[^1] == 'T')
                {
                    var c = newName[^2];
                    if (char.IsLower(c) || c == '_')
                    {
                        newName = newName.Remove(newName.Length - 1, 1);
                    }
                }
            }

            foreach (var item in NameMappings)
            {
                newName = newName.Replace(item.Key, item.Value, StringComparison.InvariantCultureIgnoreCase);
            }

            nameCache.TryAdd(cacheKey, newName);

            return newName;
        }
    }
