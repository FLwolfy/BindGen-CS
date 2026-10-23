using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Core.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

/// <summary>Emits bounded synchronous pinning scopes without transferring storage ownership.</summary>
internal static class BufferInputEmitter
{
    internal static bool Open(
        StringBuilder text,
        CSharpMethodPlan method,
        string[] arguments,
        IEnumerable<int> selectedInputs,
        string indent = "        ",
        bool captureAddresses = false
    ) {
        CSharpBufferInputPlan[] buffers = method.bufferInputs.Where(buffer => selectedInputs.Contains(buffer.parameterIndex)).ToArray();
        if (buffers.Length == 0) return false;
        CSharpBufferProtocolPlan[] counts = method.bufferProtocols.Where(protocol => protocol.writtenCountIndex >= 0 && selectedInputs.Contains(protocol.parameterIndex)).ToArray();
        foreach (CSharpBufferProtocolPlan protocol in counts)
        {
            string output = method.parameterNames[protocol.writtenCountIndex], native = Count(method, protocol);
            text.Append(indent).Append("global::System.Runtime.CompilerServices.Unsafe.SkipInit(out ").Append(output).AppendLine(");");
            text.Append(indent).Append(protocol.writtenCountType).Append(' ').Append(native).AppendLine(" = default;");
            string address = Local(method, "__bgcsCountAddress" + protocol.writtenCountIndex);
            text.Append(indent).Append("nint ").Append(address).Append(" = (nint)(&").Append(native).AppendLine(");");
            text.Append(indent).Append("if ((nuint)").Append(address).Append(" % (nuint)").Append(protocol.writtenCountAlignment)
                .AppendLine(" != 0) throw new global::System.ArgumentException(\"Managed written-count address does not satisfy native alignment.\");");
            arguments[protocol.writtenCountIndex] = captureAddresses ? address : "(" + method.parameterTypes[protocol.writtenCountIndex] + ")" + address;
        }
        Dictionary<int, string> calculated = [];
        foreach (CSharpBufferInputPlan buffer in buffers)
        {
            string name = method.parameterNames[buffer.parameterIndex];
            string length = Local(method, "__bgcsLength" + buffer.parameterIndex);
            text.Append(indent).Append(method.parameterTypes[buffer.lengthIndex]).Append(' ').Append(length).Append(" = checked((")
                .Append(method.parameterTypes[buffer.lengthIndex]).Append(")((nuint)").Append(name).Append(".Length");
            if (buffer.lengthUnit == NativeLengthUnit.Bytes) text.Append(" * (nuint)sizeof(").Append(buffer.elementType).Append(')');
            text.AppendLine("));");
            if (calculated.TryGetValue(buffer.lengthIndex, out string? shared))
                text.Append(indent).Append("if (").Append(length).Append(" != ").Append(shared).AppendLine(") throw new global::System.ArgumentException(\"Buffers sharing a native length require equal declared bounds.\");");
            else { calculated.Add(buffer.lengthIndex, length); arguments[buffer.lengthIndex] = length; }
            if (!buffer.allowsNull)
                text.Append(indent).Append("if (").Append(name).Append(".IsEmpty) throw new global::System.ArgumentException(\"This native buffer does not permit an empty null span.\", ")
                    .Append(ContextEmitter.Literal(name.TrimStart('@'))).AppendLine(");");
        }
        foreach (CSharpBufferProtocolPlan protocol in counts)
            text.Append(indent).Append("fixed (int* ").Append(ManagedCount(method, protocol)).Append(" = &")
                .Append(method.parameterNames[protocol.writtenCountIndex]).AppendLine(")");
        foreach (CSharpBufferInputPlan buffer in buffers)
            text.Append(indent).Append("fixed (").Append(buffer.elementType).Append("* ").Append(Local(method, "__bgcsPointer" + buffer.parameterIndex))
                .Append(" = ").Append(method.parameterNames[buffer.parameterIndex]).AppendLine(")");
        text.Append(indent).AppendLine("{");
        foreach (CSharpBufferInputPlan buffer in buffers)
        {
            string pointer = Local(method, "__bgcsPointer" + buffer.parameterIndex);
            text.Append(indent).Append("    if ((nuint)").Append(pointer).Append(" % (nuint)").Append(buffer.alignment)
                .AppendLine(" != 0) throw new global::System.ArgumentException(\"Managed buffer address does not satisfy native alignment; use aligned native storage through Raw.\");");
            if (captureAddresses)
            {
                string address = Local(method, "__bgcsBufferAddress" + buffer.parameterIndex);
                text.Append(indent).Append("    nint ").Append(address).Append(" = (nint)").Append(pointer).AppendLine(";");
                arguments[buffer.parameterIndex] = address;
            }
            else arguments[buffer.parameterIndex] = "(" + method.parameterTypes[buffer.parameterIndex] + ")" + pointer;
        }
        for (int countIndex = 0; countIndex < counts.Length; countIndex++)
        {
            CSharpBufferProtocolPlan count = counts[countIndex];
            string address = ManagedCount(method, count);
            text.Append(indent).Append("    if (").Append(address).AppendLine(" == null) throw new global::System.ArgumentException(\"A written-count output requires valid managed storage.\");");
            foreach (CSharpBufferInputPlan buffer in buffers)
                RefuseOverlap(address, "sizeof(int)", Local(method, "__bgcsPointer" + buffer.parameterIndex),
                    "checked((nuint)" + method.parameterNames[buffer.parameterIndex] + ".Length * (nuint)sizeof(" + buffer.elementType + "))");
            foreach (CSharpValueParameterPlan value in method.valueParameters.Where(value => selectedInputs.Contains(value.parameterIndex)))
                RefuseOverlap(address, "sizeof(int)", ValueInputEmitter.Address(method, value.parameterIndex), "sizeof(" + value.managedElementType + ")");
            for (int previous = 0; previous < countIndex; previous++)
                RefuseOverlap(address, "sizeof(int)", ManagedCount(method, counts[previous]), "sizeof(int)");
        }
        // These public counts are validated copies, not borrowed native values.
        // Establish non-aliasing first, before zeroing any output can alter an input.
        foreach (CSharpBufferProtocolPlan count in counts)
            text.Append(indent).Append("    ").Append(method.parameterNames[count.writtenCountIndex]).AppendLine(" = default;");
        return true;

        void RefuseOverlap(
            string first,
            string firstSize,
            string second,
            string secondSize
        ) {
            text.Append(indent).Append("    if ((nuint)(").Append(secondSize).Append(") != 0 && ((nuint)").Append(first)
                .Append(" <= (nuint)").Append(second).Append(" ? (nuint)").Append(second).Append(" - (nuint)").Append(first)
                .Append(" < (nuint)(").Append(firstSize).Append(") : (nuint)").Append(first).Append(" - (nuint)").Append(second)
                .Append(" < (nuint)(").Append(secondSize).AppendLine("))) throw new global::System.ArgumentException(\"A validated written-count output cannot overlap borrowed buffer/value storage or another output; use Raw for an aliasing native protocol.\");");
        }
    }

    internal static void Close(
        StringBuilder text,
        bool opened,
        string indent = "        "
    ) {
        if (opened) text.Append(indent).AppendLine("}");
    }

    internal static void Complete(
        StringBuilder text,
        CSharpMethodPlan method,
        IEnumerable<int> selectedInputs,
        string indent = "        "
    ) {
        foreach (CSharpBufferProtocolPlan protocol in method.bufferProtocols.Where(protocol => protocol.writtenCountIndex >= 0 && selectedInputs.Contains(protocol.parameterIndex)))
        {
            string native = Count(method, protocol), bound = Local(method, "__bgcsLength" + protocol.parameterIndex);
            if (protocol.writtenCountIsSigned)
                text.Append(indent).Append("if (").Append(native).AppendLine(" < 0) throw new global::System.InvalidOperationException(\"Native written count is negative.\");");
            text.Append(indent).Append("if ((ulong)").Append(native).Append(" > (ulong)").Append(bound)
                .AppendLine(") throw new global::System.InvalidOperationException(\"Native written count exceeds the supplied buffer capacity.\");");
            if (protocol.lengthUnit == NativeLengthUnit.Bytes)
                text.Append(indent).Append("if ((ulong)").Append(native).Append(" % ").Append(protocol.elementSize)
                    .AppendLine("UL != 0) throw new global::System.InvalidOperationException(\"Native written byte count does not contain complete elements.\");");
            text.Append(indent).Append(method.parameterNames[protocol.writtenCountIndex]).Append(" = checked((int)((ulong)").Append(native);
            if (protocol.lengthUnit == NativeLengthUnit.Bytes) text.Append(" / ").Append(protocol.elementSize).Append("UL");
            text.AppendLine("));");
        }
    }

    private static string Count(
        CSharpMethodPlan method,
        CSharpBufferProtocolPlan protocol
    ) => Local(method, "__bgcsWritten" + protocol.writtenCountIndex);
    private static string ManagedCount(
        CSharpMethodPlan method,
        CSharpBufferProtocolPlan protocol
    ) => Local(method, "__bgcsManagedCount" + protocol.writtenCountIndex);

    private static string Local(
        CSharpMethodPlan method,
        string name
    ) {
        while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
        return name;
    }
}
