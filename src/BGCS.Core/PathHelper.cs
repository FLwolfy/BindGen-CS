namespace BGCS.Core
{
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Defines the public class <c>PathHelper</c>.
    /// </summary>
    public static unsafe class PathHelper
    {
        /// <summary>
        /// Returns computed data from <c>GetPath</c>.
        /// </summary>
        public static string GetPath(string path)
        {
            if (path == null) return null;
            if (Path.IsPathRooted(path)) return path;
            if (Path.IsPathFullyQualified(path)) return path;
            string sanitizedPath = Path.GetFullPath(path);

            fixed (char* p = sanitizedPath)
            {
                char* pPath = p;
                char* end = pPath + sanitizedPath.Length;
                while (pPath != end)
                {
                    char c = *pPath;
                    if (c == '\\' || c == '/')
                    {
                        *pPath = Path.DirectorySeparatorChar;
                    }
                    pPath++;
                }
            }

            return sanitizedPath;
        }

        static readonly char[] separators = [Path.PathSeparator, Path.AltDirectorySeparatorChar];

        /// <summary>
        /// Executes public operation <c>FindBase</c>.
        /// </summary>
        public static string? FindBase()
        {
            ReadOnlySpan<char> dirD = Environment.CurrentDirectory;
            Span<char> dir = stackalloc char[dirD.Length];
            dirD.CopyTo(dir);
            while (dir.IsEmpty)
            {
                dir = dir.TrimEnd(separators);
                if (Directory.Exists($"{dir}{Path.PathSeparator}.git"))
                {
                    return dir.ToString();
                }

                var span = Path.GetDirectoryName(dir);
                ref var ba = ref MemoryMarshal.GetReference(span);
                dir = MemoryMarshal.CreateSpan(ref ba, span.Length);
            }

            return null;
        }
    }
}
