namespace BGCS.Core
{
    using System.Text;

    /// <summary>
    /// Defines the public class <c>FileNameHelper</c>.
    /// </summary>
    public class FileNameHelper
    {
        private static readonly Dictionary<char, string> replacements = new()
        {
            { '*', "Star" },
            { ':', "Colon" },
            { '<', "LessThan" },
            { '>', "GreaterThan" },
            { '|', "Pipe" },
            { '?', "QuestionMark" },
            { '"', "Quote" },
        };

        private static readonly string[] reservedNames = [
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4",
            "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2",
            "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        private static readonly char[] invalidChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// Executes public operation <c>SanitizeFileName</c>.
        /// </summary>
        public static string SanitizeFileName(string fileName)
        {
            int separatorIndex = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf('\\'));
            string directory = separatorIndex < 0 ? string.Empty : fileName[..(separatorIndex + 1)];
            string name = separatorIndex < 0 ? fileName : fileName[(separatorIndex + 1)..];
            var sb = new StringBuilder(name.Length);

            // Replace characters based on the dictionary
            foreach (char c in name)
            {
                if (replacements.TryGetValue(c, out string? replacement))
                {
                    sb.Append(replacement);
                }
                else if (!invalidChars.Contains(c))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_'); // Replace invalid characters with '_'
                }
            }

            name = sb.ToString();

            // Handle reserved names (Windows-specific)
            foreach (string reservedName in reservedNames)
            {
                if (string.Equals(name, reservedName, StringComparison.OrdinalIgnoreCase))
                {
                    name = $"_{name}";
                    break;
                }
            }

            // Limit the length to 255 characters
            name = name.Length > 255 ? name[..255] : name;
            return directory + name;
        }
    }
}
