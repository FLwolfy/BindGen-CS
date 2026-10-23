using System;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.CSharp.Planning;

internal static class StringResultPlanner
{
    internal static CSharpEmissionPlan Apply(CSharpEmissionPlan plan)
    {
        var results = ImmutableArray.CreateBuilder<CSharpStringResultPlan>();
        var omissions = plan.ownedProjectionOmissions.ToBuilder();
        var functionsById = plan.api.interop.functions.ToLookup(function => function.id);
        for (int index = 0; index < plan.methods.Length; index++)
        {
            CSharpMethodPlan method = plan.methods[index];
            InteropFunction function = functionsById[method.nativeExport].Single();
            if (function.returnString is not { } text) continue;
            try { results.Add(Plan(index, method, function, text)); }
            catch (NotSupportedException failure) { omissions.Add(new(function.id, null, function.source, failure.Message)); }
        }
        return plan with { stringResults = results.ToImmutable(), ownedProjectionOmissions = omissions.ToImmutable() };

        CSharpStringResultPlan Plan(
            int index,
            CSharpMethodPlan method,
            InteropFunction function,
            StringContract text
        ) {
            if (function.error is not null || function.asynchronous is not null || method.returnProjection is not null)
                Reject("Direct string results require a synchronous pointer return without a status, async or competing managed result protocol.");
            int width = text.encoding switch { NativeTextEncoding.Utf8 => 1, NativeTextEncoding.Utf16 => 2, NativeTextEncoding.Utf32 => 4, _ => 0 };
            InteropType pointer = Resolve(function.returnType);
            if (width == 0 || text.codeUnitWidth != width || !Known(text.evidence) || !Known(text.lifetime.evidence)
                || text.direction != NativeParameterDirection.Output || text.allowsNull is null || text.maximumCodeUnits <= 0
                || pointer.category != NativeTypeCategory.Pointer || pointer.elementType is not StableId)
                Reject("A string result requires explicit encoding, matching code-unit width, null allowance and bounded copy/scan evidence for an actual native pointer.");
            InteropType element = Resolve(pointer.elementType!.Value);
            if (!Integral(element) || element.size != width)
                Reject("The native string pointee must be an established integral code unit with the exact declared width.");
            if (width != 1 && text.byteOrder is not (NativeTextByteOrder.Native or NativeTextByteOrder.LittleEndian or NativeTextByteOrder.BigEndian))
                Reject("A multibyte string result requires an explicit native, little-endian or big-endian code-unit order.");
            if (text.lengthUnit is not (NativeLengthUnit.Bytes or NativeLengthUnit.CodeUnits))
                Reject("Native string result extents must use bytes or encoding code units.");
            OwnershipContract? ownership = function.returnOwnership ?? text.ownership;
            if (ownership is null || !Known(ownership.evidence) || !Known(ownership.lifetime.evidence)
                || ownership.lifetime.kind != text.lifetime.kind || ownership.lifetime.owner != text.lifetime.owner)
                Reject("A string result requires matching explicit text and native result ownership/lifetime evidence.");
            if (function.returnOwnership is { } first && text.ownership is { } second
                && (first.kind != second.kind || first.lifetime.kind != second.lifetime.kind || first.lifetime.owner != second.lifetime.owner
                    || first.releaseFunction != second.releaseFunction || first.allocator != second.allocator || first.releaseThreadDomain != second.releaseThreadDomain))
                Reject("Conflicting text and result ownership contracts cannot select a string cleanup protocol.");
            int? release = null;
            if (ownership!.kind == NativeOwnershipKind.Borrowed)
            {
                if (ownership.lifetime.kind != NativeLifetimeKind.Module || ownership.lifetime.owner is not null
                    || ownership.releaseFunction is not null || ownership.allocator is not null || ownership.releaseThreadDomain is not null)
                    Reject("Borrowed string copying requires explicit validity through actual module unload; Call or unrelated Owner lifetimes cannot be extended after return.");
            }
            else if (ownership.kind == NativeOwnershipKind.Owned)
            {
                if (ownership.lifetime.kind != NativeLifetimeKind.Owner || ownership.lifetime.owner != function.id
                    || ownership.releaseThreadDomain != "any" || ownership.releaseFunction is not StableId || ownership.allocator is null)
                    Reject("An owned string result requires an explicit matching release, allocation/release domain and any-thread cleanup contract.");
                int releaseIndex = -1;
                for (int candidate = 0; candidate < plan.methods.Length; candidate++) if (plan.methods[candidate].nativeExport == ownership.releaseFunction) releaseIndex = candidate;
                if (releaseIndex < 0 || releaseIndex == index) Reject("An owned string requires a selected independent matching release import.");
                InteropFunction cleanup = functionsById[plan.methods[releaseIndex].nativeExport].Single();
                if (cleanup.parameters.Length != 1 || Resolve(cleanup.returnType).primitiveKind != "Void" || Resolve(cleanup.parameters[0].type).id != pointer.id
                    || cleanup.error is not null || cleanup.asynchronous is not null || cleanup.returnString is not null || cleanup.returnOwnership is not null
                    || HasProtocol(cleanup.parameters[0]))
                    Reject("Owned string cleanup requires the exact synchronous void(T*) ABI without a competing parameter or result protocol.");
                release = releaseIndex;
            }
            else Reject("Unknown or transferred result ownership does not establish a managed string copy lifetime.");

            int? exact = null;
            CSharpStringResultLengthPlan? length = null;
            if (text.fixedLength is int fixedLength)
            {
                if (text.lengthParameter is not null || text.zeroTerminated == true || fixedLength < 0) Reject("A string result must select exactly one extent source.");
                if (text.lengthUnit == NativeLengthUnit.Bytes && fixedLength % width != 0) Reject("A byte string extent must contain complete native code units.");
                int units = text.lengthUnit == NativeLengthUnit.Bytes ? fixedLength / width : fixedLength;
                if (units > text.maximumCodeUnits) Reject("The fixed string extent exceeds its configured copy cap.");
                if (units > int.MaxValue / width) Reject("The fixed string extent exceeds a managed string's supported byte range.");
                exact = units;
            }
            else if (text.lengthParameter is StableId lengthId)
            {
                if (text.zeroTerminated == true) Reject("A string result must choose a terminator or exact length channel, without ambiguous truncation.");
                int count = -1;
                for (int input = 0; input < function.parameters.Length; input++) if (function.parameters[input].id == lengthId) count = input;
                if (count < 0 || HasProtocol(function.parameters[count])) Reject("A result length requires an independent native count parameter without another storage protocol.");
                InteropType countType = Resolve(function.parameters[count].type);
                bool output = text.lengthDirection == NativeParameterDirection.Output;
                string carrier = method.parameterTypes[count];
                if (output)
                {
                    if (text.lengthOutputCallBounded != true || countType.category != NativeTypeCategory.Pointer || countType.elementType is not StableId item
                        || !carrier.EndsWith('*') || Qualified(item))
                        Reject("An output string length requires explicit synchronous nonretention and a mutable integral pointer count.");
                    countType = Resolve(countType.elementType!.Value);
                    carrier = carrier[..^1].Trim();
                }
                else if (text.lengthDirection != NativeParameterDirection.Input || text.lengthOutputCallBounded is not null)
                    Reject("A result length channel must explicitly choose a scalar input or call-bounded output pointer.");
                int alignment = countType.alignment ?? 0;
                if (!Integral(countType) || countType.size is not (1 or 2 or 4 or 8) || countType.isSigned is null
                    || alignment <= 0 || alignment > 8 || (alignment & (alignment - 1)) != 0
                    || carrier is not ("byte" or "sbyte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint"))
                    Reject("A string result count needs an established integral carrier, signedness and representable output alignment.");
                length = new(count, carrier, output, text.lengthUnit, alignment);
            }
            else if (text.zeroTerminated != true) Reject("A string result needs a known zero terminator or an exact native length protocol.");
            if (length is null && (text.lengthDirection != NativeParameterDirection.Unknown || text.lengthOutputCallBounded is not null))
                Reject("Result length channel options require their exact native parameter.");
            int[] inputs = Enumerable.Range(0, function.parameters.Length).Where(input => length is not { output: true } || input != length.parameterIndex).ToArray();
            if (!inputs.All(input => OverloadPlanner.SupportsDirectInput(plan, method, function, input)))
                Reject("A direct string result input lacks a supported synchronous nonretaining friendly protocol; its raw ABI remains available.");
            int[] visible = OverloadPlanner.PublicInputs(method, inputs).ToArray();
            string resultType = text.allowsNull!.Value ? "string?" : "string";
            CSharpFacadeMethodPlan signature = new(method.name, resultType, visible.Select(input => OverloadPlanner.InputType(method, input)).ToImmutableArray(),
                visible.Select(input => method.parameterNames[input]).ToImmutableArray())
            {
                nativeExport = method.nativeExport,
                inputIndices = visible.ToImmutableArray(),
                parameterModifiers = visible.Select(input => OverloadPlanner.InputModifier(method, input)).ToImmutableArray(),
                documentation = method.documentation,
                documentationContent = method.documentationContent is { } documentation
                    ? documentation with { parameters = visible.Select(input => documentation.parameters[input]).ToImmutableArray() } : null
            };
            return new(index, signature, text.encoding, width, text.byteOrder, text.allowsNull.Value, text.maximumCodeUnits)
            { exactCodeUnits = exact, length = length, releaseIndex = release };
        }
        InteropType Resolve(StableId id)
        {
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = plan.api.interop.GetType(type.elementType!.Value);
            return type;
        }
        bool Qualified(StableId id)
        {
            InteropType type = plan.api.interop.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified)
            {
                if (type.category == NativeTypeCategory.Qualified) return true;
                type = plan.api.interop.GetType(type.elementType!.Value);
            }
            return false;
        }
    }

    private static bool HasProtocol(InteropParameter parameter) => parameter.ownership is not null || parameter.callback is not null || parameter.buffer is not null || parameter.text is not null || parameter.singleValue is not null;
    private static bool Integral(InteropType type) => type.category == NativeTypeCategory.Primitive && type.primitiveKind is
        "Char" or "SignedChar" or "UnsignedChar" or "Short" or "UnsignedShort" or "Int" or "UnsignedInt" or "Long" or "UnsignedLong"
        or "LongLong" or "UnsignedLongLong" or "WChar" or "Char8" or "Char16" or "Char32";
    private static bool Known(Evidence evidence) => evidence.level is EvidenceLevel.Declared or EvidenceLevel.UserAsserted or EvidenceLevel.Verified && !string.IsNullOrWhiteSpace(evidence.origin);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn] private static void Reject(string reason) => throw new NotSupportedException(reason);
}
