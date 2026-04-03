namespace BGCS
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    public delegate (Guid, string)? GuidSelector(string text, Match match);

    public partial class ComGUIDExtractor
    {

        private readonly List<(Regex regex, GuidSelector selector)> patterns = [];
        private readonly Regex regex = RegexExtraceGUID();

        [GeneratedRegex("DEFINE_GUID\\((.*?)\\)", RegexOptions.Compiled | RegexOptions.Singleline)]
        private static partial Regex RegexExtraceGUID();
        public ComGUIDExtractor()
        {
            AddPattern(regex, DefaultSelector);
        }

        public virtual void AddPattern(Regex regex, GuidSelector? selector)
        {
            patterns.Add((regex, selector ?? DefaultSelector));
        }

        private static (Guid, string)? DefaultSelector(string text, Match match)
        {
            if (match.Groups.Count < 2) return null;
            var group = match.Groups[1];
            var parts = group.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length < 12)
            {
                return null;
            }

            var name = parts[0].Replace("IID_", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (!TryParseHexUInt32(parts[1], out uint a) ||
                !TryParseHexUInt16(parts[2], out ushort b) ||
                !TryParseHexUInt16(parts[3], out ushort c) ||
                !TryParseHexByte(parts[4], out byte d) ||
                !TryParseHexByte(parts[5], out byte e) ||
                !TryParseHexByte(parts[6], out byte f) ||
                !TryParseHexByte(parts[7], out byte g) ||
                !TryParseHexByte(parts[8], out byte h) ||
                !TryParseHexByte(parts[9], out byte i) ||
                !TryParseHexByte(parts[10], out byte j) ||
                !TryParseHexByte(parts[11], out byte k))
            {
                return null;
            }

            return (new(a, b, c, d, e, f, g, h, i, j, k), name);
        }

        private static bool TryParseHexUInt32(string value, out uint result)
        {
            value = StripHexPrefix(value);
            return uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseHexUInt16(string value, out ushort result)
        {
            value = StripHexPrefix(value);
            return ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseHexByte(string value, out byte result)
        {
            value = StripHexPrefix(value);
            return byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        }

        private static string StripHexPrefix(string value)
        {
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return value[2..];
            }

            return value;
        }

        public virtual void ExtractGuidsFromFiles(IEnumerable<string> headerFiles, CsComCodeGenerator generator, List<(string, Guid)> guids, Dictionary<string, Guid> guidMap)
        {
            foreach (var file in headerFiles)
            {
                string text = File.ReadAllText(file);
                ExtractGuids(text, generator.Settings, generator, guids, guidMap);
            }
        }

        public virtual void ExtractGuidsFromFile(string file, CsComCodeGenerator generator, List<(string, Guid)> guids, Dictionary<string, Guid> guidMap)
        {
            string text = File.ReadAllText(file);
            ExtractGuids(text, generator.Settings, generator, guids, guidMap);
        }

        public virtual void ExtractGuids(string text, CsCodeGeneratorConfig config, CsComCodeGenerator generator, List<(string, Guid)> guids, Dictionary<string, Guid> guidMap)
        {
            foreach (var (regex, selector) in patterns)
            {
                DoMatch(text, config, generator, guids, guidMap, regex, selector);
            }
        }

        public virtual void DoMatch(string text, CsCodeGeneratorConfig config, CsComCodeGenerator generator, List<(string, Guid)> guids, Dictionary<string, Guid> guidMap, Regex regex, GuidSelector selector)
        {
            var match = regex.Matches(text);
            for (int x = 0; x < match.Count; x++)
            {
                (Guid guid, string name)? item = selector(text, match[x]);

                if (!item.HasValue)
                {
                    continue;
                }

                (Guid guid, string name) = item.Value;

                if (config.IIDMappings.ContainsKey(name))
                    continue;


                if (guidMap.TryGetValue(name, out Guid other))
                {
                    if (other != guid)
                    {
                        generator.LogWarn($"overwriting GUID {other} with {guid} for {name}");
                        guidMap[name] = guid;
                        guids.Remove((name, other));
                        guids.Add((name, guid));
                    }
                }
                else
                {
                    guids.Add((name, guid));
                    guidMap.Add(name, guid);
                }
            }
        }
    }
}
