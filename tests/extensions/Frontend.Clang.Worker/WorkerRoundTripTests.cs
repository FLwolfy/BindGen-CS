using BGCS.Frontend.Clang;
using BGCS.Frontend.Clang.Workers;
using BGCS.Frontend.Clang.Parsing.Arguments;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Analysis.Interop;
using BGCS.Analysis.Declarations;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Infrastructure.Workers;
using BGCS.Infrastructure.IO;
using BGCS.Targets.Desktop.Targeting;
using BGCS.Targets.Desktop.Toolchains;
using Xunit;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using BGCS.Core.Artifacts;
using BGCS.Core.Common;

namespace BGCS.Worker.Tests;

public sealed class WorkerRoundTripTests : WorkerTestHost
{
    [Fact]
    public async Task StandardPairDependentOverloadsRoundTripWithoutReparentingBorrowedTemplateArguments()
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-worker-template-borrow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "collection.hpp");
            await File.WriteAllTextAsync(header, "#include <utility>\ntemplate<class T> struct Collection { using iterator=T*; std::pair<iterator,bool> insert(); std::pair<iterator,bool> insert(T); };\n");
            NativeTargetDescriptor target = ToolchainResolver.ResolveToolchain(new TargetResolver().Resolve(new(new("host"))), SourceLanguage.Cpp);
            FrontendResult parsed = await CreateClient().ParseAsync(new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                parseMacros = false,
                headerSearchPaths = ToolchainResolver.ResolveHeaderSearchPaths(target, SourceLanguage.Cpp).ToImmutableArray()
            }, CancellationToken.None);
            Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.NotNull(parsed.parserBuiltinInputs);
            Assert.NotEmpty(parsed.parserBuiltinInputs.files);
            var collection = Assert.Single(parsed.module.declarations, declaration => declaration.name == "Collection");
            Assert.Single(parsed.module.declarations, declaration => declaration.name == "iterator" && declaration.parent == collection.id);
            Assert.Equal(2, parsed.module.declarations.Count(declaration => declaration.name == "insert" && declaration.parent == collection.id));
            Assert.Empty(new NativeModuleVerifier().Validate(parsed.module));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DeepWorkerDeployment_LoadsItsBundledParserAndCompanionBeyondMaxPath()
    {
        if (!OperatingSystem.IsWindows()) return;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-long-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string sourceRoot = Path.GetDirectoryName(CreateClientPath())!;
            string deployment = temporary;
            for (int index = 0; deployment.Length < 320; index++) deployment = Path.Combine(deployment, "worker-部署-" + index + "-" + new string('a', 18));
            Directory.CreateDirectory(deployment);
            foreach (string source in Directory.GetFiles(sourceRoot)) File.Copy(source, Path.Combine(deployment, Path.GetFileName(source)));
            string nativeRoot = Path.Combine(sourceRoot, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            string nativeDestination = Path.Combine(deployment, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            Directory.CreateDirectory(nativeDestination);
            foreach (string source in Directory.GetFiles(nativeRoot)) File.Copy(source, Path.Combine(nativeDestination, Path.GetFileName(source)));
            Assert.True(Path.Combine(nativeDestination, "libclang.dll").Length > 320);
            Assert.True(File.Exists(Path.Combine(nativeDestination, "libClangSharp.dll")));
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "int native_add(int left, int right);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendWorkerClient client = new(Path.Combine(deployment, "BGCS.Frontend.Clang.Worker.dll"), host, TimeSpan.FromSeconds(60));
            FrontendResult parsed = await client.ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.DoesNotContain(parsed.module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Contains(parsed.module.declarations, declaration => declaration.name == "native_add" && declaration.kind == "Function");
        }
        finally { await DeleteWorkerDeploymentAsync(temporary); }
    }

    [Fact]
    public async Task EntrySelection_RetainsTransitiveStorageWithoutPromotingDependencyExports()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-entry-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string dependency = Path.Combine(temporary, "dependency.h");
            string header = Path.Combine(temporary, "entry.h");
            await File.WriteAllTextAsync(dependency, "struct Item { int value; }; struct Unrelated { float value; }; int dependency_only(void);");
            await File.WriteAllTextAsync(header, "#include \"dependency.h\"\nint read_item(const struct Item* item);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            var parsed = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            var selection = new DeclarationSelector(host.paths.comparer).SelectEntries(parsed.module, [header], temporary);
            Assert.Equal("read_item", selection.module.GetDeclaration(Assert.Single(selection.callables)).name);
            Assert.Contains(selection.module.declarations, declaration => declaration.name == "Item");
            Assert.Contains(selection.module.declarations, declaration => declaration.name == "value");
            Assert.DoesNotContain(selection.module.declarations, declaration => declaration.name is "dependency_only" or "Unrelated");
            Assert.Empty(new NativeModuleVerifier().Validate(selection.module));
            var abi = new CDeclarationAnalyzer().Analyze(selection.module, "example", selection.callables);
            Assert.Equal("read_item", Assert.Single(abi.functions).exportName);
            Assert.Contains(abi.types, type => type.declarationName == "Item" && type.fields.Length == 1);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task InternalCFunctions_AreNotInventedAsExternalLinkerExports()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-internal-linkage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "static int local(int value) { return value; } int external(int value);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            var parsed = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.All(parsed.module.declarations.Where(declaration => declaration.kind == "Function"), declaration => Assert.Equal("C", declaration.linkage));
            var local = Assert.Single(parsed.module.declarations, declaration => declaration.name == "local");
            Assert.True(local.isStatic);
            var abi = new CDeclarationAnalyzer().Analyze(parsed.module, "example");
            Assert.Equal("external", Assert.Single(abi.functions).exportName);
            Assert.Contains(abi.diagnostics, diagnostic => diagnostic.code == "BGCS-C-INTERNAL-LINKAGE" && diagnostic.severity.ToString() == "Warning");
            var explicitSelection = new CDeclarationAnalyzer().Analyze(parsed.module, "example", [local.id]);
            Assert.Contains(explicitSelection.diagnostics, diagnostic => diagnostic.code == "BGCS-C-INTERNAL-LINKAGE" && diagnostic.severity.ToString() == "Error");
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("--target=x86_64-unknown-linux-gnu")]
    [InlineData("-target")]
    [InlineData("-m32")]
    [InlineData("-mbig-endian")]
    [InlineData("@hidden-compiler-options.rsp")]
    public async Task ParserArguments_CannotReplaceTheResolvedNativeAbi(string argument)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-argument-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "int value(void);");
            if (argument.StartsWith('@'))
                await File.WriteAllTextAsync(Path.Combine(temporary, argument[1..]), "--target=x86_64-unknown-linux-gnu");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            var failure = await Assert.ThrowsAsync<System.IO.InvalidDataException>(async () => await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header])
            { pathBase = temporary, parseMacros = false, arguments = [argument] }, CancellationToken.None));
            // Preparation expands and snapshots response inputs before checking the
            // resolved ABI. A response file cannot smuggle an alternative target.
            Assert.Contains("resolved CPU", failure.Message);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallableFacts_PreserveUnspecifiedPrototypesAndBlockObjectAbi(bool block)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-callable-abi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, block ? "int invoke(int (^callback)(int));" : "typedef int (*Callback)(); int invoke(Callback callback);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            var result = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header])
            { pathBase = temporary, parseMacros = false, arguments = block ? ["-fblocks"] : [] }, CancellationToken.None);
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            if (block)
            {
                Assert.Single(result.module.types, type => type.category == BGCS.Core.Native.NativeTypeCategory.Block);
                var abi = new CDeclarationAnalyzer().Analyze(result.module, "block");
                Assert.Contains(abi.diagnostics, diagnostic => diagnostic.severity.ToString() == "Error" && diagnostic.message.Contains("not ordinary C function pointers", StringComparison.Ordinal));
            }
            else
            {
                var callback = Assert.Single(result.module.types, type => type.category == BGCS.Core.Native.NativeTypeCategory.Function);
                Assert.False(callback.hasPrototype);
                Assert.Empty(callback.parameterTypes);
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task DeployedWorker_ExecutesUsingTheLockedApplicationClosure()
    {
        string source = Path.GetDirectoryName(CreateClientPath())!;
        string hostRid = RuntimeInformation.RuntimeIdentifier;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-deployed-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var files = Directory.EnumerateFiles(source).Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".deps.json", StringComparison.Ordinal) || path.EndsWith(".runtimeconfig.json", StringComparison.Ordinal))
                .Concat(Directory.EnumerateFiles(Path.Combine(source, "runtimes", hostRid, "native"), "*", SearchOption.AllDirectories));
            WorkerAssetManifest manifest = new(1, hostRid, ClangParserIdentity.C_IDENTITY, files.Select(path =>
            {
                using FileStream stream = File.OpenRead(path);
                return new ArtifactDescriptor(Path.GetRelativePath(source, path).Replace('\\', '/'), "application/octet-stream", new ContentDigest(Convert.ToHexStringLower(SHA256.HashData(stream))));
            }).ToImmutableArray());
            string deployed = Path.Combine(temporary, "application", "bgcs", "worker");
            new WorkerAssetDeployer(host.paths).Deploy(source, deployed, manifest, hostRid);
            string entry = WorkerAssetLocator.Locate(host, applicationBase: Path.Combine(temporary, "application"));
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "int deployed(int value);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            var result = await new FrontendWorkerClient(entry, host).ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.Contains(result.module.declarations, declaration => declaration.name == "deployed");
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
        }
        finally { await DeleteWorkerDeploymentAsync(temporary); }
    }

    private static async Task DeleteWorkerDeploymentAsync(string directory)
    {
        string resolved = Path.GetFullPath(directory);
        string temporaryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
            || !(Path.GetFileName(resolved).StartsWith("bgcs-deployed-worker-", StringComparison.Ordinal)
                || Path.GetFileName(resolved).StartsWith("bgcs-long-worker-", StringComparison.Ordinal)))
            throw new InvalidOperationException("Worker cleanup must remain inside its owned temporary directory.");
        // Allow a transient Windows image mapping to clear after the worker exits.
        // Bound the retry; a persistent deployment lock must still fail the test.
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                Directory.Delete(resolved, recursive: true);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }

    [Fact]
    public async Task ReturnedCallback_UsesItsOwnFunctionArgumentTypes()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-returned-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "float (*factory(int seed))(double value);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            var callback = Assert.Single(result.module.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Function));
            Assert.Equal("Double", result.module.GetType(Assert.Single(callback.parameterTypes)).primitiveKind);
            Assert.Equal("Float", result.module.GetType(callback.returnType!.Value).primitiveKind);
            var factory = Assert.Single(result.module.declarations.Where(declaration => declaration.name == "factory"));
            var seed = Assert.Single(factory.children.Select(result.module.GetDeclaration));
            Assert.Equal("Int", result.module.GetType(seed.type!.Value).primitiveKind);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task CallableFacts_DistinguishVariadicCallbacksAndUnspecifiedCArguments()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-prototype-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "typedef int (*Logger)(int category, ...); void set_logger(Logger callback); int unspecified(); int exact(void);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            Assert.True(Assert.Single(result.module.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Function)).isVariadic);
            Assert.False(Assert.Single(result.module.declarations.Where(declaration => declaration.name == "unspecified")).hasPrototype);
            Assert.True(Assert.Single(result.module.declarations.Where(declaration => declaration.name == "exact")).hasPrototype);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task NativeStorageFacts_PreserveArrayAliasAndPointerAlignment()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "typedef int Values[3]; struct Storage { Values items; int* address; int tail[]; }; void read(const struct Storage* storage);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            var array = Assert.Single(result.module.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Array && type.arrayLength == 3));
            Assert.Equal(12, array.size);
            Assert.Equal(4, array.alignment);
            var alias = Assert.Single(result.module.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Alias && type.spelling == "Values"));
            Assert.Equal(4, alias.alignment);
            Assert.All(result.module.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Pointer), type => Assert.Equal(result.module.pointerSize, type.alignment));
            var flexible = Assert.Single(result.module.types.Where(type => type.category == BGCS.Core.Native.NativeTypeCategory.Array && type.arrayLength is null));
            Assert.Null(flexible.size);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task UnsignedEnumEvaluation_PreservesAllSixtyFourBits()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-enum-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.hpp");
            await File.WriteAllTextAsync(header, "enum class Flags : unsigned long long { All = ~0ull, High = 1ull << 63 }; enum class Signed : long long { MinusOne = -1 };");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.Cpp, [header]) { pathBase = temporary, parseMacros = false }, CancellationToken.None);
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            Assert.Equal("18446744073709551615", Assert.Single(result.module.declarations.Where(declaration => declaration.name == "All")).evaluatedIntegralValue);
            Assert.Equal("9223372036854775808", Assert.Single(result.module.declarations.Where(declaration => declaration.name == "High")).evaluatedIntegralValue);
            Assert.Equal("-1", Assert.Single(result.module.declarations.Where(declaration => declaration.name == "MinusOne")).evaluatedIntegralValue);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task RealWorker_ReturnsDetachedTypesQualifiersOffsetsAndFunctions()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header,
                "struct Item { char prefix; int value; }; typedef const int* ReadOnlyInt; int read_item(const struct Item* item);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendRequest request = new(target, SourceLanguage.C, [header])
            {
                pathBase = temporary,
                parseMacros = false
            };
            var result = await CreateClient().ParseAsync(request, CancellationToken.None);
            var module = result.module;
            Assert.Contains(result.snapshot.files, dependency => dependency.path == header);
            Assert.True(SourceSnapshotVerifier.Matches(result.snapshot, host.paths));
            Assert.Empty(new NativeModuleVerifier().Validate(module));
            Assert.Empty(module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            var record = Assert.Single(module.declarations.Where(declaration => declaration.name == "Item"));
            var value = Assert.Single(module.declarations.Where(declaration => declaration.name == "value"));
            Assert.Equal(4, value.offset);
            Assert.Equal(record.id, value.parent);
            Assert.Contains(module.types, type => type.qualifier == "Const");
            Assert.Contains(module.declarations, declaration => declaration.name == "read_item");
            Assert.All(module.declarations, declaration => Assert.False(Path.IsPathRooted(declaration.source.file)));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public async Task PreCanceledRequest_DoesNotStartAWorker()
    {
        NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await CreateClient().ParseAsync(new(target, SourceLanguage.C, []), cancellation.Token));
    }

    private FrontendWorkerClient CreateClient() => new(CreateClientPath(), host, TimeSpan.FromSeconds(30));

    private static string CreateClientPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new InvalidOperationException("The test repository is unavailable.");
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "Release" : "Debug";
        string worker = Path.Combine(root, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0", "BGCS.Frontend.Clang.Worker.dll");
        return worker;
    }

    [Fact]
    public async Task CppFacts_PreserveRValueReferencesAndTypedTemplateArguments()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-cpp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.hpp");
            await File.WriteAllTextAsync(header, "template<typename T, int N> struct Box { T value; }; using IntBox = Box<int, 4>; int consume(int&& value);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = temporary,
                languageStandard = "c++23",
                parseMacros = false
            }, CancellationToken.None);
            Assert.Empty(result.module.diagnostics.Where(diagnostic => diagnostic.severity.ToString() == "Error"));
            Assert.Empty(new NativeModuleVerifier().Validate(result.module));
            Assert.Contains(result.module.types, type => type.isRValueReference);
            Assert.Contains(result.module.declarations, declaration => declaration.templateParameters.Length == 2);
            Assert.Contains(result.module.declarations, declaration => declaration.templateArguments.Any(argument => argument.type is not null));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task NestedRecordFirstReferencedAsAField_RetainsItsOwningDeclaration()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-nested-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.hpp");
            await File.WriteAllTextAsync(header, "struct Outer { struct Inner { int value; }; Inner member; };");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.Cpp, [header])
            {
                pathBase = temporary,
                languageStandard = "c++23",
                parseMacros = false
            }, CancellationToken.None);
            var outer = Assert.Single(result.module.declarations.Where(declaration => declaration.name == "Outer"));
            var inner = Assert.Single(result.module.declarations.Where(declaration => declaration.name == "Inner"));
            Assert.Equal(outer.id, inner.parent);
            Assert.Empty(new NativeModuleVerifier().Validate(result.module));
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Theory]
    [InlineData("-fsigned-char", true)]
    [InlineData("-funsigned-char", false)]
    public async Task PlainCharSignedness_ComesFromCompilerFactsRatherThanHostAssumptions(
        string flag,
        bool signed
    ) {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-char-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "struct Value { char code; }; char echo(char value);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header])
            {
                pathBase = temporary,
                parseMacros = false,
                arguments = [flag]
            }, CancellationToken.None);
            var type = Assert.Single(result.module.types.Where(type => type.primitiveKind == "Char"));
            Assert.Equal(signed, type.isSigned);
            Assert.Equal(1, type.alignment);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public async Task CombinedQualifiers_AreNotDiscardedAtEitherPointerLevel()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-worker-qualifiers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string header = Path.Combine(temporary, "native.h");
            await File.WriteAllTextAsync(header, "void inspect(const volatile int* const* values);");
            NativeTargetDescriptor target = new TargetResolver().Resolve(new(new NativeTargetId("host")));
            FrontendResult result = await CreateClient().ParseAsync(new(target, SourceLanguage.C, [header])
            {
                pathBase = temporary,
                parseMacros = false
            }, CancellationToken.None);
            var parameter = Assert.Single(result.module.declarations.Where(declaration => declaration.name == "values"));
            var outer = result.module.GetType(parameter.type!.Value);
            var pointerConst = result.module.GetType(outer.elementType!.Value);
            var inner = result.module.GetType(pointerConst.elementType!.Value);
            var valueConst = result.module.GetType(inner.elementType!.Value);
            var valueVolatile = result.module.GetType(valueConst.elementType!.Value);
            Assert.Equal("Const", pointerConst.qualifier);
            Assert.Equal("Const", valueConst.qualifier);
            Assert.Equal("Volatile", valueVolatile.qualifier);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
