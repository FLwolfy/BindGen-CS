using System;
using System.Collections.Generic;
using BGCS.Configuration;
using BGCS.Configuration.Mapping;
using BGCS.Configuration.Naming;
using BGCS.Conversion;
using BGCS.CppAst.Model.Declarations;
using BGCS.CppAst.Model.Types;
using Xunit;

namespace BGCS.Tests;

public class CsCodeGeneratorConfigTypeApiTests
{
    [Fact]
    public void MappingHelpers_ShouldFindConfiguredMappings()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.enumMappings.Add(new EnumMapping("EType", "ETypeFriendly", null));
        cfg.functionMappings.Add(new FunctionMapping("DoThing", "DoThingFriendly", null, [], []));
        cfg.classMappings.Add(new TypeMapping("NativeStruct", "NativeStructFriendly", null));
        cfg.handleMappings.Add(new HandleMapping("NativeHandle", "NativeHandleFriendly", null));
        cfg.delegateMappings.Add(new DelegateMapping("OnValue", "void", "int"));

        Assert.True(cfg.TryGetEnumMapping("EType", out var enumMapping));
        Assert.Equal("ETypeFriendly", enumMapping!.friendlyName);
        Assert.True(cfg.TryGetFunctionMapping("DoThing", out var fnMapping));
        Assert.Equal("DoThingFriendly", fnMapping!.friendlyName);
        Assert.True(cfg.TryGetTypeMapping("NativeStruct", out var typeMapping));
        Assert.Equal("NativeStructFriendly", typeMapping!.friendlyName);
        Assert.True(cfg.TryGetHandleMapping("NativeHandle", out var handleMapping));
        Assert.Equal("NativeHandleFriendly", handleMapping!.friendlyName);
        Assert.True(cfg.TryGetDelegateMapping("OnValue", out var delegateMapping));
        Assert.Equal("int", delegateMapping!.signature);
    }

    [Fact]
    public void ArrayMappingHelper_ShouldMatchPrimitiveAndSize()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.arrayMappings.Add(new ArrayMapping(CppPrimitiveKind.Float, 4, "Vector4"));

        CppArrayType arrayType = new(default, CppPrimitiveType.@float, 4);

        Assert.True(cfg.TryGetArrayMapping(arrayType, out string? mapping));
        Assert.Equal("Vector4", mapping);
    }

    [Fact]
    public void TypeConverter_ShouldMapExtendedWindowsPrimitiveTypes()
    {
        CsCodeGeneratorConfig config = new();

        Assert.Equal("int", config.typeConverter.Convert(CppPrimitiveType.@long, CsTypeStyle.Raw));
        Assert.Equal("char", config.typeConverter.Convert(CppPrimitiveType.wChar, CsTypeStyle.Raw));
        Assert.Equal("Int128", config.typeConverter.Convert(CppPrimitiveType.int128, CsTypeStyle.Raw));
        Assert.Equal("UInt128", config.typeConverter.Convert(CppPrimitiveType.uInt128, CsTypeStyle.Raw));
        Assert.Equal("Half", config.typeConverter.Convert(CppPrimitiveType.float16, CsTypeStyle.Raw));
    }

    [Fact]
    public void TypeConverter_ShouldUseFriendlyMappingsForDeclaredTypes()
    {
        CsCodeGeneratorConfig config = new();
        CppEnum nativeMode = new(default, "NATIVE_MODE") { integerType = CppPrimitiveType.@int };
        CppClass nativeContext = new(default, "native_context");
        config.enumMappings.Add(new EnumMapping("NATIVE_MODE", "NativeMode", null));
        config.classMappings.Add(new TypeMapping("native_context", "NativeContext", null));

        Assert.Equal("NativeMode", config.typeConverter.Convert(nativeMode, CsTypeStyle.Raw));
        Assert.Equal("NativeContext", config.typeConverter.Convert(nativeContext, CsTypeStyle.Raw));

        config.typeMappings["NATIVE_MODE"] = "ExternalMode";
        config.typeMappings["native_context"] = "ExternalContext";
        Assert.Equal("ExternalMode", config.typeConverter.Convert(nativeMode, CsTypeStyle.Raw));
        Assert.Equal("ExternalContext", config.typeConverter.Convert(nativeContext, CsTypeStyle.Raw));
    }

    [Fact]
    public void TypeConverter_ShouldRequireExplicitMappingsForUnexposedAndGenericTypes()
    {
        CsCodeGeneratorConfig config = new();
        CppUnexposedType opaque = new(default, "NativeOpaque");
        CppGenericType generic = new(default, new CppUnexposedType(default, "Vector"));
        generic.genericArguments.Add(CppPrimitiveType.@int);

        Assert.Throws<UnexposedTypeException>(() => config.typeConverter.Convert(opaque, CsTypeStyle.Raw));
        Assert.Throws<NotSupportedException>(() => config.typeConverter.Convert(generic, CsTypeStyle.Raw));

        config.typeMappings["NativeOpaque"] = "nint";
        config.typeMappings["Vector<int>"] = "NativeIntVector";
        Assert.Equal("nint", config.typeConverter.Convert(opaque, CsTypeStyle.Raw));
        Assert.Equal("NativeIntVector", config.typeConverter.Convert(generic, CsTypeStyle.Raw));
    }

    [Fact]
    public void FunctionAliasMappings_ShouldAddAndResolveAlias()
    {
        CsCodeGeneratorConfig cfg = new();
        FunctionAliasMapping alias = new("glBindTexture", "glBindTextureEXT", "BindTextureExt", null);
        cfg.AddFunctionAliasMapping(alias);

        Assert.True(cfg.TryGetFunctionAliasMapping("glBindTexture", "glBindTextureEXT", out var resolved));
        Assert.Equal("BindTextureExt", resolved!.friendlyName);
        Assert.Same(alias, cfg.GetFunctionAliasMapping("glBindTexture", "glBindTextureEXT"));
    }

    [Theory]
    [InlineData("out", "output")]
    [InlineData("ref", "reference")]
    [InlineData("in", "input")]
    [InlineData("base", "baseValue")]
    [InlineData("void", "voidValue")]
    [InlineData("int", "intValue")]
    [InlineData("lock", "lock0")]
    [InlineData("event", "evnt")]
    [InlineData("string", "str")]
    [InlineData("params", "@params")]
    [InlineData("", "unknown0")]
    public void GetParameterName_ShouldApplyReservedNameRules(string input, string expected)
    {
        CsCodeGeneratorConfig cfg = new();

        string actual = cfg.GetParameterName(0, input);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeParameterName_ShouldUseConventionAndPrefixDigit()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.parameterNamingConvention = NamingConvention.CamelCase;

        string normalized = cfg.NormalizeParameterName("9_VALUE");

        Assert.Equal("_9Value", normalized);
        Assert.Equal("value", cfg.NormalizeParameterName(string.Empty));
    }

    [Fact]
    public void NormalizeValue_ShouldRequireExplicitMappingForProjectConstructors()
    {
        CsCodeGeneratorConfig cfg = new();

        Assert.Equal("default", cfg.NormalizeValue("NULL"));
        Assert.Equal("float.MaxValue", cfg.NormalizeValue("FLT_MAX"));
        Assert.Equal("1.17549435E-38f", cfg.NormalizeValue("FLT_MIN"));
        Assert.Equal("-1.17549435E-38f", cfg.NormalizeValue("-FLT_MIN"));
        Assert.Equal("1", cfg.NormalizeValue("true"));
        Assert.Equal("ExternalPoint(1,2)", cfg.NormalizeValue("ExternalPoint(1,2)"));
        cfg.knownDefaultValueNames["ExternalPoint(1,2)"] = "new Point2(1, 2)";
        Assert.Equal("new Point2(1, 2)", cfg.NormalizeValue("ExternalPoint(1,2)"));
    }

    [Fact]
    public void DefaultConfig_ShouldNotAssumeProjectOrPlatformSdkTypedefs()
    {
        CsCodeGeneratorConfig cfg = CsCodeGeneratorConfig.@default;

        Assert.Equal("byte", cfg.typeMappings["uint8_t"]);
        Assert.False(cfg.typeMappings.ContainsKey("Uint8"));
        Assert.False(cfg.typeMappings.ContainsKey("BOOL"));
        Assert.False(cfg.typeMappings.ContainsKey("HWND"));
        Assert.Empty(cfg.ignoredTypes);
        Assert.Empty(cfg.ignoredTypedefs);
        HashSet<string> standardTypes =
        [
            "uint8_t", "uint16_t", "uint32_t", "uint64_t", "int8_t", "int16_t",
            "int32_t", "int64_t", "int64_t*", "unsigned char", "signed char",
            "char", "size_t", "bool"
        ];
        Assert.All(cfg.typeMappings.Keys, key => Assert.Contains(key, standardTypes));

        cfg.typeMappings["Uint8"] = "byte";
        Assert.Equal("byte", cfg.typeMappings["Uint8"]);
    }

    [Fact]
    public void GetConstantName_ShouldPreferKnownMapping()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.knownConstantNames["GL_TRIANGLES"] = "Triangles";

        Assert.Equal("Triangles", cfg.GetConstantName("GL_TRIANGLES"));
    }

    [Fact]
    public void GetEnumNamePrefixAndEnumName_ShouldStripPrefixAndKeepReadableName()
    {
        CsCodeGeneratorConfig cfg = new();
        EnumPrefix prefix = cfg.GetEnumNamePrefix("IMGUI_COLOR");

        string enumItem = cfg.GetEnumName("IMGUI_COLOR_RED", prefix);

        Assert.Equal("Red", enumItem);
    }

    [Fact]
    public void GetExtensionNamePrefixAndName_ShouldBuildPascalCaseName()
    {
        CsCodeGeneratorConfig cfg = new();

        string prefix = cfg.GetExtensionNamePrefix("my_ext");
        string extensionName = cfg.GetExtensionName("MY_EXT_DRAW_INDIRECT", prefix);

        Assert.Equal("MY_EXT", prefix);
        Assert.Equal("DrawIndirect", extensionName);
    }

    [Fact]
    public void GetCsFunctionName_ShouldUseFriendlyNameAndIgnoredParts()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.ignoredParts.Add("Gl");
        cfg.functionMappings.Add(new FunctionMapping("vkDoThing", "DoThingFriendly", null, [], []));

        Assert.Equal("DoThingFriendly", cfg.GetCsFunctionName("vkDoThing"));
        Assert.Equal("CreateBuffer", cfg.GetCsFunctionName("glCreateBuffer"));
    }

    [Fact]
    public void GetCsFunctionName_ShouldPreferMappingThenStripLongestExactPrefix()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.functionPrefixes.Add("Im");
        cfg.functionPrefixes.Add("ImGuizmo_");
        cfg.functionMappings.Add(new FunctionMapping("ImGuizmo_BeginFrame", "StartFrame", null, [], []));

        Assert.Equal("SetRect", cfg.GetCsFunctionName("ImGuizmo_SetRect"));
        Assert.Equal("StartFrame", cfg.GetCsFunctionName("ImGuizmo_BeginFrame"));
        Assert.Equal("ImGuizmoSetRect", cfg.GetCsFunctionName("imGuizmoSetRect"));

        cfg.functionNamingConvention = NamingConvention.Unknown;
        Assert.Equal("SetRect", cfg.GetCsFunctionName("ImGuizmo_SetRect"));
    }

    [Fact]
    public void GetBoolType_ShouldRespectConfiguredBoolMode()
    {
        CsCodeGeneratorConfig cfg = new();
        Assert.Equal(BoolType.Bool8, cfg.boolType);
        Assert.Equal("Bool8", cfg.GetBoolType());

        cfg.boolType = BoolType.Bool8;
        Assert.Equal("Bool8", cfg.GetBoolType());

        cfg.boolType = BoolType.Bool32;
        Assert.Equal("Bool32", cfg.GetBoolType());

        cfg.boolType = BoolType.Byte;
        Assert.Equal("byte", cfg.GetBoolType());

        cfg.boolType = BoolType.Int32;
        Assert.Equal("int", cfg.GetBoolType());
    }

    [Fact]
    public void DelegatePointerType_ShouldUseConfiguredNativeBoolRepresentation()
    {
        CsCodeGeneratorConfig cfg = new()
        {
            boolType = BoolType.Byte,
            delegatesAsVoidPointer = false
        };
        CppFunctionType callbackType = new(default, CppPrimitiveType.@bool)
        {
            callingConvention = CppCallingConvention.C
        };
        callbackType.parameters.Add(new CppParameter(default, CppPrimitiveType.@bool, "enabled"));

        string pointerType = cfg.GetDelegatePointerType(callbackType, withConvention: true);

        Assert.Contains("<byte, byte>", pointerType);
        Assert.DoesNotContain("bool", pointerType, StringComparison.Ordinal);
    }

    [Fact]
    public void DelegatePointerType_ShouldRespectDelegatesAsVoidPointerFlag()
    {
        CsCodeGeneratorConfig cfg = new() { boolType = BoolType.Bool8 };
        CppFunctionType callbackType = new(default, CppPrimitiveType.@void);
        callbackType.parameters.Add(new CppParameter(default, CppPrimitiveType.@int, "value"));

        cfg.delegatesAsVoidPointer = false;
        string pointerType = cfg.GetDelegatePointerType(callbackType);
        Assert.Contains("delegate* unmanaged[", pointerType);
        Assert.Contains("int", pointerType);

        cfg.delegatesAsVoidPointer = true;
        Assert.Equal("void*", cfg.GetDelegatePointerType(callbackType));
    }

    [Fact]
    public void ParameterSignatureHelpers_ShouldPreservePointersAndBoolMapping()
    {
        CsCodeGeneratorConfig cfg = new() { boolType = BoolType.Bool8 };
        List<CppParameter> parameters =
        [
            new CppParameter(default, CppPrimitiveType.@bool, "enabled"),
            new CppParameter(default, new CppPointerType(default, CppPrimitiveType.@int, System.IntPtr.Size), "values")
        ];

        string signature = cfg.GetParameterSignature(parameters, canUseOut: false);
        string nameless = cfg.GetNamelessParameterSignature(parameters, canUseOut: false);
        string marshalling = cfg.WriteFunctionMarshalling(parameters);

        Assert.Contains("Bool8 enabled", signature);
        Assert.Contains("int* values", signature);
        Assert.Equal("Bool8, int*", nameless);
        Assert.Equal("enabled, values", marshalling);
    }

    [Fact]
    public void TryGetDefaultValue_ShouldResolveMappedDefaults()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.functionMappings.Add(new FunctionMapping("set_mode", "SetMode", null, new Dictionary<string, string> { ["mode"] = "MY_MODE_FAST" }, []));
        cfg.knownEnumPrefixes["MyMode"] = "MY_MODE";

        CppTypedef typedef = new(default, "MyMode", CppPrimitiveType.@int);
        CppParameter parameter = new(default, typedef, "mode");

        bool found = cfg.TryGetDefaultValue("set_mode", parameter, out string? defaultValue);

        Assert.True(found);
        Assert.Equal("MyMode.Fast", defaultValue);
    }

    [Fact]
    public void WriteCsSummary_StringOverloads_ShouldProduceXmlSummary()
    {
        CsCodeGeneratorConfig cfg = new();
        bool ok = cfg.WriteCsSummary("line1\nline2", out string? summary);

        Assert.True(ok);
        Assert.NotNull(summary);
        Assert.Contains("/// <summary>", summary);
        Assert.Contains("line1<br/>", summary);
        Assert.Contains("line2<br/>", summary);
    }

    [Fact]
    public void WriteCsSummary_WhenNullAndPlaceholderDisabled_ShouldReturnFalse()
    {
        CsCodeGeneratorConfig cfg = new() { generatePlaceholderComments = false };

        bool ok = cfg.WriteCsSummary((string?)null, out string? summary);

        Assert.False(ok);
        Assert.Null(summary);
    }
}
