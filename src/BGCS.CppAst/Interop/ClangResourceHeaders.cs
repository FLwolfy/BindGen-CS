using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace BGCS.CppAst.Interop;

internal static class ClangResourceHeaders
{
    private const string C_RESOURCE_NAME = "BGCS.CppAst.Clang.Headers.zip";
    private static readonly Lazy<string> m_directory = new(Extract);

    internal static string directory => m_directory.Value;

    private static string Extract()
    {
        using Stream resource = typeof(ClangResourceHeaders).Assembly.GetManifestResourceStream(C_RESOURCE_NAME)
            ?? throw new InvalidOperationException("The parser's Clang resource headers are missing from its assembly.");
        string hash = Convert.ToHexString(SHA256.HashData(resource));
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BGCS", "ClangResources");
        string destination = Path.Combine(cache, hash);
        string sentinel = Path.Combine(destination, "include", "stddef.h");
        if (File.Exists(sentinel))
            return destination;

        Directory.CreateDirectory(cache);
        string staging = Path.Combine(cache, hash + "." + Guid.NewGuid().ToString("N"));
        try
        {
            resource.Position = 0;
            using (var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: true))
                archive.ExtractToDirectory(staging);
            try
            {
                Directory.Move(staging, destination);
            }
            catch (IOException) when (File.Exists(sentinel))
            {
                // Another parser process published the identical immutable resource bundle.
            }
            return destination;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }
}
