using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Hosting;
using BGCS.Frontend.Clang.Model.Metadata;
using BGCS.Infrastructure.IO;
using ClangSharp.Interop;

namespace BGCS.Frontend.Clang.Parsing;

internal static unsafe class IncludeDependencyCollector
{
    internal static IReadOnlyList<string> GetSourceFiles(
        CppCompilation compilation,
        IHostPathService hostPaths
    ) {
        HashSet<string> paths = new(hostPaths.comparer);
        Exception? failure = null;
        CXInclusionVisitor visitor = (
            fileHandle,
            _,
            _,
            _
        ) => {
            if (failure is not null) return;
            try
            {
                using CXString name = ((CXFile)fileHandle).Name;
                string path = name.ToString();
                if (Path.IsPathFullyQualified(path)) paths.Add(Path.GetFullPath(path));
            }
            catch (Exception exception) { failure = exception; }
        };
        compilation.translationUnit.GetInclusions(visitor, default);
        GC.KeepAlive(visitor);
        if (failure is not null) throw new IOException("Parsed source provenance could not be collected.", failure);
        return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    internal static ImmutableArray<InputDependency> Collect(
        CppCompilation compilation,
        IHostPathService paths
    ) {
        return Collect(compilation.translationUnit, paths);
    }

    internal static ImmutableArray<InputDependency> Collect(
        CXTranslationUnit translationUnit,
        IHostPathService paths
    ) {
        Dictionary<string, InputDependency> dependencies = new(paths.comparer);
        Exception? failure = null;
        CXInclusionVisitor visitor = (
            fileHandle,
            _,
            _,
            _
        ) => {
            if (failure is not null)
                return;
            try
            {
                CXFile file = (CXFile)fileHandle;
                using CXString nativeName = file.Name;
                string path = Path.GetFullPath(nativeName.ToString());
                if (!File.Exists(path)) // The synthesized unsaved entry is not a physical dependency.
                    return;
                ReadOnlySpan<byte> parsedBytes = translationUnit.GetFileContents(file, out _);
                string digest = Convert.ToHexString(SHA256.HashData(parsedBytes)).ToLowerInvariant();
                using FileStream current = File.OpenRead(path);
                if (current.Length != parsedBytes.Length || !string.Equals(digest, Convert.ToHexString(SHA256.HashData(current)).ToLowerInvariant(), StringComparison.Ordinal))
                    throw new IOException($"Native input changed while parsing: '{path}'.");
                dependencies.TryAdd(path, new(path, PhysicalPathResolver.Resolve(path, paths), parsedBytes.Length, new ContentDigest(digest)));
            }
            catch (Exception exception)
            {
                failure = exception; // Never unwind a managed exception through libclang's callback frame.
            }
        };
        translationUnit.GetInclusions(visitor, default);
        GC.KeepAlive(visitor);
        if (failure is not null)
            throw new IOException("The parser input snapshot could not be verified.", failure);
        return dependencies.Values.OrderBy(input => input.path, StringComparer.Ordinal).ToImmutableArray();
    }
}
