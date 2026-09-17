namespace BGCS.Core
{
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
            ArgumentNullException.ThrowIfNull(path);
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

        /// <summary>
        /// Finds the nearest parent directory containing a <c>.git</c> directory.
        /// </summary>
        /// <returns>The repository root, or <see langword="null"/> when no parent is a Git work tree.</returns>
        public static string? FindBase()
        {
            DirectoryInfo? directory = new(Path.GetFullPath(Environment.CurrentDirectory));
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }
    }
}
