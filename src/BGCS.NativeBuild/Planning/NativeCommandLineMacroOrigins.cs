using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace BGCS.NativeBuild.Planning;

// This records declared command-line text, not compiler builtin or header facts.
// Final token equality is required; driver transformations which cannot be
// reproduced exactly simply retain unknown provenance.
internal sealed class NativeCommandLineMacroOrigins
{
    private readonly Dictionary<string, NativeMacroDirectiveReader.Definition> m_definitions = new(StringComparer.Ordinal);
    private readonly bool m_unambiguous = true;
    internal HashSet<string> names { get; } = new(StringComparer.Ordinal);
    internal string fingerprint { get; }

    internal NativeCommandLineMacroOrigins(
        ImmutableArray<string> arguments,
        bool msvc,
        bool rawStrings
    ) {
        List<string> inputs = [];
        for (int index = 0; index < arguments.Length; index++)
        {
            string option = arguments[index];
            if (option == "--") break;
            if (TakesOperand(option, msvc)) { index++; continue; }
            bool define = option.StartsWith("-D", StringComparison.Ordinal) || msvc && option.StartsWith("/D", StringComparison.Ordinal);
            bool undefine = option.StartsWith("-U", StringComparison.Ordinal) || msvc && option.StartsWith("/U", StringComparison.Ordinal);
            if (!define && !undefine)
            {
                // An unknown forwarding/driver option may consume a following -D
                // token as its own operand. Preserve unknown provenance rather than
                // inventing a command-line definition from that token's spelling.
                if ((option.StartsWith('-') || msvc && option.StartsWith('/')) && !KnownOption(option, msvc)) m_unambiguous = false;
                continue;
            }
            string value = option[2..];
            inputs.Add(option);
            if (value.Length == 0)
            {
                if (++index >= arguments.Length) throw new ArgumentException("An executed native macro option omitted its value.");
                value = arguments[index];
                inputs.Add(value);
            }
            int equals = value.IndexOf('=');
            string declaration = equals < 0 ? value : value[..equals];
            string replacement = equals < 0 ? "1" : value[(equals + 1)..];
            ImmutableArray<NativeMacroDirectiveReader.Definition> parsed = NativeMacroDirectiveReader.Read(
                "#define " + declaration + " " + replacement, rawStrings: rawStrings);
            if (parsed.Length != 1 || declaration.Any(character => character is '\r' or '\n')) continue;
            var definition = parsed[0];
            names.Add(definition.name);
            if (undefine) m_definitions.Remove(definition.name);
            else m_definitions[definition.name] = definition;
        }
        fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { msvc, unambiguous = m_unambiguous, inputs })));
    }

    internal bool Declares(string name) => names.Contains(name);

    internal bool Matches(NativeMacroDirectiveReader.Definition actual) => m_unambiguous && m_definitions.TryGetValue(actual.name, out var declared)
        && declared.functionLike == actual.functionLike && declared.parameters.SequenceEqual(actual.parameters, StringComparer.Ordinal)
        && declared.tokens == actual.tokens;

    private static bool TakesOperand(
        string option,
        bool msvc
    ) => option is "-I" or "-isystem" or "-iquote" or "-idirafter" or "-F" or "-iframework"
        or "-include" or "-imacros" or "-include-pch" or "-isysroot" or "--sysroot" or "-iprefix" or "-iwithprefix" or "-iwithprefixbefore"
        or "-resource-dir" or "-target" or "--target" or "-arch" or "-x" or "-o" or "-MF" or "-MT" or "-MQ" or "--config-user-dir" or "--config-system-dir"
        || msvc && option is "/I" or "/external:I" or "/FI" or "/Fi" or "/sourceDependencies";

    private static bool KnownOption(
        string option,
        bool msvc
    ) {
        if (msvc) return option is "/nologo" or "/MD" or "/MDd" or "/MT" or "/MTd" or "/TC" or "/TP" or "/EHsc" or "/Zc:__cplusplus"
                or "/Zc:preprocessor" or "/permissive-" or "/P" or "/PD" or "/C" or "/J"
            || option.StartsWith("/I", StringComparison.Ordinal) || option.StartsWith("/external:I", StringComparison.Ordinal)
            || option.StartsWith("/FI", StringComparison.Ordinal) || option.StartsWith("/Fi", StringComparison.Ordinal)
            || option.StartsWith("/W", StringComparison.Ordinal) || option.StartsWith("/external:W", StringComparison.Ordinal)
            || option.StartsWith("/std:", StringComparison.Ordinal) || option.StartsWith("/source-charset:", StringComparison.Ordinal)
            || option.StartsWith("/execution-charset:", StringComparison.Ordinal) || option.StartsWith("/Zp", StringComparison.Ordinal);
        return option is "--no-default-config" or "-fPIC" or "-fpic" or "-fno-pic" or "-fno-PIC" or "-nostdinc" or "-nostdinc++" or "-nostdlibinc" or "-nobuiltininc"
                or "-fshort-wchar" or "-fshort-enums" or "-fno-short-enums" or "-funsigned-char" or "-fsigned-char" or "-ffreestanding" or "-fno-builtin"
                or "-fms-extensions" or "-fno-ms-extensions" or "-fms-compatibility" or "-fno-ms-compatibility" or "-fdeclspec" or "-fno-declspec"
                or "-fno-exceptions" or "-fexceptions" or "-fno-rtti" or "-frtti" or "-pthread" or "-w" or "-pedantic" or "-pedantic-errors"
                or "-E" or "-MD" or "-MMD" or "-MP" or "-dM" or "-CC" or "-g" or "-g0" or "-g1" or "-g2" or "-g3"
            || option.StartsWith("-I", StringComparison.Ordinal) || option.StartsWith("-F", StringComparison.Ordinal)
            || option.StartsWith("-isystem=", StringComparison.Ordinal) || option.StartsWith("--sysroot=", StringComparison.Ordinal)
            || option.StartsWith("-W", StringComparison.Ordinal) || option.StartsWith("-O", StringComparison.Ordinal)
            || option.StartsWith("-std=", StringComparison.Ordinal) || option.StartsWith("--target=", StringComparison.Ordinal)
            || option.StartsWith("-fms-compatibility-version=", StringComparison.Ordinal) || option.StartsWith("-fpack-struct=", StringComparison.Ordinal)
            || option.StartsWith("--rsp-quoting=", StringComparison.Ordinal);
    }
}
