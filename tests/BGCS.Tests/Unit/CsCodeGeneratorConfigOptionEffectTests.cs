using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BGCS.Core;
using BGCS.Core.Logging;
using BGCS.Core.Mapping;
using BGCS.CppAst.Model.Types;
using BGCS.Metadata;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BGCS.Tests;

public class CsCodeGeneratorConfigOptionEffectTests
{
    [Fact]
    public void SerializableOptions_ShouldRoundTripWithExpectedBehavior()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "bgcs-cfg-explicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string configPath = Path.Combine(tempDir, "config.json");
        File.WriteAllText(Path.Combine(tempDir, "base.json"), "{}");

        string oldCwd = Environment.CurrentDirectory;
        Environment.CurrentDirectory = tempDir;

        try
        {
            var cfg = new CsCodeGeneratorConfig
            {
                BaseConfig = new BaseConfig { Url = "file://base.json", IgnoredProperties = ["IncludeFolders"] },
                Namespace = "Ns.Value",
                ApiName = "Api.Value",
                LibName = "Lib.Value",
                LogLevel = LogSeverity.Debug,
                CppLogLevel = LogSeverity.Information,
                EnableExperimentalOptions = true,
                GenerateSizeOfStructs = true,
                GenerateConstructorsForStructs = false,
                DelegatesAsVoidPointer = false,
                WrapPointersAsHandle = true,
                GeneratePlaceholderComments = false,
                UseCustomContext = true,
                GetLibraryNameFunctionName = "GetLibNameX",
                GetLibraryExtensionFunctionName = "GetLibExtX",
                ImportType = ImportType.LibraryImport,
                GenerateMetadata = true,
                GenerateConstants = false,
                GenerateEnums = false,
                GenerateExtensions = false,
                GenerateFunctions = false,
                GenerateHandles = false,
                GenerateTypes = false,
                GenerateDelegates = false,
                OneFilePerType = false,
                MergeGeneratedFilesToSingleFile = true,
                RuntimeNamespace = "Runtime.X",
                GenerateRuntimeSource = true,
                BoolType = BoolType.Bool32,
                ConstantNamingConvention = NamingConvention.CamelCase,
                EnumNamingConvention = NamingConvention.SnakeCase,
                EnumItemNamingConvention = NamingConvention.CamelCase,
                ExtensionNamingConvention = NamingConvention.SnakeCase,
                FunctionNamingConvention = NamingConvention.CamelCase,
                HandleNamingConvention = NamingConvention.SnakeCase,
                TypeNamingConvention = NamingConvention.CamelCase,
                DelegateNamingConvention = NamingConvention.SnakeCase,
                ParameterNamingConvention = NamingConvention.SnakeCase,
                MemberNamingConvention = NamingConvention.CamelCase,
                AutoSquashTypedef = false,
                GenerateAdditionalOverloads = true
            };

            cfg.FunctionTableEntries = [new CsFunctionTableEntry(7, "entry_x")];
            cfg.KnownConstantNames = new Dictionary<string, string> { ["CONST_X"] = "ConstX" };
            cfg.KnownEnumValueNames = new Dictionary<string, string> { ["ENUM_VAL_X"] = "EnumValX" };
            cfg.KnownEnumPrefixes = new Dictionary<string, string> { ["EnumX"] = "ENUM_X" };
            cfg.KnownExtensionPrefixes = new Dictionary<string, string> { ["ExtX"] = "EXT_X" };
            cfg.KnownExtensionNames = new Dictionary<string, string> { ["EXT_NAME_X"] = "ExtNameX" };
            cfg.KnownDefaultValueNames = new Dictionary<string, string> { ["DEFAULT_X"] = "42" };
            cfg.KnownConstructors = new Dictionary<string, List<string>> { ["TypeX"] = ["CreateX"] };
            cfg.KnownMemberFunctions = new Dictionary<string, List<string>> { ["TypeX"] = ["MethodX"] };
            cfg.IgnoredParts = ["IGN_PART"];
            cfg.Keywords = ["kw_x"];
            cfg.IgnoredFunctions = ["fn_ignore_x"];
            cfg.IgnoredExtensions = ["ext_ignore_x"];
            cfg.IgnoredTypes = ["type_ignore_x"];
            cfg.IgnoredEnums = ["enum_ignore_x"];
            cfg.IgnoredTypedefs = ["typedef_ignore_x"];
            cfg.IgnoredDelegates = ["delegate_ignore_x"];
            cfg.IgnoredConstants = ["const_ignore_x"];
            cfg.AllowedFunctions = ["fn_allow_x"];
            cfg.AllowedExtensions = ["ext_allow_x"];
            cfg.AllowedTypes = ["type_allow_x"];
            cfg.AllowedEnums = ["enum_allow_x"];
            cfg.AllowedTypedefs = ["typedef_allow_x"];
            cfg.AllowedDelegates = ["delegate_allow_x"];
            cfg.AllowedConstants = ["const_allow_x"];
            cfg.Usings = ["System.Text"];
            cfg.IncludeFolders = ["inc_x"];
            cfg.SystemIncludeFolders = ["sys_inc_x"];
            cfg.Defines = ["DEF_X=1"];
            cfg.AdditionalArguments = ["-std=c23"];
            cfg.CustomEnums =
            [
                new CsEnumMetadata("CPP_ENUM_X", "EnumX", [], "enum-comment", "int",
                    [new CsEnumItemMetadata("CPP_ITEM_X", "1", "ItemX", "1", [], "item-comment")])
            ];
            cfg.VaryingTypes = ["Span<int>"];
            cfg.ConstantMappings = [new ConstantMapping("CONST_OLD_X", "CONST_NEW_X", "cmt", "int", "7")];
            cfg.EnumMappings = [new EnumMapping("ENUM_OLD_X", "EnumNewX", "enum-cmt")];
            cfg.FunctionMappings = [new FunctionMapping("FnOldX", "FnNewX", "fn-cmt", [], [])];
            cfg.HandleMappings = [new HandleMapping("HandleOldX", "HandleNewX", "handle-cmt")];
            cfg.ClassMappings = [new TypeMapping("TypeOldX", "TypeNewX", "type-cmt")];
            cfg.DelegateMappings = [new DelegateMapping("DelegateX", "void", "int value")];
            cfg.ArrayMappings = [new ArrayMapping(CppPrimitiveKind.Int, 4, "Int4X")];
            cfg.NameMappings = new Dictionary<string, string> { ["OLD_NAME_X"] = "NewNameX" };
            cfg.TypeMappings = new Dictionary<string, string> { ["old_type_x"] = "NewTypeX" };
            cfg.TypedefToEnumMappings = new Dictionary<string, string?> { ["TypedefEnumX"] = "EnumNewX" };
            cfg.FunctionAliasMappings =
                new Dictionary<string, List<FunctionAliasMapping>>
                {
                    ["FnOldX"] = [new FunctionAliasMapping("FnOldX", "FnAliasX", "FnAliasFriendlyX", "alias-cmt")]
                };

            cfg.Save(configPath);
            CsCodeGeneratorConfig loaded = CsCodeGeneratorConfig.Load(configPath, NoopComposer.Instance);

            Assert.NotNull(loaded.BaseConfig);
            Assert.Equal("file://base.json", loaded.BaseConfig!.Url);
            Assert.Contains("IncludeFolders", loaded.BaseConfig.IgnoredProperties);
            Assert.Equal("Ns.Value", loaded.Namespace);
            Assert.Equal("Api.Value", loaded.ApiName);
            Assert.Equal("Lib.Value", loaded.LibName);
            Assert.Equal(LogSeverity.Debug, loaded.LogLevel);
            Assert.Equal(LogSeverity.Information, loaded.CppLogLevel);
            Assert.True(loaded.EnableExperimentalOptions);
            Assert.True(loaded.GenerateSizeOfStructs);
            Assert.False(loaded.GenerateConstructorsForStructs);
            Assert.False(loaded.DelegatesAsVoidPointer);
            Assert.True(loaded.WrapPointersAsHandle);
            Assert.False(loaded.GeneratePlaceholderComments);
            Assert.True(loaded.UseCustomContext);
            Assert.Equal("GetLibNameX", loaded.GetLibraryNameFunctionName);
            Assert.Equal("GetLibExtX", loaded.GetLibraryExtensionFunctionName);
            Assert.Equal(ImportType.LibraryImport, loaded.ImportType);
            Assert.True(loaded.UseLibraryImport);
            Assert.False(loaded.UseFunctionTable);
            Assert.True(loaded.GenerateMetadata);
            Assert.False(loaded.GenerateConstants);
            Assert.False(loaded.GenerateEnums);
            Assert.False(loaded.GenerateExtensions);
            Assert.False(loaded.GenerateFunctions);
            Assert.False(loaded.GenerateHandles);
            Assert.False(loaded.GenerateTypes);
            Assert.False(loaded.GenerateDelegates);
            Assert.False(loaded.OneFilePerType);
            Assert.True(loaded.MergeGeneratedFilesToSingleFile);
            Assert.Equal("Runtime.X", loaded.RuntimeNamespace);
            Assert.True(loaded.GenerateRuntimeSource);
            Assert.Equal(BoolType.Bool32, loaded.BoolType);
            Assert.Equal("int", loaded.GetBoolType());
            Assert.Equal(NamingConvention.CamelCase, loaded.ConstantNamingConvention);
            Assert.Equal(NamingConvention.SnakeCase, loaded.EnumNamingConvention);
            Assert.Equal(NamingConvention.CamelCase, loaded.EnumItemNamingConvention);
            Assert.Equal(NamingConvention.SnakeCase, loaded.ExtensionNamingConvention);
            Assert.Equal(NamingConvention.CamelCase, loaded.FunctionNamingConvention);
            Assert.Equal(NamingConvention.SnakeCase, loaded.HandleNamingConvention);
            Assert.Equal(NamingConvention.CamelCase, loaded.TypeNamingConvention);
            Assert.Equal(NamingConvention.SnakeCase, loaded.DelegateNamingConvention);
            Assert.Equal(NamingConvention.SnakeCase, loaded.ParameterNamingConvention);
            Assert.Equal(NamingConvention.CamelCase, loaded.MemberNamingConvention);
            Assert.False(loaded.AutoSquashTypedef);
            Assert.True(loaded.GenerateAdditionalOverloads);

            Assert.Contains("entry_x", loaded.FunctionTableEntries.Select(x => x.EntryPoint));
            Assert.Equal("ConstX", loaded.GetConstantName("CONST_X"));
            Assert.Contains("ENUM_VAL_X", loaded.KnownEnumValueNames.Keys);
            Assert.Contains("EnumX", loaded.KnownEnumPrefixes.Keys);
            Assert.Contains("ExtX", loaded.KnownExtensionPrefixes.Keys);
            Assert.Contains("EXT_NAME_X", loaded.KnownExtensionNames.Keys);
            Assert.Contains("DEFAULT_X", loaded.KnownDefaultValueNames.Keys);
            Assert.Contains("TypeX", loaded.KnownConstructors.Keys);
            Assert.Contains("TypeX", loaded.KnownMemberFunctions.Keys);
            Assert.Contains("IGN_PART", loaded.IgnoredParts);
            Assert.Contains("kw_x", loaded.Keywords);
            Assert.Contains("fn_ignore_x", loaded.IgnoredFunctions);
            Assert.Contains("ext_ignore_x", loaded.IgnoredExtensions);
            Assert.Contains("type_ignore_x", loaded.IgnoredTypes);
            Assert.Contains("enum_ignore_x", loaded.IgnoredEnums);
            Assert.Contains("typedef_ignore_x", loaded.IgnoredTypedefs);
            Assert.Contains("delegate_ignore_x", loaded.IgnoredDelegates);
            Assert.Contains("const_ignore_x", loaded.IgnoredConstants);
            Assert.Contains("fn_allow_x", loaded.AllowedFunctions);
            Assert.Contains("ext_allow_x", loaded.AllowedExtensions);
            Assert.Contains("type_allow_x", loaded.AllowedTypes);
            Assert.Contains("enum_allow_x", loaded.AllowedEnums);
            Assert.Contains("typedef_allow_x", loaded.AllowedTypedefs);
            Assert.Contains("delegate_allow_x", loaded.AllowedDelegates);
            Assert.Contains("const_allow_x", loaded.AllowedConstants);
            Assert.Contains("System.Text", loaded.Usings);
            Assert.Contains("inc_x", loaded.IncludeFolders);
            Assert.Contains("sys_inc_x", loaded.SystemIncludeFolders);
            Assert.Contains("DEF_X=1", loaded.Defines);
            Assert.Contains("-std=c23", loaded.AdditionalArguments);
            Assert.Contains("Span<int>", loaded.VaryingTypes);
            Assert.Contains("old_type_x", loaded.TypeMappings.Keys);
            Assert.Equal("NewTypeX", loaded.TypeMappings["old_type_x"]);
            Assert.Equal("NewNameX", loaded.NameMappings["OLD_NAME_X"]);
            Assert.Equal("EnumNewX", loaded.TypedefToEnumMappings["TypedefEnumX"]);
            Assert.True(loaded.TryGetEnumMapping("ENUM_OLD_X", out _));
            Assert.True(loaded.TryGetFunctionMapping("FnOldX", out _));
            Assert.True(loaded.TryGetTypeMapping("TypeOldX", out _));
            Assert.True(loaded.TryGetHandleMapping("HandleOldX", out _));
            Assert.True(loaded.TryGetDelegateMapping("DelegateX", out _));
            Assert.True(loaded.TryGetFunctionAliasMapping("FnOldX", "FnAliasX", out _));
            Assert.Single(loaded.CustomEnums);
            Assert.Single(loaded.ConstantMappings);
            Assert.Single(loaded.EnumMappings);
            Assert.Single(loaded.FunctionMappings);
            Assert.Single(loaded.HandleMappings);
            Assert.Single(loaded.ClassMappings);
            Assert.Single(loaded.DelegateMappings);
            Assert.Single(loaded.ArrayMappings);
        }
        finally
        {
            Environment.CurrentDirectory = oldCwd;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void HeaderInjector_And_CustomEnumItemMapper_ShouldBeRuntimeOnlyAndNotSerialized()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), "bgcs-cfg-runtime-hooks-" + Guid.NewGuid().ToString("N") + ".json");
        int headerInjectorCalls = 0;
        int enumMapperCalls = 0;

        try
        {
            var cfg = new CsCodeGeneratorConfig
            {
                HeaderInjector = (_, _) => headerInjectorCalls++,
                CustomEnumItemMapper = (_, _, _, _) => enumMapperCalls++
            };

            cfg.HeaderInjector?.Invoke(new DummyCodeWriter(), new System.Text.StringBuilder());
            cfg.CustomEnumItemMapper?.Invoke(null!, null!, null!, null!);

            Assert.Equal(1, headerInjectorCalls);
            Assert.Equal(1, enumMapperCalls);

            cfg.Save(tempPath);
            string json = File.ReadAllText(tempPath);
            JObject root = JObject.Parse(json);
            Assert.Null(root[nameof(CsCodeGeneratorConfig.HeaderInjector)]);
            Assert.Null(root[nameof(CsCodeGeneratorConfig.CustomEnumItemMapper)]);

            CsCodeGeneratorConfig loaded = CsCodeGeneratorConfig.Load(tempPath, NoopComposer.Instance);
            Assert.Null(loaded.HeaderInjector);
            Assert.Null(loaded.CustomEnumItemMapper);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed class NoopComposer : IConfigComposer
    {
        public static readonly NoopComposer Instance = new();

        public void Compose(ref CsCodeGeneratorConfig config)
        {
        }
    }
}
