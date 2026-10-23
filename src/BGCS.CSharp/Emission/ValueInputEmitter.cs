using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BGCS.Core.Interop;
using BGCS.CSharp.Planning;

namespace BGCS.CSharp.Emission;

/// <summary>Supplies checked original storage borrows or exact source-level value copies for one synchronous native call.</summary>
internal static class ValueInputEmitter
{
    internal static bool Open(
        StringBuilder text,
        CSharpMethodPlan method,
        string[] arguments,
        IEnumerable<int> selectedInputs,
        string indent = "        ",
        bool captureAddresses = false
    ) {
        CSharpValueParameterPlan[] values = method.valueParameters.Where(value => selectedInputs.Contains(value.parameterIndex)).ToArray();
        if (values.Length == 0) return false;
        if (values.Any(value => !Enum.IsDefined(value.allocationMode))) throw new InvalidOperationException("A native value has no implemented allocation strategy.");
        CSharpValueParameterPlan[] borrows = values.Where(value => value.allocationMode == CSharpValueAllocationMode.AddressCheckedPinnedBorrow).ToArray();
        foreach (CSharpValueParameterPlan value in borrows.Where(value => value.direction == NativeParameterDirection.Output))
            text.Append(indent).Append("global::System.Runtime.CompilerServices.Unsafe.SkipInit(out ").Append(method.parameterNames[value.parameterIndex]).AppendLine(");");
        foreach (CSharpValueParameterPlan value in borrows)
        {
            int index = value.parameterIndex;
            text.Append(indent).Append("fixed (").Append(value.managedElementType).Append("* ").Append(Pointer(method, index)).Append(" = &");
            if (value.direction == NativeParameterDirection.Input)
                text.Append("global::System.Runtime.CompilerServices.Unsafe.AsRef(in ").Append(method.parameterNames[index]).Append(')');
            else text.Append(method.parameterNames[index]);
            text.AppendLine(")");
        }
        text.Append(indent).AppendLine("{");
        foreach (CSharpValueParameterPlan value in values.Where(value => value.allocationMode == CSharpValueAllocationMode.NativeValueCopy))
        {
            int index = value.parameterIndex;
            if (value.direction is not (NativeParameterDirection.Input or NativeParameterDirection.Output))
                throw new InvalidOperationException("An original native by-value copy cannot acquire ref alias semantics.");
            text.Append(indent).Append("    ").Append(value.elementType).Append(' ').Append(CopiedLocal(method, index)).Append(" = ")
                .Append(value.direction == NativeParameterDirection.Input ? RecipeConversionEmitter.Input(method, index, method.parameterNames[index]) : "default").AppendLine(";");
        }
        foreach (CSharpValueParameterPlan value in values)
        {
            int index = value.parameterIndex;
            string address = Address(method, index);
            text.Append(indent).Append("    nint ").Append(address).Append(" = (nint)(").Append(value.allocationMode == CSharpValueAllocationMode.NativeValueCopy ? "&" + CopiedLocal(method, index) : Pointer(method, index)).AppendLine(");");
            text.Append(indent).Append("    if (").Append(address).AppendLine(" == 0) throw new global::System.ArgumentException(\"This native value requires non-null borrowed storage.\");");
            text.Append(indent).Append("    if ((nuint)").Append(address).Append(" % (nuint)").Append(value.alignment)
                .AppendLine(" != 0) throw new global::System.ArgumentException(\"Managed value address does not satisfy native alignment; use aligned native storage through Raw.\");");
            arguments[index] = captureAddresses ? address : "(" + method.parameterTypes[index] + ")" + address;
        }
        return true;
    }

    internal static void Close(
        StringBuilder text,
        bool opened,
        string indent = "        "
    ) {
        if (opened) text.Append(indent).AppendLine("}");
    }

    internal static bool IsAddress(
        CSharpMethodPlan method,
        int index
    ) => method.bufferInputs.Any(buffer => buffer.parameterIndex == index)
        || method.bufferProtocols.Any(buffer => buffer.writtenCountIndex == index) || method.valueParameters.Any(value => value.parameterIndex == index);

    internal static string CopiedLocal(
        CSharpMethodPlan method,
        int index
    ) => Unique(method, "__bgcsCopiedValue" + index);
    internal static string Address(
        CSharpMethodPlan method,
        int index
    ) => Unique(method, "__bgcsValueAddress" + index);
    private static string Pointer(
        CSharpMethodPlan method,
        int index
    ) => Unique(method, "__bgcsPinnedValue" + index);
    private static string Unique(
        CSharpMethodPlan method,
        string name
    ) {
        while (method.parameterNames.Any(parameter => parameter.TrimStart('@') == name)) name += "_";
        return name;
    }
}
