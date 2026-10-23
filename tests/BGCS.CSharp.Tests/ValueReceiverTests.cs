using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.CSharp.Planning;
using BGCS.CSharp.Projection;
using BGCS.CSharp.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace BGCS.CSharp.Tests;

public sealed class ValueReceiverTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task ValueMembersReuseCheckedOriginalStorageAndAnExplicitContext(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        ManagedApiModel model = Model(sourceRuntime: sourceRuntime);
        NeutralBackendTests.MemorySink sink = new();
        EmissionRequest request = new("native", sink)
        {
            importMode = mode,
            includeRuntimeSource = sourceRuntime,
            singleFile = sourceRuntime,
            runtimeNamespace = sourceRuntime ? "Generated.Values.Runtime" : "BGCS.Runtime"
        };
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, request);
        CSharpStoragePlan carrier = Assert.Single(plan.storage, item => item.nativeType == new StableId("record"));
        Assert.True(carrier.valueMembers.Length == 2, string.Join(Environment.NewLine, plan.valueParameterOmissions.Select(omission => omission.reason)));
        Assert.Empty(plan.objects);
        Assert.Empty(plan.valueParameterOmissions);
        Assert.Contains(carrier.valueMembers, member => member.isReadOnly && member.signature.name == "Read");
        Assert.Contains(carrier.valueMembers, member => !member.isReadOnly && member.signature.name == "Add");
        await new CSharpTestBackend().EmitAsync(model, request, CancellationToken.None);
        string allSource = string.Join("\n", sink.sources.Values);
        Assert.Contains(".Add(ref this, delta)", allSource);
        Assert.Contains(".Read(in this)", allSource);
        Assert.Contains("public readonly int Read(", allSource);
        StructDeclarationSyntax declaration = Assert.Single(sink.sources.SelectMany(pair => CSharpSyntaxTree.ParseText(pair.Value)
            .GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>()), type => type.Identifier.ValueText == carrier.name);
        Assert.Equal(carrier.fields.Length, declaration.Members.OfType<FieldDeclarationSyntax>().Count(field => !field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword))));
        string context = sourceRuntime ? "ValueApiContext" : "ValueApi";
        string carrierName = sourceRuntime ? "ValueApi.NativeValue" : "NativeValue";
        string consumer = $$"""
            namespace Generated.Values;
            public static class ValueConsumer {
                public static int Use({{context}} context) {
                    {{carrierName}} value=default;
                    value.Add(context,2);
                    return value.Read(context);
                }
            }
            """;
        string[] references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        CSharpCompilation compilation = CSharpCompilation.Create("ValueReceiver_" + Guid.NewGuid().ToString("N"),
            sink.sources.Select(pair => CSharpSyntaxTree.ParseText(pair.Value, new(LanguageVersion.CSharp14), pair.Key))
                .Append(CSharpSyntaxTree.ParseText(consumer, new(LanguageVersion.CSharp14))),
            references.Append(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path)),
            new(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));
        if (mode == BindingImportMode.LibraryImport) await CompileWithSdkAsync(sink, consumer, sourceRuntime);
        else
        {
            using MemoryStream image = new();
            var emitted = compilation.Emit(image);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        }
        new EmittedInteropContractReader().Verify(plan, sink.sources.Select(source => (source.Key, source.Value)), "native");
        foreach (string mutation in new[] { "ref this", "in this", "ThrowIfNull", "readonly int Read" })
        {
            string replace = mutation switch { "ref this" => "ref __bgcsCopy", "in this" => "in __bgcsCopy", "ThrowIfNull" => "KeepAlive", _ => "int Read" };
            var changed = sink.sources.Select(pair => (pair.Key, pair.Value.Replace(mutation, replace, StringComparison.Ordinal))).ToArray();
            Assert.Contains(changed, pair => pair.Item2 != sink.sources[pair.Key]);
            Assert.Throws<InvalidOperationException>(() => new EmittedInteropContractReader().Verify(plan, changed, "native"));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("retained")]
    [InlineData("unknown-retention")]
    [InlineData("output")]
    [InlineData("owner-lifetime")]
    [InlineData("volatile")]
    [InlineData("unknown-qualifier")]
    [InlineData("const-write")]
    [InlineData("overaligned")]
    [InlineData("retained-owner")]
    [InlineData("unknown-record-role")]
    public void MissingOrIncompatibleReceiverProofKeepsTheRawImport(string fault)
    {
        ManagedApiModel model = Model(fault);
        CSharpEmissionPlan plan = new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink()));
        Assert.DoesNotContain(plan.storage.SelectMany(carrier => carrier.valueMembers), member => plan.methods[member.methodIndex].nativeExport == new StableId("add"));
        Assert.Contains(plan.valueParameterOmissions, omission => omission.nativeExport == new StableId("add"));
        Assert.Contains(plan.methods, method => method.nativeExport == new StableId("add"));
    }

    [Theory]
    [InlineData("object-name")]
    [InlineData("release-domain")]
    [InlineData("pointer-alias")]
    public void ContractsOnlyRequestsCannotInventAResourceOrCarrier(string fault)
    {
        ManagedApiModel original = Model();
        ManagedReceiverRequest request = new("add", fault == "pointer-alias" ? "alias" : "NativeValue", "self")
        {
            kind = ManagedReceiverKind.Value,
            objectName = fault == "object-name" ? "NewObject" : null,
            releaseDomain = fault == "release-domain" ? "free" : null
        };
        InteropModule source = original.interop;
        if (fault == "pointer-alias") source = new(source.libraryId, source.targetId, source.pointerSize,
            source.types.Add(new(new("alias"), "Alias", NativeTypeCategory.Alias, 8, 8) { elementType = new("record-pointer") }), source.functions, [])
        { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        Assert.Throws<ArgumentException>(() => new DefaultManagedProjection().Project(source,
            new("Generated.Values", "ValueApi") { memberFunctions = [request] }, CancellationToken.None));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("size")]
    [InlineData("carrier-name")]
    [InlineData("member-overload")]
    public void CarrierMembersCannotShadowStorageOrRequiredPresentation(string fault)
    {
        ManagedApiModel model = Model(collision: fault);
        Assert.Throws<InvalidOperationException>(() => new CSharpTestPlanner().Plan(model, new("native", new NeutralBackendTests.MemorySink())));
    }

    internal static async Task CompileWithSdkAsync(
        NeutralBackendTests.MemorySink sink,
        string consumer,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-value-receiver-compile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var source in sink.sources)
            {
                string path = Path.GetFullPath(Path.Combine(root, source.Key));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("A test source escaped its temporary project.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, source.Value);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "Consumer.cs"), consumer);
            string reference = sourceRuntime ? string.Empty : "<ItemGroup><Reference Include=\"BGCS.Runtime\"><HintPath>"
                + System.Security.SecurityElement.Escape(typeof(BGCS.Runtime.NativeModuleSession).Assembly.Location)
                + "</HintPath></Reference></ItemGroup>";
            await File.WriteAllTextAsync(Path.Combine(root, "Consumer.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>"
                + "<TargetFramework>net10.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable>"
                + "<TreatWarningsAsErrors>true</TreatWarningsAsErrors><NuGetAudit>false</NuGetAudit></PropertyGroup>" + reference + "</Project>");
            string? sdk = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            string executable = string.IsNullOrEmpty(sdk) ? "dotnet" : Path.Combine(sdk, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            ProcessStartInfo start = new(executable) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "build", "Consumer.csproj", "-c", "Release", "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false" }) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the SDK consumer build.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(deadline.Token); }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            string diagnostics = await output + Environment.NewLine + await error;
            Assert.True(process.ExitCode == 0, diagnostics);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ManagedApiModel Model(
        string? fault = null,
        bool sourceRuntime = false,
        string? collision = null
    ) {
        Evidence evidence = new(EvidenceLevel.UserAsserted, "fixture:original-value-storage");
        InteropType integer = new(new("int"), "int", NativeTypeCategory.Primitive, 4, 4) { primitiveKind = "Int", isSigned = true };
        InteropType record = new(new("record"), "NativeValue", NativeTypeCategory.Record, 8, 4)
        { recordKind = "Struct", fields = [new(new("value"), "value", integer.id, 0, 0, null), new(new("tag"), "tag", integer.id, 4, 32, null)] };
        if (fault == "unknown-record-role") record = record with { recordKind = null };
        InteropType qualified = new(new("const-record"), "const NativeValue", NativeTypeCategory.Qualified, 8, 4) { elementType = record.id, qualifier = "Const" };
        InteropType pointer = new(new("record-pointer"), "NativeValue*", NativeTypeCategory.Pointer, 8, 8) { elementType = record.id };
        InteropType constPointer = pointer with { id = new("const-pointer"), nativeName = "const NativeValue*", elementType = qualified.id };
        if (fault == "const-write") pointer = pointer with { elementType = qualified.id };
        if (fault == "overaligned")
        {
            record = record with { size = 32, alignment = 32 };
            qualified = qualified with { size = 32, alignment = 32 };
        }
        if (fault is "volatile" or "unknown-qualifier")
        {
            qualified = qualified with { qualifier = fault == "volatile" ? "Volatile" : "Unknown" };
            pointer = pointer with { elementType = qualified.id };
        }
        ValueParameterContract Value(NativeParameterDirection direction) => new(direction, new(NativeLifetimeKind.Call, null, evidence), evidence)
        { allowsNull = false, retainsPointer = false };
        ValueParameterContract? value = Value(NativeParameterDirection.InputOutput);
        value = fault switch
        {
            "missing" => null,
            "retained" => value with { retainsPointer = true },
            "unknown-retention" => value with { retainsPointer = null },
            "output" => value with { direction = NativeParameterDirection.Output },
            "owner-lifetime" => value with { lifetime = new(NativeLifetimeKind.Owner, null, evidence) },
            _ => value
        };
        InteropFunction add = new(new("add"), "native_add", integer.id,
            [new(new("add-self"), "self", pointer.id) { singleValue = value }, new(new("delta"), "delta", integer.id)], "C");
        if (fault == "retained-owner") add = add with
        {
            parameters = add.parameters.SetItem(0, add.parameters[0] with
            { ownership = new(NativeOwnershipKind.Borrowed, new(NativeLifetimeKind.Call, null, evidence), evidence) { retainsPointer = true } })
        };
        InteropFunction read = new(new("read"), "native_read", integer.id,
            [new(new("read-self"), "self", constPointer.id) { singleValue = Value(NativeParameterDirection.Input) }], "C");
        if (collision == "member-overload") read = read with
        { parameters = [new(new("read-delta"), "delta", integer.id), new(new("read-self"), "self", pointer.id) { singleValue = Value(NativeParameterDirection.InputOutput) }] };
        InteropModule module = new("values", "windows-x64-msvc", 8, [integer, record, qualified, pointer, constPointer], [add, read], []) { byteOrder = global::BGCS.Core.Native.NativeByteOrder.LittleEndian };
        string addName = collision == "size" ? "NativeSize" : collision == "carrier-name" ? "NativeValue" : "Add";
        return new DefaultManagedProjection().Project(module, new("Generated.Values", "ValueApi")
        {
            style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
            nestGeneratedTypesInApi = sourceRuntime,
            names = ImmutableDictionary<string, string>.Empty.Add("native_add", addName).Add("native_read", collision == "member-overload" ? "Add" : "Read"),
            records = [new("NativeValue") { emitSizeConstant = true,
                fields = [new("value") { name = collision == "field" ? "Add" : "Value" }, new("tag") { name = "Tag" }] }],
            memberFunctions = [new("add", "NativeValue", "self") { kind = ManagedReceiverKind.Value }, new("read", "NativeValue", "self") { kind = ManagedReceiverKind.Value }]
        }, CancellationToken.None);
    }
}
