using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BGCS.Configuration;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;
using BGCS.Infrastructure.Execution;
using BGCS.Infrastructure.IO;

namespace BGCS.Validation;

/// <summary>Measures closed consumer value types in an isolated .NET process without loading consumer assemblies into the generator.</summary>
public sealed class ManagedExternalTypeProbe
{
    /// <summary>Compiles and executes typed field measurements against explicitly captured consumer inputs.</summary>
    /// <param name="projections">The requested exact consumer fields and closed type syntax.</param>
    /// <param name="configuration">Explicit consumer sources, assembly references and conditional symbols.</param>
    /// <param name="baseDirectory">The absolute consumer input base.</param>
    /// <param name="timeout">The complete compile-and-execute deadline.</param>
    /// <param name="cancellationToken">Cancels the probe and terminates its process tree before returning.</param>
    /// <returns>Detached CLR observations and frozen inputs that the caller must revalidate before publication.</returns>
    /// <remarks>This process boundary is a crash boundary, not a sandbox for untrusted consumer code. Storage measurements do not certify native by-value calls.</remarks>
    public async Task<ManagedExternalTypeProbeResult> ProbeAsync(
        ImmutableArray<ExternalTypeProjection> projections,
        ManagedBuildConfiguration configuration,
        string baseDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) {
        if (projections.IsDefaultOrEmpty) throw new ArgumentException("At least one external type projection is required.", nameof(projections));
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        if (!Path.IsPathFullyQualified(baseDirectory)) throw new ArgumentException("An external type probe requires an absolute consumer input base.", nameof(baseDirectory));
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
        using CancellationTokenSource deadline = new(timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        CancellationToken token = linked.Token;
        string temporary = Path.Combine(Path.GetTempPath(), "bgcs-external-type-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            ManagedSdkSelection sdk = ManagedSdkEnvironment.Resolve();
            string sdkRoot = Path.GetDirectoryName(sdk.executable)!;
            ImmutableArray<string> sources = configuration.sources.Select(path => Path.GetFullPath(path, baseDirectory)).ToImmutableArray();
            ImmutableArray<string> references = configuration.references.Select(path => Path.GetFullPath(path, baseDirectory)).ToImmutableArray();
            ImmutableArray<DeclaredExternalInput>.Builder declared = ImmutableArray.CreateBuilder<DeclaredExternalInput>();
            foreach (string path in sources.Concat(references).Append(sdk.executable)) declared.Add(new(path, ExternalInputKind.File));
            // These are the actual compiler, reference assemblies and executing CLR closure, not version labels standing in for bytes.
            declared.Add(new(Path.Combine(sdkRoot, "sdk", sdk.sdkVersion), ExternalInputKind.Directory));
            declared.Add(new(Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref"), ExternalInputKind.Directory));
            declared.Add(new(Path.Combine(sdkRoot, "shared", "Microsoft.NETCore.App"), ExternalInputKind.Directory));
            // Explicit reference directories may supply dependency DLLs to SDK resolution; freeze their complete immediate content and membership.
            ImmutableArray<string> referenceDirectories = references.Select(path => Path.GetDirectoryName(path)!).Distinct(PathComparer()).ToImmutableArray();
            foreach (string directory in referenceDirectories)
                foreach (string dependency in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)) declared.Add(new(dependency, ExternalInputKind.File));
            SourceSnapshot captured = new ExternalInputSnapshotBuilder().Capture(declared.ToImmutable(), baseDirectory, token);
            captured = captured with { searchDirectories = captured.searchDirectories.AddRange(referenceDirectories.Select(path => DirectoryMembershipDigest.Capture(path, cancellationToken: token))) };
            string program = ProgramSource(projections);
            await File.WriteAllTextAsync(Path.Combine(temporary, "Program.cs"), program, token).ConfigureAwait(false);
            XElement properties = new("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("TargetFramework", "net10.0"),
                new XElement("LangVersion", "14.0"), new XElement("AllowUnsafeBlocks", "true"), new XElement("Nullable", "enable"),
                new XElement("TreatWarningsAsErrors", "true"), new XElement("EnableDefaultCompileItems", "false"),
                new XElement("EnableNETAnalyzers", "false"), new XElement("BGCSGenerateBindings", "false"), new XElement("UseAppHost", "false"));
            if (!configuration.defines.IsDefaultOrEmpty)
            {
                foreach (string define in configuration.defines)
                    if (define.Length == 0 || char.IsDigit(define[0]) || define.Any(character => !char.IsLetterOrDigit(character) && character != '_'))
                        throw new ArgumentException("An external type probe conditional symbol is invalid.");
                properties.Add(new XElement("DefineConstants", "$(DefineConstants);" + string.Join(';', configuration.defines)));
            }
            XElement items = new("ItemGroup", new XElement("Compile", new XAttribute("Include", "Program.cs")));
            foreach (string source in sources.Distinct(PathComparer())) items.Add(new XElement("Compile", new XAttribute("Include", EscapeItem(source))));
            int referenceIndex = 0;
            foreach (string reference in references.Distinct(PathComparer())) items.Add(new XElement("Reference", new XAttribute("Include", "Consumer" + referenceIndex++),
                new XElement("HintPath", EscapeItem(reference)), new XElement("Private", "true")));
            string project = Path.Combine(temporary, "Probe.csproj");
            new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties, items)).Save(project);
            await File.WriteAllTextAsync(Path.Combine(temporary, "global.json"), JsonSerializer.Serialize(new
            { sdk = new { version = sdk.sdkVersion, rollForward = "disable", allowPrerelease = false } }), token).ConfigureAwait(false);
            ProcessExecutionResult built = await ProcessExecutor.ExecuteAsync(sdk.executable,
                ["build", project, "-v:minimal", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"], temporary, timeout, token,
                environmentOverrides: sdk.environment).ConfigureAwait(false);
            if (built.exitCode != 0 || built.timedOut) throw new InvalidOperationException("The external consumer type probe did not compile: " + built.standardOutput + built.standardError);
            if (!SourceSnapshotVerifier.Matches(captured, token)) throw new IOException("External consumer inputs or managed compiler components changed during probe compilation.");
            string output = Path.Combine(temporary, "bin", "Debug", "net10.0");
            SourceSnapshot executable = new ExternalInputSnapshotBuilder().Capture([new(output, ExternalInputKind.Directory)], temporary, token);
            string resultPath = Path.Combine(temporary, "result.json");
            ProcessExecutionResult executed = await ProcessExecutor.ExecuteAsync(sdk.executable, [Path.Combine(output, "Probe.dll"), resultPath],
                temporary, timeout, token, environmentOverrides: sdk.environment).ConfigureAwait(false);
            if (executed.exitCode != 0 || executed.timedOut) throw new InvalidOperationException("The external consumer field probe failed: " + executed.standardOutput + executed.standardError);
            if (!SourceSnapshotVerifier.Matches(captured, token) || !SourceSnapshotVerifier.Matches(executable, token))
                throw new IOException("External consumer or probe executable inputs changed while CLR fields were measured.");
            string identity = JsonSerializer.Serialize(captured) + JsonSerializer.Serialize(executable.files.Select(file => new
            { path = Path.GetRelativePath(output, file.path).Replace('\\', '/'), file.digest })) + program;
            ContentDigest digest = new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
            using JsonDocument result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, token).ConfigureAwait(false));
            JsonElement root = result.RootElement;
            ManagedProbeIdentity probe = new(root.GetProperty("framework").GetString()!, root.GetProperty("rid").GetString()!,
                root.GetProperty("architecture").GetString()!, root.GetProperty("pointerSize").GetInt32(), root.GetProperty("littleEndian").GetBoolean(), digest);
            JsonElement.ArrayEnumerator facts = root.GetProperty("facts").EnumerateArray();
            ImmutableArray<ManagedTypeStorageFact>.Builder measured = ImmutableArray.CreateBuilder<ManagedTypeStorageFact>();
            int index = 0;
            foreach (JsonElement fact in facts)
            {
                if (index >= projections.Length) throw new InvalidOperationException("The external type probe returned too many observations.");
                ImmutableArray<ManagedFieldStorageFact>.Builder fields = ImmutableArray.CreateBuilder<ManagedFieldStorageFact>();
                foreach (JsonElement field in fact.GetProperty("fields").EnumerateArray()) fields.Add(new(field.GetProperty("name").GetString()!,
                    field.GetProperty("type").GetString()!, Enum.Parse<ManagedScalarKind>(field.GetProperty("kind").GetString()!, ignoreCase: false),
                    field.GetProperty("size").GetInt32(), field.GetProperty("offset").GetInt64(), field.GetProperty("isPublic").GetBoolean(), field.GetProperty("isWritable").GetBoolean()));
                measured.Add(new(projections[index++].managedType, fact.GetProperty("type").GetString()!, fact.GetProperty("size").GetInt32(),
                    fact.GetProperty("alignment").GetInt32(), fact.GetProperty("referenceFree").GetBoolean(), fact.GetProperty("layout").GetString()!,
                    fields.ToImmutable(), probe, new(EvidenceLevel.Verified, "bgcs:managed-field-probe:v1")));
            }
            if (index != projections.Length) throw new InvalidOperationException("The external type probe omitted a requested consumer type.");
            return new(measured.ToImmutable(), captured);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("The external consumer type probe exceeded its compile-and-execute deadline.");
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static string ProgramSource(ImmutableArray<ExternalTypeProjection> projections)
    {
        StringBuilder source = new("""
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Linq;
            using System.Reflection;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Text.Json;
            [StructLayout(LayoutKind.Sequential)] struct Alignment<T> where T : unmanaged { public byte prefix; public T value; }
            public static unsafe class Program {
              static string Kind(Type t) => t == typeof(sbyte) || t == typeof(short) || t == typeof(int) || t == typeof(long) ? "SignedInteger"
                : t == typeof(byte) || t == typeof(ushort) || t == typeof(uint) || t == typeof(ulong) ? "UnsignedInteger"
                : t == typeof(float) || t == typeof(double) ? "BinaryFloat"
                : t.IsPointer || t == typeof(IntPtr) || t == typeof(UIntPtr) ? "NativeAddress" : "Unknown";
              static int Size(Type t) => Kind(t) == "NativeAddress" ? IntPtr.Size
                : t == typeof(sbyte) || t == typeof(byte) ? 1
                : t == typeof(short) || t == typeof(ushort) ? 2
                : t == typeof(int) || t == typeof(uint) || t == typeof(float) ? 4
                : t == typeof(long) || t == typeof(ulong) || t == typeof(double) ? 8
                : throw new NotSupportedException("A projected consumer field has no supported scalar or data-address storage.");
              static object Field<T>(ref T value, void* field, string name) where T : unmanaged {
                var info = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.Public)!;
                if (!info.IsPublic || info.IsInitOnly || info.IsLiteral) throw new NotSupportedException("A projected consumer field must be public and writable.");
                return new { name, type = info.FieldType.FullName!, kind = Kind(info.FieldType), size = Size(info.FieldType),
                  offset = Unsafe.ByteOffset(ref Unsafe.As<T, byte>(ref value), ref Unsafe.AsRef<byte>(field)).ToInt64(),
                  isPublic = info.IsPublic, isWritable = !info.IsInitOnly && !info.IsLiteral };
              }
              static object Fact<T>(object[] fields) where T : unmanaged {
                var all = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (all.Length != fields.Length) throw new InvalidOperationException("A consumer type contains unmapped or inaccessible instance fields.");
                Alignment<T> alignment = default;
                return new { type = typeof(T).AssemblyQualifiedName!, size = Unsafe.SizeOf<T>(),
                  alignment = checked((int)Unsafe.ByteOffset(ref alignment.prefix, ref Unsafe.As<T, byte>(ref alignment.value))),
                  referenceFree = !RuntimeHelpers.IsReferenceOrContainsReferences<T>(), layout = typeof(T).StructLayoutAttribute!.Value.ToString(), fields };
              }
              public static void Main(string[] args) {
                var facts = new List<object>();
            """);
        for (int index = 0; index < projections.Length; index++)
        {
            ExternalTypeProjection projection = projections[index];
            string type = ExternalCarrierPlanner.NormalizeType(projection.managedType);
            if (projection.fields.IsDefaultOrEmpty || projection.fields.Select(field => field.managedField).Distinct(StringComparer.Ordinal).Count() != projection.fields.Length)
                throw new ArgumentException("An external type probe requires distinct explicit consumer fields.");
            source.Append(type).Append(" value").Append(index).AppendLine(" = default;");
            source.Append("facts.Add(Fact<").Append(type).Append(">([");
            foreach (ExternalFieldProjection field in projection.fields)
            {
                string name = ExternalCarrierPlanner.NormalizeField(field.managedField);
                source.Append("Field(ref value").Append(index).Append(", (void*)(&value").Append(index).Append('.').Append(name).Append("), ")
                    .Append(JsonSerializer.Serialize(name.TrimStart('@'))).Append("),");
            }
            source.AppendLine("]));");
        }
        source.AppendLine("""
                File.WriteAllText(args[0], JsonSerializer.Serialize(new { framework = RuntimeInformation.FrameworkDescription,
                  rid = RuntimeInformation.RuntimeIdentifier, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                  pointerSize = IntPtr.Size, littleEndian = BitConverter.IsLittleEndian, facts }));
              }
            }
            """);
        return source.ToString();
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string EscapeItem(string path) => path.Replace("%", "%25", StringComparison.Ordinal).Replace("$", "%24", StringComparison.Ordinal)
        .Replace("@", "%40", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal).Replace("'", "%27", StringComparison.Ordinal)
        .Replace("(", "%28", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal).Replace("*", "%2A", StringComparison.Ordinal).Replace("?", "%3F", StringComparison.Ordinal);
}

/// <summary>Returns detached CLR storage observations together with their source, assembly and compiler input closure.</summary>
/// <param name="facts">One observation per requested consumer type in request order.</param>
/// <param name="inputs">Captured inputs to revalidate before publishing generated bindings.</param>
public sealed record ManagedExternalTypeProbeResult(
    ImmutableArray<ManagedTypeStorageFact> facts,
    SourceSnapshot inputs
);
