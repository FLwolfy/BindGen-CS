using BGCS.Infrastructure.IO;
using BGCS.Infrastructure.Workers;
using System;
using System.Collections.Generic;

namespace BGCS.Frontend.Clang.Parsing.Arguments;

/// <summary>Prevents unstructured driver arguments from replacing the resolved target and parser identity.</summary>
public static class NativeArgumentIdentityValidator
{
    /// <summary>Rejects hidden CPU, ABI, byte-order and unsupported parser resource/plugin substitutions after argument-file expansion.</summary>
    /// <param name="arguments">Fully expanded effective arguments, including separately inspected linker-tail arguments.</param>
    /// <param name="context">The diagnostic stage name.</param>
    /// <param name="rejectParserSubstitution">Whether parser driver mode, language, resources and native plugins must remain the declared frontend identity.</param>
    public static void Validate(
        IEnumerable<string> arguments,
        string context,
        bool rejectParserSubstitution = false
    ) {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        foreach (string original in arguments)
        {
            ArgumentNullException.ThrowIfNull(original);
            foreach (string token in Forwarded(original))
            {
                if (token.StartsWith('@') || token == "--config" || token.StartsWith("--config=", StringComparison.Ordinal))
                    throw new ArgumentException("Effective " + context + " arguments contain an unexpanded response or configuration input.");
                if (token is "--target" or "-target" or "-triple" or "-aux-triple" or "-m32" or "-m64" or "-mx32" or "-m16"
                    or "-mbig-endian" or "-mlittle-endian" or "-EB" or "-EL" or "-msoft-float" or "-mhard-float"
                    or "-freg-struct-return" or "-fpcc-struct-return" or "-target-abi" or "-target-cpu" or "-aux-target-cpu"
                    or "/Gr" or "-Gr" or "/Gv" or "-Gv" or "/Gz" or "-Gz" or "-mrtd" or "-msseregparm" or "-mregparm" or "-fdefault-calling-conv"
                    || Joined(token, "--target") || Joined(token, "-target") || Joined(token, "-triple") || Joined(token, "-aux-triple")
                    || Joined(token, "-mabi") || Joined(token, "-mfloat-abi") || Joined(token, "-target-abi")
                    || Joined(token, "-target-cpu") || Joined(token, "-aux-target-cpu")
                    || Joined(token, "-mregparm") || Joined(token, "-fdefault-calling-conv")
                    || token.StartsWith("-fcall-used-", StringComparison.Ordinal) || token.StartsWith("-fcall-saved-", StringComparison.Ordinal))
                    throw new ArgumentException("Effective " + context + " arguments cannot replace the resolved CPU, native target ABI, calling ABI or byte order: " + token);
                if (token is "-specs" or "--specs" || Joined(token, "-specs") || Joined(token, "--specs"))
                    throw new NotSupportedException("Effective " + context + " arguments cannot load opaque compiler specs that can replace the frozen target or tool operations.");
                if (token is "-load" or "-plugin" or "-add-plugin" or "-fplugin" || Joined(token, "-fplugin") || Joined(token, "-fplugin-arg"))
                    throw new NotSupportedException("Effective " + context + " arguments cannot load opaque native AST plugins; compiler-preprocessed facts cannot verify post-preprocessing declaration mutations.");
                if (rejectParserSubstitution && (token is "-resource-dir" or "-load" or "-plugin" or "-add-plugin" or "-cc1" or "-cc1as" or "-x"
                    || Joined(token, "-resource-dir") || Joined(token, "--driver-mode") || Joined(token, "-fplugin") || Joined(token, "-fpass-plugin")
                    || Joined(token, "-std") || token.StartsWith("/std:", StringComparison.OrdinalIgnoreCase)))
                    throw new NotSupportedException("Effective " + context + " arguments cannot replace the declared parser language, standard, builtin resources or native plugin identity: " + token);
            }
        }
    }

    private static bool Joined(
        string value,
        string option
    ) => value.StartsWith(option + "=", StringComparison.Ordinal);
    private static IEnumerable<string> Forwarded(string value)
    {
        if (value.StartsWith("/clang:", StringComparison.OrdinalIgnoreCase))
            foreach (string token in Forwarded(value[7..])) yield return token;
        else if (value.StartsWith("-Wl,", StringComparison.Ordinal))
            foreach (string part in value[4..].Split(','))
                foreach (string token in Forwarded(part)) yield return token;
        else yield return value;
    }
}
