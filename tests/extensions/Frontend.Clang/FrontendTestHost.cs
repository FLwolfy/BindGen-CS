using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Core.Inputs;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Targets.Desktop;
using System.Text.Json;
using BGCS.Core.Hosting;
using BGCS.Hosting.Standard;

namespace BGCS.Frontend.Clang.Tests;

/// <summary>Provides explicitly composed session facilities for each public frontend test instance.</summary>
public abstract class FrontendTestHost : IDisposable
{
    protected HostServices host { get; } = new HostServicesProvider().Create(new(AppContext.BaseDirectory, JsonSerializer.SerializeToElement(new { })));
    protected async Task<FrontendResult> ParseSourceAsync(
        string source,
        NativeTargetDescriptor? target = null,
        SourceLanguage language = SourceLanguage.Cpp,
        System.Collections.Immutable.ImmutableArray<string> defines = default,
        string? standard = null
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-public-frontend-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using HostServices parseHost = new HostServicesProvider().Create(new(root, JsonSerializer.SerializeToElement(new { })));
            string header = Path.Combine(root, language == SourceLanguage.Cpp ? "input.hpp" : "input.h");
            await File.WriteAllTextAsync(header, source);
            if (target is null && !new DesktopTargetProvider().TryResolve(new(new("host")) { language = language }, out target)) throw new InvalidOperationException("The explicit desktop profile declined its host target.");
            return await new ClangFrontend(parseHost).ParseAsync(new(target!, language, [header])
            { pathBase = root, headerSearchPaths = target!.headerSearchPaths, defines = defines.IsDefault ? [] : defines, languageStandard = standard ?? (language == SourceLanguage.Cpp ? "c++23" : "c17") }, CancellationToken.None);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    public virtual void Dispose() => host.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
