using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Core.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

internal static class TextInputEmitter
{
    internal static IEnumerable<int> ForwardedInputs(
        CSharpMethodPlan method,
        IEnumerable<int> nativeInputs
    ) {
        var counts = method.stringInputs.Where(input => input.lengthIndex is not null).Select(input => input.lengthIndex!.Value).ToHashSet();
        return nativeInputs.Where(index => !counts.Contains(index));
    }

    internal static void Append(
        StringBuilder text,
        CSharpMethodPlan method,
        string runtimeNamespace,
        string[] arguments,
        IEnumerable<int>? selectedInputs = null,
        string indent = "        "
    ) {
        foreach (CSharpStringInputPlan input in method.stringInputs)
        {
            if (selectedInputs is not null && !selectedInputs.Contains(input.parameterIndex)) continue;
            string local = "__bgcsText" + input.parameterIndex;
            while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == local)) local += "_";
            text.Append(indent).Append("using var ").Append(local).Append(" = global::").Append(CSharpNames.Namespace(runtimeNamespace))
                .Append('.').Append(input.encoding switch
                {
                    NativeTextEncoding.Utf8 => "Utf8Codec",
                    NativeTextEncoding.Utf16 => "Utf16Codec",
                    NativeTextEncoding.Utf32 => "Utf32Codec",
                    _ => throw new InvalidOperationException("A text input omitted its checked codec.")
                }).Append(input.lengthIndex is null ? ".EncodeNullTerminated(" : ".EncodeCounted(").Append(method.parameterNames[input.parameterIndex])
                .Append(", ").Append(input.allowsNull ? "true" : "false");
            if (input.encoding != NativeTextEncoding.Utf8)
                text.Append(", ").Append(input.byteOrder switch
                {
                    NativeTextByteOrder.Native => "null",
                    NativeTextByteOrder.LittleEndian => "false",
                    NativeTextByteOrder.BigEndian => "true",
                    _ => throw new InvalidOperationException("A wide text input omitted its checked byte order.")
                });
            text.Append(", (nuint)").Append(input.alignment).Append(", this.m_temporaryAllocator");
            if (input.lengthIndex is not null) text.Append(", ").Append(input.maximumCodeUnits);
            text.AppendLine(");");
            arguments[input.parameterIndex] = "(" + method.parameterTypes[input.parameterIndex] + ")" + local + ".pointer";
            if (input.lengthIndex is int length)
                arguments[length] = "checked((" + method.parameterTypes[length] + ")((long)" + local + ".encodedCodeUnitCount"
                    + (input.lengthUnit == NativeLengthUnit.Bytes ? " * " + input.codeUnitWidth : string.Empty) + "))";
        }
    }
}
