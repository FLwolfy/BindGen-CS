using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BGCS.Contracts.Requests;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BGCS.CSharp.Planning;

/// <summary>Reads immutable metadata images and source text without loading consumer assemblies or executing their code.</summary>
internal sealed class ManagedConstantCompilerInputs
{
    private const long C_TOTAL_BYTES = 512L * 1024 * 1024;
    private const int C_IMAGE_BYTES = 64 * 1024 * 1024;
    private const int C_SOURCE_BYTES = 16 * 1024 * 1024;
    private static readonly ConditionalWeakTable<ManagedCompilationInputs, ManagedConstantCompilerInputs> m_prepared = new();
    private readonly ImmutableArray<(MetadataReference Reference, ManagedAssemblyInput Input)> m_images;

    private ManagedConstantCompilerInputs(
        ImmutableArray<MetadataReference> references,
        ImmutableArray<SyntaxTree> sources,
        CSharpParseOptions parseOptions,
        ImmutableArray<(MetadataReference, ManagedAssemblyInput)> images
    ) { this.references = references; this.sources = sources; this.parseOptions = parseOptions; m_images = images; }

    internal ImmutableArray<MetadataReference> references { get; }
    internal ImmutableArray<SyntaxTree> sources { get; }
    internal CSharpParseOptions parseOptions { get; }

    internal static ManagedConstantCompilerInputs Create(
        ManagedCompilationInputs? inputs,
        ImmutableArray<MetadataReference> framework,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();
        if (inputs is null) return new(framework, [], new(LanguageVersion.CSharp14), []);
        return m_prepared.GetValue(inputs, value => Prepare(value, framework, cancellationToken));
    }

    private static ManagedConstantCompilerInputs Prepare(
        ManagedCompilationInputs inputs,
        ImmutableArray<MetadataReference> framework,
        CancellationToken cancellationToken
    ) {
        if (inputs.assemblies.IsDefault || inputs.sources.IsDefault || inputs.defines.IsDefault)
            throw new ArgumentException("Frozen managed compilation collections must be initialized.");
        if (inputs.assemblies.Length > 1024 || inputs.sources.Length > 256 || inputs.defines.Length > 1024)
            throw new ArgumentException("Frozen managed compilation inputs exceed 1024 assemblies, 256 sources or 1024 symbols.");
        foreach (string symbol in inputs.defines)
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 512 || !SyntaxFacts.IsValidIdentifier(symbol))
                throw new ArgumentException("A managed preprocessor symbol must be a bounded C# identifier.");
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14).WithPreprocessorSymbols(inputs.defines);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        if (!inputs.assemblies.Any(input => input?.isFramework == true)) references.AddRange(framework);
        var images = ImmutableArray.CreateBuilder<(MetadataReference, ManagedAssemblyInput)>();
        var trees = ImmutableArray.CreateBuilder<SyntaxTree>();
        HashSet<string> paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        long total = 0;
        foreach (ManagedAssemblyInput input in inputs.assemblies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(input);
            ValidatePath(input.path);
            if (!paths.Add(input.path) || string.IsNullOrWhiteSpace(input.assemblyIdentity))
                throw new ArgumentException("Frozen assembly paths must be unique and declare a nonempty metadata identity.");
            ValidateBytes(input.image, input.digest.value, C_IMAGE_BYTES, input.path);
            try
            {
                MetadataReference reference = MetadataReference.CreateFromImage(input.image, filePath: input.path);
                references.Add(reference);
                images.Add((reference, input));
            }
            catch (Exception failure) when (failure is BadImageFormatException or ArgumentException)
            { throw new ArgumentException("A frozen managed reference is not an independently readable assembly image: " + input.path, failure); }
        }
        foreach (ManagedSourceInput input in inputs.sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(input);
            ValidatePath(input.path);
            if (!paths.Add(input.path)) throw new ArgumentException("Frozen managed source and reference paths must be unique.");
            ValidateBytes(input.image, input.digest.value, C_SOURCE_BYTES, input.path, allowEmpty: true);
            using MemoryStream stream = new(input.image.ToArray(), writable: false);
            SourceText text = SourceText.From(stream, encoding: Encoding.UTF8, throwIfBinaryDetected: true);
            if (!string.Equals(text.ToString(), input.text, StringComparison.Ordinal))
                throw new ArgumentException("Frozen managed source text differs from its original encoded bytes: " + input.path);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(text, parseOptions, input.path, cancellationToken);
            Diagnostic[] failures = tree.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(8).ToArray();
            if (failures.Length != 0) throw new ArgumentException("A frozen managed source has syntax errors: " + string.Join("; ", failures.Select(diagnostic => diagnostic.ToString())));
            trees.Add(tree);
        }
        return new(references.ToImmutable(), trees.ToImmutable(), parseOptions, images.ToImmutable());

        void ValidateBytes(
            ImmutableArray<byte> image,
            string? digest,
            int limit,
            string path,
            bool allowEmpty = false
        ) {
            if (image.IsDefault || !allowEmpty && image.Length == 0 || image.Length > limit || checked(total += image.Length) > C_TOTAL_BYTES)
                throw new ArgumentException("Frozen managed input bytes exceed their per-file or 512 MiB total limit: " + path);
            if (Convert.ToHexStringLower(SHA256.HashData(image.AsSpan())) != digest)
                throw new ArgumentException("Frozen managed input bytes do not match their declared SHA-256: " + path);
        }
    }

    internal void VerifyAssemblyClosure(
        CSharpCompilation compilation,
        CancellationToken cancellationToken
    ) {
        if (m_images.IsEmpty) return;
        var symbols = references.Select(reference => (Reference: reference, Symbol: compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol)).ToArray();
        foreach ((MetadataReference reference, ManagedAssemblyInput input) in m_images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IAssemblySymbol assembly = symbols.Single(item => ReferenceEquals(item.Reference, reference)).Symbol
                ?? throw new ArgumentException("A frozen reference is a module or invalid metadata rather than an assembly: " + input.path);
            if (assembly.Identity.ToString() != input.assemblyIdentity)
                throw new ArgumentException("A frozen assembly's actual metadata identity differs from its declaration: " + input.path);
            ImmutableHashSet<string> optionalForwarders = OptionalFrameworkForwarders(input);
            foreach (IAssemblySymbol dependency in assembly.Modules.SelectMany(module => module.ReferencedAssemblySymbols))
                if (!optionalForwarders.Contains(dependency.Identity.Name)
                    && !symbols.Any(item => item.Symbol is not null && SymbolEqualityComparer.Default.Equals(item.Symbol, dependency)))
                    throw new ArgumentException("The frozen managed reference closure is incomplete: " + input.assemblyIdentity + " requires " + dependency.Identity
                        + ". Declare or capture the exact matching dependency; metadata lookup never loads it from the process or current directory.");
        }
    }

    private static ImmutableHashSet<string> OptionalFrameworkForwarders(ManagedAssemblyInput input)
    {
        if (!input.isFramework) return ImmutableHashSet<string>.Empty;
        using PEReader pe = new(input.image);
        MetadataReader metadata = pe.GetMetadataReader();
        HashSet<AssemblyReferenceHandle> required = metadata.TypeReferences.Select(handle => metadata.GetTypeReference(handle).ResolutionScope)
            .Where(handle => handle.Kind == HandleKind.AssemblyReference).Select(handle => (AssemblyReferenceHandle)handle).ToHashSet();
        HashSet<string> requiredNames = required.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return metadata.ExportedTypes.Select(handle => metadata.GetExportedType(handle).Implementation)
            .Where(handle => handle.Kind == HandleKind.AssemblyReference).Select(handle => (AssemblyReferenceHandle)handle)
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).Where(name => !requiredNames.Contains(name))
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static Diagnostic[] ConstantFailures(
        CSharpCompilation compilation,
        SyntaxTree assertions,
        CSharpEmissionPlan plan,
        CancellationToken cancellationToken
    )
        => compilation.GetDiagnostics(cancellationToken).Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(diagnostic => !UnrelatedPartialMethodBody(diagnostic, assertions, plan, cancellationToken)).Take(16).ToArray();

    private static bool UnrelatedPartialMethodBody(
        Diagnostic diagnostic,
        SyntaxTree assertions,
        CSharpEmissionPlan plan,
        CancellationToken cancellationToken
    ) {
        SyntaxTree? tree = diagnostic.Location.SourceTree;
        if (tree is null || ReferenceEquals(tree, assertions)) return false;
        SyntaxNode node = tree.GetRoot(cancellationToken).FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        MethodDeclarationSyntax? method = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null || !(method.Body?.FullSpan.Contains(diagnostic.Location.SourceSpan) == true
            || method.ExpressionBody?.FullSpan.Contains(diagnostic.Location.SourceSpan) == true)) return false;
        TypeDeclarationSyntax? type = method.Parent as TypeDeclarationSyntax;
        if (type is null || !type.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword))) return false;
        string declared = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse()
            .Select(scope => scope.Name.ToString().Replace("@", string.Empty, StringComparison.Ordinal))
            .Concat(type.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(parent => parent.Identifier.ValueText)).Append(type.Identifier.ValueText));
        string ns = string.Join(".", plan.api.@namespace.Split('.').Select(part => part.TrimStart('@'))) + ".";
        // A C# constant can refer to a method's name, but cannot evaluate its body.
        // Only these generated-context extension bodies are deferred; fields,
        // signatures, aliases, enum members and synthetic assertions still fail.
        return declared == ns + plan.contextName.TrimStart('@') || declared == ns + plan.api.name.TrimStart('@');
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Frozen managed inputs require absolute original paths, not process-relative lookup.");
    }
}
