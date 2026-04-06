namespace BGCS.Patching
{
    using System.Collections.Generic;

    /// <summary>
    /// Defines the public class <c>PatchContext</c>.
    /// </summary>
    public class PatchContext
    {
        private readonly string stage;
        private readonly List<string> files = [];

        /// <summary>
        /// Initializes a new instance of <see cref="PatchContext"/>.
        /// </summary>
        public PatchContext(string stage)
        {
            this.stage = stage;
        }

        /// <summary>
        /// Exposes public member <c>stage</c>.
        /// </summary>
        public string Stage => stage;

        /// <summary>
        /// Executes public operation <c>CopyFromInput</c>.
        /// </summary>
        public void CopyFromInput(string root, List<string> input)
        {
            foreach (var file in input)
            {
                var relativePath = Path.GetRelativePath(root, file);
                var fullPath = Path.Combine(stage, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.Copy(file, fullPath, true);
                if (!files.Contains(relativePath))
                {
                    files.Add(relativePath);
                }
            }
        }

        /// <summary>
        /// Returns computed data from <c>GetFullPath</c>.
        /// </summary>
        public string GetFullPath(string path)
        {
            return Path.Combine(stage, path);
        }

        /// <summary>
        /// Executes public operation <c>CopyToOutput</c>.
        /// </summary>
        public void CopyToOutput(string root)
        {
            foreach (var file in files)
            {
                var relativePath = file;
                var fullPath = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.Copy(GetFullPath(file), fullPath, true);
            }
        }

        /// <summary>
        /// Executes public operation <c>CopyFromStage</c>.
        /// </summary>
        public void CopyFromStage(PatchContext context)
        {
            foreach (var file in context.files)
            {
                var relativePath = file;
                var fullPath = Path.Combine(stage, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.Copy(context.GetFullPath(file), fullPath, true);
                if (!files.Contains(relativePath))
                {
                    files.Add(relativePath);
                }
            }
        }

        /// <summary>
        /// Executes public operation <c>ReadFile</c>.
        /// </summary>
        public string ReadFile(string path)
        {
            var fullPath = Path.Combine(stage, path);
            return File.ReadAllText(fullPath);
        }

        /// <summary>
        /// Writes output for <c>WriteFile</c>.
        /// </summary>
        public void WriteFile(string path, string content)
        {
            var fullPath = Path.Combine(stage, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            if (!files.Contains(path))
            {
                files.Add(path);
            }
        }
    }
}
