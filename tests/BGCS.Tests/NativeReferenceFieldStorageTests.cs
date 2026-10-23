using BGCS.Frontend.Clang.Workers;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.NativeBridge.Emission;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeReferenceFieldStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerReferenceSlotsRetainStrictGraphAndConsumerCompilerChecks(bool corruptStorage)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-reference-storage-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string header = Path.Combine(root, "native.hpp"), source = Path.Combine(root, "proof.cpp");
            await File.WriteAllTextAsync(header, """
                struct LargeReferent { char bytes[64]; };
                using LargeReference = LargeReferent&;
                struct References { LargeReference first; LargeReferent& second; int tail; };
                struct Variants { LargeReferent&& object; void (&function)(); void (&&rfunction)(); int tail; };
                """);
            var target = NativeConsumerHarness.ParserTarget(SourceLanguage.Cpp);
            NativeModule parsed = (await new FrontendWorkerClient(Path.Combine(WorkerRoot(), "BGCS.Frontend.Clang.Worker.dll"), TestHostPaths.CreateHost())
                .ParseAsync(new(target, SourceLanguage.Cpp, [header])
                { pathBase = root, languageStandard = "c++23", parseMacros = false }, CancellationToken.None)).module;
            Assert.DoesNotContain(parsed.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Empty(new NativeModuleVerifier().Validate(parsed));
            NativeDeclaration first = Assert.Single(parsed.declarations, declaration => declaration.qualifiedName == "References::first");
            Assert.Equal(64, parsed.GetType(first.type!.Value).size);
            Assert.InRange(first.storageSize!.Value, 1, 63);
            Assert.Equal(EvidenceLevel.Declared, first.storageEvidence!.level);
            NativeModule observed = corruptStorage ? Copy(parsed, first with { storageSize = first.storageSize + 1 }) : parsed;
            string proof = new NativeStorageAssertionEmitter().Emit(observed, [header], NativeAssertionConventions.For(observed));
            Assert.Contains("BGCS native reference field storage differs from parsed facts", proof);
            Assert.Contains("_type = decltype(((Variants*)nullptr)->function);", proof);
            Assert.Contains("_type = decltype(((Variants*)nullptr)->rfunction);", proof);
            Assert.Contains("is_lvalue_reference<decltype(((References*)nullptr)->first)>", proof);
            Assert.Contains("is_rvalue_reference<decltype(((Variants*)nullptr)->object)>", proof);
            Assert.Contains("decltype(((References*)nullptr)->tail)", proof);
            await File.WriteAllTextAsync(source, proof);
            string compiler = NativeConsumerHarness.FindCompiler(true);
            bool msvc = Path.GetFileName(compiler).Equals("cl.exe", StringComparison.OrdinalIgnoreCase);
            ImmutableArray<string> arguments = msvc
                ? ["/nologo", "/Zs", "/TP", "/std:c++latest", source, .. target.toolchain.systemIncludeFolders.Select(directory => "/I" + directory)]
                : ["-std=c++23", "-fsyntax-only", source, .. NativeConsumerHarness.CompilerSdkArguments()];
            var result = await ProcessExecutor.ExecuteAsync(compiler, arguments, root, TimeSpan.FromSeconds(60));
            Assert.False(result.timedOut);
            if (corruptStorage)
            {
                Assert.NotEqual(0, result.exitCode);
                Assert.Contains("BGCS native reference field storage differs", result.standardOutput + result.standardError);
            }
            else Assert.True(result.exitCode == 0, result.standardOutput + result.standardError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("unproven")]
    [InlineData("negative")]
    [InlineData("overflow")]
    [InlineData("ordinary-mismatch")]
    public void GraphValidationUsesReferenceSlotsAndStillRejectsMalformedStorage(string condition)
    {
        StableId recordId = new("record"), recordType = new("record-type"), large = new("large"), reference = new("reference"), alias = new("alias"), fieldId = new("field"), aliasId = new("member-alias");
        NativeDeclaration record = new(recordId, "Class", "Holder", "Holder", recordType, new("native.hpp", 1, 1))
        { isCompleteDefinition = true, children = [fieldId, aliasId] };
        NativeDeclaration memberAlias = new(aliasId, "Typedef", "Reference", "Holder::Reference", alias, new("native.hpp", 2, 1)) { parent = recordId };
        NativeDeclaration field = new(fieldId, "Field", "value", "Holder::value", alias, new("native.hpp", 2, 1))
        { parent = recordId, offset = 0, storageSize = 8, storageEvidence = new(EvidenceLevel.Declared, "fixture:compiler-slot") };
        field = condition switch
        {
            "missing" => field with { storageSize = null, storageEvidence = null },
            "unproven" => field with { storageEvidence = new(EvidenceLevel.UserAsserted, "fixture:user") },
            "negative" => field with { storageSize = -1 },
            "overflow" => field with { storageSize = long.MaxValue },
            "ordinary-mismatch" => field with { type = large, storageSize = 4 },
            _ => field
        };
        NativeModule module = new("fixture", "x86_64-pc-linux-gnu", 8,
            [new(recordType, NativeTypeCategory.Record, "Holder", 8, 8) { declaration = recordId },
             new(large, NativeTypeCategory.Record, "LargeReferent", 64, 1),
             new(reference, NativeTypeCategory.Reference, "LargeReferent &", 64, 1) { elementType = large },
             new(alias, NativeTypeCategory.Alias, "Reference", 64, 1) { elementType = reference }], [record, field, memberAlias], [])
        { language = SourceLanguage.Cpp };
        var diagnostics = new NativeModuleVerifier().Validate(module);
        if (condition == "valid") Assert.Empty(diagnostics);
        else Assert.Contains(diagnostics, diagnostic => diagnostic.isFailure && diagnostic.message.Contains(condition switch
        {
            "missing" => "omitted its compiler-established field storage size",
            "overflow" => "exceeds its record",
            "ordinary-mismatch" => "inconsistent with its nonreference type size",
            _ => "invalid field storage facts"
        }, StringComparison.Ordinal));
        if (condition is "missing" or "unproven" or "negative")
            Assert.Throws<NotSupportedException>(() => new NativeStorageAssertionEmitter().Emit(module, [], new("", "", new("X86_64SysV", false))));
    }

    private static NativeModule Copy(
        NativeModule source,
        NativeDeclaration field
    )
        => new(source.targetId, source.triple, source.pointerSize, source.types,
            source.declarations.Select(declaration => declaration.id == field.id ? field : declaration).ToImmutableArray(), source.diagnostics)
        { language = source.language, byteOrder = source.byteOrder };

    private static string WorkerRoot()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "BindGen-CS.slnx"))) repository = repository.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(repository!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
