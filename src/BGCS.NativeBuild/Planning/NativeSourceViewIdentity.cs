using System;
using System.Collections.Generic;
using System.IO;
using BGCS.Contracts.Common;
using BGCS.Contracts.Requests;
using BGCS.Infrastructure.Verification;

namespace BGCS.NativeBuild.Planning;

// Original input paths remain meaningful. Only explicit Host-owned source
// mappings and this operation's aggregate filename have portable identities;
// neither mapping is applied to a C/C++ language token or macro replacement.
internal sealed class NativeSourceViewIdentity
{
    private readonly Dictionary<string, string> m_files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal NativeSourceViewIdentity(
        string input,
        FrontendRequest source
    ) {
        HashSet<string> identities = new(StringComparer.Ordinal) { "bgcs-native-source-view-input" };
        foreach (var mapping in source.sourcePathMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Value) || mapping.Value.IndexOfAny(['\r', '\n', '\0']) >= 0)
                throw new InvalidDataException("A generated source identity must be a nonempty portable filename without line-control characters.");
            string file = Path.GetFullPath(mapping.Key, source.pathBase).Replace('\\', '/');
            string logical = ArtifactDigestVerifier.ValidateRelativePath(mapping.Value);
            if (!m_files.TryAdd(file, logical) || !identities.Add(logical))
                throw new InvalidDataException("Generated source identities repeat a physical or logical input path.");
        }
        if (!m_files.TryAdd(Path.GetFullPath(input).Replace('\\', '/'), "bgcs-native-source-view-input"))
            throw new InvalidDataException("A source mapping conflicts with the owned native aggregate input.");
    }

    internal string Normalize(string source) => NativeLineDirectiveReader.Rewrite(source, directive =>
        m_files.TryGetValue(directive.file.Replace('\\', '/'), out string? logical)
            ? source.Substring(directive.start, directive.fileStart - directive.start)
                + logical.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
                + source.Substring(directive.fileStart + directive.fileLength,
                    directive.start + directive.length - directive.fileStart - directive.fileLength)
            : null);

    internal SourceLocation? Map(SourceLocation? location) => location is { } origin
        && m_files.TryGetValue(origin.file.Replace('\\', '/'), out string? logical) ? origin with { file = logical } : location;
}
