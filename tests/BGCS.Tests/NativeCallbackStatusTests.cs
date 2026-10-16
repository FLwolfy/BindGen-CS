using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeCallbackStatusTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task CallbackFailuresReleaseSuccessfulOwnedResultsAndPreserveNativeStatusCleanup(BindingImportMode mode, bool sourceRuntime)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-callback-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "callback.hpp"), """
                #include <stdexcept>
                namespace Demo {
                using Callback = int (*)(int value, void* state);
                inline int last_value=0, live_objects=0;
                inline int Invoke(Callback callback,int seed,void* state,int mode) {
                    int value=callback(seed,state); if(mode!=0) throw std::runtime_error("native after callback"); return value;
                }
                inline void Notify(Callback callback,int seed,void* state,int mode) {
                    last_value=callback(seed,state); if(mode!=0) throw std::runtime_error("native after callback");
                }
                inline int Last() {return last_value;}
                inline int Live() {return live_objects;}
                class Widget {
                    int value;
                public:
                    Widget(Callback callback,int seed,void* state) {
                        if(seed<0) throw std::runtime_error("native constructor");
                        value=callback(seed,state); ++live_objects;
                    }
                    ~Widget() noexcept {--live_objects;}
                    int Get() const {return value;}
                };
                }
                """);
            BindingConfiguration configuration = new()
            {
                schemaVersion = 1, library = new() { id = "callback_status", nativeName = "callback_status_fixture" },
                source = new() { language = SourceLanguage.Cpp, headers = ["callback.hpp"], parseMacros = false },
                api = new()
                {
                    @namespace = "Generated.Status", name = "StatusApi", importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("Demo::Invoke", "InvokeCallback").Add("Demo::Notify", "NotifyCallback")
                        .Add("Demo::Last", "Last").Add("Demo::Live", "Live").Add("Demo::Widget::Widget", "CreateWidget").Add("Demo::Widget::Get", "Get"),
                    functionAliases = [new("Demo::Invoke", "AliasInvoke"), new("Demo::Widget::Widget", "AliasCreateWidget")],
                    functionContainers = [new("Calls", [new("Demo::Invoke", "Compute")])]
                },
                semantics = new()
                {
                    callbackParameters = new[] { "Demo::Invoke", "Demo::Notify", "Demo::Widget::Widget" }.Select(function => new CallbackParameterConfiguration
                    {
                        function = function, parameter = "callback", lifetime = NativeLifetimeKind.Call, retained = false,
                        threading = NativeCallbackThreading.CallerThread, userDataParameter = "state", callableUserDataIndex = 1, failureReturn = "-7"
                    }).ToImmutableArray(),
                    ownedResults = [new() { function = "Demo::Widget::Widget", releaseThreadDomain = "any" }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                nativeBuild = new() { mode = "sources" }, cache = new() { enabled = false }
            };
            await using BindingHost host = BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            BuildResult result = await host.BuildAsync(new(new(configuration, root)));
            Assert.True(result.success, result.GetDiagnosticSummary());
            string targetRoot = Path.Combine(result.outputRoot, Assert.Single(result.generation.targetModels).targetId);
            string binary = Assert.Single(Directory.GetFiles(Path.Combine(targetRoot, "native", "bin")));
            string context = sourceRuntime ? "StatusApiContext" : "StatusApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string sharedInitialization = mode == BindingImportMode.FunctionTable ? "StatusApi.Load(binary);" : "StatusApi.Create();";
            string shared = sourceRuntime ? $$"""
                {{sharedInitialization}}
                if(StatusApi.AliasInvoke(value=>value+4,38,0)!=42 || StatusApi.Calls.Compute(value=>value+4,38,0)!=42) return 20;
                using(var sharedWidget=StatusApi.AliasCreateWidget(value=>value+4,38)) {if(sharedWidget.Get()!=42) return 21;}
                StatusApi.Dispose();
                """ : "";
            string runtime = sourceRuntime ? "Generated.Status.Runtime" : "BGCS.Runtime";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary, "callback_status_fixture", $$"""
                using System;
                using Generated.Status;
                using ApiContext = Generated.Status.{{context}};
                public static unsafe class Probe {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        if(api.InvokeCallback(value=>value+4,38,0)!=42 || api.AliasInvoke(value=>value+4,38,0)!=42 || api.Calls.Compute(value=>value+4,38,0)!=42) return 1;
                        api.NotifyCallback(value=>value+4,38,0); if(api.Last()!=42) return 2;
                        var marker=new ApplicationException("original managed callback");
                        try {api.InvokeCallback(value=>throw marker,1,0);return 3;} catch(ApplicationException failure) {if(!ReferenceEquals(failure,marker)) return 4;}
                        try {api.NotifyCallback(value=>throw marker,1,0);return 5;} catch(ApplicationException failure) {if(!ReferenceEquals(failure,marker)) return 6;}
                        if(api.Last()!=-7) return 7;
                        try {api.InvokeCallback(value=>throw marker,1,1);return 8;} catch(ApplicationException failure) {if(!ReferenceEquals(failure,marker)) return 9;}
                        try {api.InvokeCallback(value=>value,1,1);return 10;} catch({{runtime}}.NativeCallException) {}
                        using(var widget=api.CreateWidget(value=>value+4,38)) {if(widget.Get()!=42 || api.Live()!=1) return 11;}
                        if(api.Live()!=0) return 12;
                        try {using var invalid=api.CreateWidget(value=>throw marker,0);return 13;} catch(ApplicationException failure) {if(!ReferenceEquals(failure,marker)) return 14;}
                        if(api.Live()!=0) return 15;
                        try {using var invalid=api.CreateWidget(value=>value,-1);return 16;} catch({{runtime}}.NativeCallException) {}
                        if(api.Live()!=0) return 17;
                        {{shared}}
                        using var observer={{factory}};
                        using(var widget=api.AliasCreateWidget(value=>{api.Dispose();return value+4;},38)) {
                            if(observer.Live()!=1) return 18;
                            widget.Dispose(); if(widget.releaseFailure is not null || observer.Live()!=0) return 19;
                        }
                        return 42;
                    }
                }
                """, "Probe", root, sourceRuntime));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static string WorkerRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(directory!.FullName, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
