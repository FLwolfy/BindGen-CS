using System.IO;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Native;
using Xunit;

namespace BGCS.NativeBridge.Tests;

public sealed class NeutralStringConstantSelectionTests
{
    [Fact]
    public void ImmutableNamedStringVariablesRemainPublicConstantRootsAlongsideObjectMacros()
    {
        NativeType character = new(new("char"), NativeTypeCategory.Primitive, "char", 1, 1) { primitiveKind = "Char", isSigned = true };
        NativeType qualified = new(new("const-char"), NativeTypeCategory.Qualified, "const char", 1, 1) { elementType = character.id, qualifier = "Const" };
        NativeType array = new(new("array"), NativeTypeCategory.Array, "const char[4]", 4, 1) { elementType = qualified.id, arrayLength = 4 };
        NativeStringConstant value = new(NativeStringLiteralKind.Ordinary, [81, 0, 82, 0], 1, new(EvidenceLevel.Declared, "compiler:array-literal"));
        NativeDeclaration variable = new(new("variable"), "Field", "NAMED", "NAMED", array.id, new("literal.h", 1, 1))
        { role = "Variable", isStatic = true, stringConstant = value };
        NativeDeclaration macro = new(new("macro"), "Macro", "MACRO", "MACRO", null, new("literal.h", 2, 1))
        { macro = new(false, [], true), stringConstant = value };
        NativeModule source = new("windows-x64-msvc", "x86_64-pc-windows-msvc", 8, [character, qualified, array], [variable, macro], []) { language = SourceLanguage.C };
        DeclarationSelection selected = new DeclarationSelector(global::System.StringComparer.Ordinal).Select(source, new(), ["literal.h"], Path.GetTempPath(), includeTransitivelyReferencedHeaders: false);
        Assert.Contains(variable.id, selected.publicDeclarations);
        Assert.Contains(macro.id, selected.publicDeclarations);
        InteropModule interop = new NativeConstantAnalyzer().Apply(selected.module, new("literal", source.targetId, source.pointerSize, [], [], []), selected.publicDeclarations);
        Assert.Equal(2, interop.stringConstants.Length);
        Assert.Contains(interop.stringConstants, constant => constant.sourceDeclaration == variable.id);
    }
}
