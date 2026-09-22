using System;
using System.Collections.Generic;
using BGCS.Conversion;
using BGCS.Core;
using BGCS.Core.Mapping;
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
        cfg.EnumMappings.Add(new EnumMapping("EType", "ETypeFriendly", null));
        cfg.FunctionMappings.Add(new FunctionMapping("DoThing", "DoThingFriendly", null, [], []));
        cfg.ClassMappings.Add(new TypeMapping("NativeStruct", "NativeStructFriendly", null));
        cfg.HandleMappings.Add(new HandleMapping("NativeHandle", "NativeHandleFriendly", null));
        cfg.DelegateMappings.Add(new DelegateMapping("OnValue", "void", "int"));

        Assert.True(cfg.TryGetEnumMapping("EType", out var enumMapping));
        Assert.Equal("ETypeFriendly", enumMapping!.FriendlyName);
        Assert.True(cfg.TryGetFunctionMapping("DoThing", out var fnMapping));
        Assert.Equal("DoThingFriendly", fnMapping!.FriendlyName);
        Assert.True(cfg.TryGetTypeMapping("NativeStruct", out var typeMapping));
        Assert.Equal("NativeStructFriendly", typeMapping!.FriendlyName);
        Assert.True(cfg.TryGetHandleMapping("NativeHandle", out var handleMapping));
        Assert.Equal("NativeHandleFriendly", handleMapping!.FriendlyName);
        Assert.True(cfg.TryGetDelegateMapping("OnValue", out var delegateMapping));
        Assert.Equal("int", delegateMapping!.Signature);
    }

    [Fact]
    public void ArrayMappingHelper_ShouldMatchPrimitiveAndSize()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.ArrayMappings.Add(new ArrayMapping(CppPrimitiveKind.Float, 4, "Vector4"));

        CppArrayType arrayType = new(default, CppPrimitiveType.Float, 4);

        Assert.True(cfg.TryGetArrayMapping(arrayType, out string? mapping));
        Assert.Equal("Vector4", mapping);
    }

    [Fact]
    public void TypeConverter_ShouldMapExtendedWindowsPrimitiveTypes()
    {
        CsCodeGeneratorConfig config = new();

        Assert.Equal("int", config.TypeConverter.Convert(CppPrimitiveType.Long, CsTypeStyle.Raw));
        Assert.Equal("char", config.TypeConverter.Convert(CppPrimitiveType.WChar, CsTypeStyle.Raw));
        Assert.Equal("Int128", config.TypeConverter.Convert(CppPrimitiveType.Int128, CsTypeStyle.Raw));
        Assert.Equal("UInt128", config.TypeConverter.Convert(CppPrimitiveType.UInt128, CsTypeStyle.Raw));
        Assert.Equal("Half", config.TypeConverter.Convert(CppPrimitiveType.Float16, CsTypeStyle.Raw));
    }

    [Fact]
    public void TypeConverter_ShouldUseFriendlyMappingsForDeclaredTypes()
    {
        CsCodeGeneratorConfig config = new();
        CppEnum nativeMode = new(default, "NATIVE_MODE") { IntegerType = CppPrimitiveType.Int };
        CppClass nativeContext = new(default, "native_context");
        config.EnumMappings.Add(new EnumMapping("NATIVE_MODE", "NativeMode", null));
        config.ClassMappings.Add(new TypeMapping("native_context", "NativeContext", null));

        Assert.Equal("NativeMode", config.TypeConverter.Convert(nativeMode, CsTypeStyle.Raw));
        Assert.Equal("NativeContext", config.TypeConverter.Convert(nativeContext, CsTypeStyle.Raw));

        config.TypeMappings["NATIVE_MODE"] = "ExternalMode";
        config.TypeMappings["native_context"] = "ExternalContext";
        Assert.Equal("ExternalMode", config.TypeConverter.Convert(nativeMode, CsTypeStyle.Raw));
        Assert.Equal("ExternalContext", config.TypeConverter.Convert(nativeContext, CsTypeStyle.Raw));
    }

    [Fact]
    public void TypeConverter_ShouldRequireExplicitMappingsForUnexposedAndGenericTypes()
    {
        CsCodeGeneratorConfig config = new();
        CppUnexposedType opaque = new(default, "NativeOpaque");
        CppGenericType generic = new(default, new CppUnexposedType(default, "Vector"));
        generic.GenericArguments.Add(CppPrimitiveType.Int);

        Assert.Throws<UnexposedTypeException>(() => config.TypeConverter.Convert(opaque, CsTypeStyle.Raw));
        Assert.Throws<NotSupportedException>(() => config.TypeConverter.Convert(generic, CsTypeStyle.Raw));

        config.TypeMappings["NativeOpaque"] = "nint";
        config.TypeMappings["Vector<int>"] = "NativeIntVector";
        Assert.Equal("nint", config.TypeConverter.Convert(opaque, CsTypeStyle.Raw));
        Assert.Equal("NativeIntVector", config.TypeConverter.Convert(generic, CsTypeStyle.Raw));
    }

    [Fact]
    public void FunctionAliasMappings_ShouldAddAndResolveAlias()
    {
        CsCodeGeneratorConfig cfg = new();
        FunctionAliasMapping alias = new("glBindTexture", "glBindTextureEXT", "BindTextureExt", null);
        cfg.AddFunctionAliasMapping(alias);

        Assert.True(cfg.TryGetFunctionAliasMapping("glBindTexture", "glBindTextureEXT", out var resolved));
        Assert.Equal("BindTextureExt", resolved!.FriendlyName);
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
        cfg.ParameterNamingConvention = NamingConvention.CamelCase;

        string normalized = cfg.NormalizeParameterName("9_VALUE");

        Assert.Equal("_9Value", normalized);
        Assert.Equal("value", cfg.NormalizeParameterName(string.Empty));
    }

    [Fact]
    public void NormalizeValue_ShouldHandleKnownNativePatterns()
    {
        CsCodeGeneratorConfig cfg = new();

        Assert.Equal("default", cfg.NormalizeValue("NULL", sanitize: false));
        Assert.Equal("float.MaxValue", cfg.NormalizeValue("FLT_MAX", sanitize: false));
        Assert.Equal("1", cfg.NormalizeValue("true", sanitize: false));
        Assert.Equal("new Vector2(1,2)", cfg.NormalizeValue("ImVec2(1,2)", sanitize: false));
        Assert.Null(cfg.NormalizeValue("ImVec2(1,2)", sanitize: true));
    }

    [Fact]
    public void GetConstantName_ShouldPreferKnownMapping()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.KnownConstantNames["GL_TRIANGLES"] = "Triangles";

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
        cfg.IgnoredParts.Add("Gl");
        cfg.FunctionMappings.Add(new FunctionMapping("vkDoThing", "DoThingFriendly", null, [], []));

        Assert.Equal("DoThingFriendly", cfg.GetCsFunctionName("vkDoThing"));
        Assert.Equal("CreateBuffer", cfg.GetCsFunctionName("glCreateBuffer"));
    }

    [Fact]
    public void GetCsFunctionName_ShouldPreferMappingThenStripLongestExactPrefix()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.FunctionPrefixes.Add("Im");
        cfg.FunctionPrefixes.Add("ImGuizmo_");
        cfg.FunctionMappings.Add(new FunctionMapping("ImGuizmo_BeginFrame", "StartFrame", null, [], []));

        Assert.Equal("SetRect", cfg.GetCsFunctionName("ImGuizmo_SetRect"));
        Assert.Equal("StartFrame", cfg.GetCsFunctionName("ImGuizmo_BeginFrame"));
        Assert.Equal("ImGuizmoSetRect", cfg.GetCsFunctionName("imGuizmoSetRect"));

        cfg.FunctionNamingConvention = NamingConvention.Unknown;
        Assert.Equal("SetRect", cfg.GetCsFunctionName("ImGuizmo_SetRect"));
    }

    [Fact]
    public void GetBoolType_ShouldRespectConfiguredBoolMode()
    {
        CsCodeGeneratorConfig cfg = new();
        Assert.Equal(BoolType.Bool8, cfg.BoolType);
        Assert.Equal("Bool8", cfg.GetBoolType());
        Assert.Equal("Bool8", cfg.GetBoolType(ptr: true));

        cfg.BoolType = BoolType.Bool8;
        Assert.Equal("Bool8", cfg.GetBoolType());
        Assert.Equal("Bool8", cfg.GetBoolType(ptr: true));

        cfg.BoolType = BoolType.Bool32;
        Assert.Equal("Bool32", cfg.GetBoolType());
        Assert.Equal("Bool32", cfg.GetBoolType(ptr: true));

        cfg.BoolType = BoolType.Byte;
        Assert.Equal("byte", cfg.GetBoolType());
        Assert.Equal("byte", cfg.GetBoolType(ptr: true));

        cfg.BoolType = BoolType.Int32;
        Assert.Equal("int", cfg.GetBoolType());
        Assert.Equal("int", cfg.GetBoolType(ptr: true));
    }

    [Fact]
    public void DelegatePointerType_ShouldUseConfiguredNativeBoolRepresentation()
    {
        CsCodeGeneratorConfig cfg = new()
        {
            BoolType = BoolType.Byte,
            DelegatesAsVoidPointer = false
        };
        CppFunctionType callbackType = new(default, CppPrimitiveType.Bool)
        {
            CallingConvention = CppCallingConvention.C
        };
        callbackType.Parameters.Add(new CppParameter(default, CppPrimitiveType.Bool, "enabled"));

        string pointerType = cfg.GetDelegatePointerType(callbackType, withConvention: true);

        Assert.Contains("<byte, byte>", pointerType);
        Assert.DoesNotContain("bool", pointerType, StringComparison.Ordinal);
    }

    [Fact]
    public void DelegatePointerType_ShouldRespectDelegatesAsVoidPointerFlag()
    {
        CsCodeGeneratorConfig cfg = new() { BoolType = BoolType.Bool8 };
        CppFunctionType callbackType = new(default, CppPrimitiveType.Void);
        callbackType.Parameters.Add(new CppParameter(default, CppPrimitiveType.Int, "value"));

        cfg.DelegatesAsVoidPointer = false;
        string pointerType = cfg.GetDelegatePointerType(callbackType);
        Assert.Contains("delegate*<", pointerType);
        Assert.Contains("int", pointerType);

        cfg.DelegatesAsVoidPointer = true;
        Assert.Equal("void*", cfg.GetDelegatePointerType(callbackType));
    }

    [Fact]
    public void ParameterSignatureHelpers_ShouldHandleCompatibilityAndBoolMapping()
    {
        CsCodeGeneratorConfig cfg = new() { BoolType = BoolType.Bool8 };
        List<CppParameter> parameters =
        [
            new CppParameter(default, CppPrimitiveType.Bool, "enabled"),
            new CppParameter(default, new CppPointerType(default, CppPrimitiveType.Int), "values")
        ];

        string signature = cfg.GetParameterSignature(parameters, canUseOut: false);
        string namelessCompat = cfg.GetNamelessParameterSignature(parameters, canUseOut: false, compatibility: true);
        string marshallingCompat = cfg.WriteFunctionMarshalling(parameters, compatibility: true);

        Assert.Contains("Bool8 enabled", signature);
        Assert.Contains("int* values", signature);
        Assert.Equal("Bool8, nint", namelessCompat);
        Assert.Equal("enabled, (nint)values", marshallingCompat);
    }

    [Fact]
    public void TryGetDefaultValue_ShouldResolveMappedDefaults()
    {
        CsCodeGeneratorConfig cfg = new();
        cfg.FunctionMappings.Add(new FunctionMapping("set_mode", "SetMode", null, new Dictionary<string, string> { ["mode"] = "MY_MODE_FAST" }, []));
        cfg.KnownEnumPrefixes["MyMode"] = "MY_MODE";

        CppTypedef typedef = new(default, "MyMode", CppPrimitiveType.Int);
        CppParameter parameter = new(default, typedef, "mode");

        bool found = cfg.TryGetDefaultValue("set_mode", parameter, sanitize: false, out string? defaultValue);

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
        CsCodeGeneratorConfig cfg = new() { GeneratePlaceholderComments = false };

        bool ok = cfg.WriteCsSummary((string?)null, out string? summary);

        Assert.False(ok);
        Assert.Null(summary);
    }
}
