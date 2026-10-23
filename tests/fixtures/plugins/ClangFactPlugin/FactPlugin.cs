using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using BGCS.Core.Common;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Plugins;
using BGCS.Core.Requests;
using BGCS.Frontend.Clang.Advanced;

namespace ClangFactFixture;

public class FactPlugin : IBindingPlugin, IExternalInputProvider
{
    protected PluginConfiguration? configuration;
    public virtual PluginDescriptor descriptor => CreateDescriptor();
    public static PluginDescriptor CreateDescriptor() => new("clang.fixture", "1.0.0", [new("clang.fixture.facts", ExtensionPort.FrontendFactContributor)])
    {
        configurationSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object", additionalProperties = false, required = new[] { "externalFile" },
            properties = new { externalFile = new { type = "string", minLength = 1 }, mode = new { type = "string" } }
        })
    };
    public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration settings)
    {
        configuration = settings;
        registry.RegisterFactory<IClangFactContributor>("clang.fixture.facts", _ => CreateContributor(settings));
    }
    protected virtual FactContributor CreateContributor(PluginConfiguration settings) => new(settings);
    public ImmutableArray<DeclaredExternalInput> GetExternalInputs(ExternalInputContext context, CancellationToken token)
        => configuration is null ? [] : [new(configuration.ResolveInputPath("/externalFile"), ExternalInputKind.File)];
    public void Dispose() { }
}

public sealed class FingerprintedFactPlugin : FactPlugin, ICacheFingerprintProvider
{
    protected override FactContributor CreateContributor(PluginConfiguration settings) => new FingerprintedFactContributor(settings);
    public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken token)
        => Fingerprint(configuration!, 0);
    internal static CacheFingerprintContribution Fingerprint(PluginConfiguration settings, int calls)
    {
        string path = settings.ResolveInputPath("/externalFile");
        string mode = settings.settings.TryGetProperty("mode", out JsonElement value) ? value.GetString() ?? "" : "";
        string content = settings.settings.GetRawText() + "\n" + File.ReadAllText(path) + (mode == "changeFingerprint" ? "\n" + calls : "");
        return new(new(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)))), [new(path, ExternalInputKind.File)]);
    }
}

public class FactContributor(PluginConfiguration configuration) : IClangFactContributor, IDisposable
{
    private ClangFactContributionContext? m_context;
    protected int calls;
    protected PluginConfiguration settings => configuration;
    public NativeModule Contribute(ClangFactContributionContext context, NativeModule module, CancellationToken token)
    {
        m_context = context;
        if (context.request.purpose != FrontendRequestPurpose.SourceAnalysis) return module;
        calls++;
        string mode = configuration.settings.TryGetProperty("mode", out JsonElement value) ? value.GetString() ?? "" : "";
        if (mode == "crash") Environment.FailFast("Intentional isolated Clang fact contributor crash.");
        if (mode == "slow")
        {
            string marker = configuration.ResolveInputPath("/externalFile") + ".worker-pid";
            File.WriteAllText(marker + ".tmp", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            File.Move(marker + ".tmp", marker, overwrite: true);
            Thread.Sleep(Timeout.Infinite);
        }
        if (mode == "throw") throw new InvalidOperationException("Intentional Clang contributor failure.");
        token.ThrowIfCancellationRequested();
        NativeDeclaration declaration = module.declarations.Single(item => item.kind == "Function" && item.name == "read_value");
        if (!context.TryGetDeclarationCursor(declaration.id, out var cursor)) throw new InvalidOperationException("The stable declaration has no original cursor.");
        using var spelling = cursor.Spelling;
        if (spelling.ToString() != declaration.name || context.translationUnit.Cursor.Kind != ClangSharp.Interop.CXCursorKind.CXCursor_TranslationUnit)
            throw new InvalidOperationException("The live Clang context does not match the detached declaration.");
        var location = context.GetSourceLocation(cursor);
        if (location != declaration.source) throw new InvalidOperationException("Original cursor source mapping was lost.");
        string text = "advanced-clang:" + spelling + ":" + File.ReadAllText(configuration.ResolveInputPath("/externalFile"));
        return new(module.targetId, module.triple, module.pointerSize, module.types,
            module.declarations.SetItem(module.declarations.IndexOf(declaration), declaration with { documentation = text }), module.diagnostics)
        { language = module.language, byteOrder = module.byteOrder };
    }
    public void Dispose()
    {
        if (m_context is null) return;
        try { _ = m_context.translationUnit; }
        catch (ObjectDisposedException) { return; }
        throw new InvalidOperationException("The native contribution context remained active after its invocation.");
    }
}

public sealed class FingerprintedFactContributor(PluginConfiguration configuration) : FactContributor(configuration), ICacheFingerprintProvider, IExternalInputProvider
{
    public CacheFingerprintContribution CaptureFingerprint(CacheFingerprintContext context, CancellationToken token)
        => FingerprintedFactPlugin.Fingerprint(settings, calls);
    public ImmutableArray<DeclaredExternalInput> GetExternalInputs(ExternalInputContext context, CancellationToken token)
        => [new(settings.ResolveInputPath("/externalFile"), ExternalInputKind.File)];
}
