using System;
using System.IO;
using BGCS.Configuration;
using BGCS.Core.Text;

namespace BGCS.Output;

internal static class SingleFileOutputNameResolver
{
    internal static string Resolve(CsCodeGeneratorConfig config)
    {
        string fileName = string.IsNullOrWhiteSpace(config.singleFileOutputName) ? "Bindings.cs" : config.singleFileOutputName.Trim();
        if (fileName.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(fileName))
        {
            throw new ArgumentException("SingleFileOutputName must be a file name in the output root.", nameof(config.singleFileOutputName));
        }

        fileName = FileNameHelper.SanitizeFileName(fileName);
        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
        {
            fileName += ".cs";
        }
        else if (!fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("SingleFileOutputName must use the .cs extension.", nameof(config.singleFileOutputName));
        }

        return fileName;
    }
}
