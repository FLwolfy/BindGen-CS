using System;
using System.Globalization;
using System.Text;
using BGCS.Analysis.Bridge;
using BGCS.Core.Bridge;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using BGCS.NativeBridge.Planning;

namespace BGCS.NativeBridge.Emission;

internal static partial class ConfiguredFamilyAssertionEmitter
{
    private static bool EmitOwnerVariantPathOrTime(
        NativeBridgePlan module,
        CppFamilyBinding binding,
        StandardValueOperation facts,
        StableId ownerId,
        string owner,
        int index,
        StringBuilder output,
        Action<string, string> check
    ) {
        if (facts.family is not (StandardValueKind.UniquePointer or StandardValueKind.SharedPointer or StandardValueKind.Variant
            or StandardValueKind.Path or StandardValueKind.Duration or StandardValueKind.TimePoint)) return false;
        string element = Name(index, "element"), count = Name(index, "count"), read = Name(index, "read");
        string Native(StableId type) => NativeTypeSpelling.Format(module.source, type);
        string Bare(string expression) => "typename std::remove_cv<typename std::remove_reference<decltype(" + expression + ")>::type>::type";
        void BoundedCount(string expression)
        {
            output.Append("using ").Append(count).Append(" = ").Append(Bare(expression)).AppendLine(";");
            check("std::is_integral<" + count + ">::value && std::is_unsigned<" + count + ">::value && !std::is_same<" + count
                + ", bool>::value && sizeof(" + count + ") <= sizeof(uint64_t)", "bounded unsigned native count");
        }
        if (facts.family is StandardValueKind.UniquePointer or StandardValueKind.SharedPointer)
        {
            output.Append("using ").Append(element).Append(" = ").Append(Native(facts.argumentTypes[0])).AppendLine(";");
            check("std::is_same<typename " + owner + "::element_type, " + element + ">::value", "exact native smart-owner element type");
            check("std::is_same<decltype(std::declval<const " + owner + "&>().get()), " + element + "*>::value", "exact borrowed smart-owner pointer");
            check("std::is_same<decltype(std::declval<const " + owner + "&>().operator bool()), bool>::value", "native smart-owner presence result");
            check("std::is_default_constructible<" + owner + ">::value && std::is_nothrow_destructible<" + owner + ">::value", "default construction and nonthrowing smart-owner cleanup");
            if (facts.family == StandardValueKind.UniquePointer)
                check("!std::is_copy_constructible<" + owner + ">::value && std::is_nothrow_move_constructible<" + owner + ">::value", "move-only nonthrowing unique-owner transfer");
            else check("std::is_nothrow_copy_constructible<" + owner + ">::value", "nonthrowing shared-owner copy");
            return true;
        }
        if (facts.family == StandardValueKind.Variant)
        {
            if (binding.variantAccess != CppVariantAccess.MemberGet) throw new InvalidOperationException("A configured variant lost its explicit member-access protocol.");
            BoundedCount("std::declval<const " + owner + "&>().index()");
            for (int alternative = 0; alternative < facts.argumentTypes.Length; alternative++)
                check("std::is_same<" + Bare("std::declval<const " + owner + "&>().template get<" + alternative.ToString(CultureInfo.InvariantCulture) + ">()")
                    + ", " + Bare("std::declval<" + Native(facts.argumentTypes[alternative]) + ">()") + ">::value", "exact configured variant alternative " + alternative.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        if (facts.family == StandardValueKind.Path)
        {
            if (binding.pathFormat is null || !Enum.IsDefined(binding.pathFormat.Value)) throw new InvalidOperationException("A configured path lost its explicit UTF-8 conversion.");
            string conversion = binding.pathFormat == NativePathFormat.NativeUtf8 ? "u8string" : "generic_u8string";
            output.Append("using ").Append(read).Append(" = ").Append(Bare("std::declval<const " + owner + "&>()." + conversion + "()")).AppendLine(";")
                .Append("using ").Append(element).Append(" = typename std::remove_cv<typename std::remove_pointer<decltype(std::declval<const ")
                .Append(read).AppendLine("&>().data())>::type>::type;");
            check("std::is_pointer<decltype(std::declval<const " + read + "&>().data())>::value", "native path data pointer");
            check("std::is_integral<" + element + ">::value && !std::is_same<" + element + ", bool>::value && sizeof(" + element + ") == 1", "one-byte UTF-8 path code units");
            BoundedCount("std::declval<const " + read + "&>().size()");
            check("std::is_same<" + Bare("*std::declval<const " + read + "&>().data()") + ", " + element + ">::value", "exact counted UTF-8 path byte access");
            return true;
        }
        StandardLibraryFactReader reader = new(module.familyBindings);
        StableId durationId = facts.family == StandardValueKind.TimePoint ? facts.argumentTypes[1] : ownerId;
        StandardValueOperation duration = reader.Read(module.source, durationId)!;
        NativeType nativeDuration = StandardLibraryFactReader.Resolve(module.source, durationId);
        NativeDeclaration durationDeclaration = module.source.GetDeclaration(nativeDuration.declaration!.Value);
        NativeDurationPeriod period = reader.ReadPeriod(module.source, durationId)
            ?? throw new InvalidOperationException("A configured chrono protocol lost its exact rational period.");
        string durationOwner = facts.family == StandardValueKind.Duration ? owner : Native(durationId);
        string representation = Native(duration.argumentTypes[0]);
        string periodType = Native(durationDeclaration.templateArguments[1].type!.Value);
        check("std::is_same<typename " + durationOwner + "::rep, " + representation + ">::value", "exact native duration representation");
        check("std::is_same<typename " + durationOwner + "::period, " + periodType + ">::value", "exact native duration period type");
        check(durationOwner + "::period::num == " + period.numerator.ToString(CultureInfo.InvariantCulture) + "LL && " + durationOwner + "::period::den == "
            + period.denominator.ToString(CultureInfo.InvariantCulture) + "LL", "exact normalized native duration ratio");
        check("std::is_same<decltype(std::declval<const " + durationOwner + "&>().count()), " + representation + ">::value", "exact native duration count result");
        if (facts.family == StandardValueKind.TimePoint)
        {
            check("std::is_same<typename " + owner + "::clock, " + Native(facts.argumentTypes[0]) + ">::value", "exact native clock identity");
            check("std::is_same<typename " + owner + "::duration, " + Native(durationId) + ">::value", "exact native time-point duration");
            check("std::is_same<" + Bare("std::declval<const " + owner + "&>().time_since_epoch()") + ", " + Native(durationId) + ">::value", "exact native epoch-count operation");
        }
        return true;
    }
}
