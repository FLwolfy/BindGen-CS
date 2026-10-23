using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Core;
using BGCS.Core.Inputs;
using BGCS.Core.Interop;
using BGCS.Core.Managed;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeOwnedInstanceCallbackTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable, false)]
    [InlineData(BindingImportMode.DllImport, false)]
    [InlineData(BindingImportMode.LibraryImport, false)]
    [InlineData(BindingImportMode.FunctionTable, true)]
    [InlineData(BindingImportMode.DllImport, true)]
    [InlineData(BindingImportMode.LibraryImport, true)]
    public async Task OwnedMemberCallbacksRetainTheWholeNativeFrameAndCleanEveryOwnedResult(
        BindingImportMode mode,
        bool sourceRuntime
    ) {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-owned-instance-callback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "widget.hpp"), Header);
            string[] functions = ["Widget::Widget(int)", "Widget::~Widget", "Widget::Value", "Widget::Clone", "Widget::CallPlain", "Widget::CallState",
                "Widget::Spawn", "Widget::Touch", "Widget::Composed", "Widget::CloneFill", "Widget::SpawnFill", "Live", "Last", "Destroyed"];
            CallbackParameterConfiguration Callback(
                string member,
                bool userData,
                string? fallback
            ) => new()
            {
                function = "Widget::" + member,
                parameter = "callback",
                lifetime = NativeLifetimeKind.Call,
                retained = false,
                threading = NativeCallbackThreading.CallerThread,
                userDataParameter = userData ? "userdata" : null,
                callableUserDataIndex = userData ? 1 : null,
                failureReturn = fallback
            };
            BindingConfiguration configuration = new()
            {
                schemaVersion = 2,
                library = new() { id = "owned_callbacks", nativeName = "owned_callbacks_fixture" },
                source = new()
                {
                    language = SourceLanguage.Cpp,
                    headers = ["widget.hpp"],
                    parseMacros = false,
                    includeTransitivelyReferencedHeaders = false,
                    selection = new() { includeDeclarations = functions.ToImmutableArray() }
                },
                api = new()
                {
                    @namespace = "Generated.OwnedCallbacks",
                    name = "OwnedApi",
                    importMode = mode,
                    style = sourceRuntime ? ManagedApiStyle.Static : ManagedApiStyle.Instance,
                    names = ImmutableDictionary<string, string>.Empty.Add("Widget::Widget(int)", "CreateWidget")
                        .Add("Widget::Value", "Value").Add("Widget::Clone", "Clone").Add("Widget::CallPlain", "CallPlain")
                        .Add("Widget::CallState", "CallState").Add("Widget::Spawn", "Spawn").Add("Widget::Touch", "Touch")
                        .Add("Widget::CloneFill", "CloneFill").Add("Widget::SpawnFill", "SpawnFill")
                        .Add("Widget::Composed", "Composed").Add("Live", "Live").Add("Last", "Last").Add("Destroyed", "Destroyed"),
                    functionAliases = [new("Widget::CallState", "AliasCallState")],
                    functionContainers = [new("Calls", [new("Widget::CallState", "Invoke")])]
                },
                semantics = new()
                {
                    ownedResults = new[] { "Widget::Widget(int)", "Widget::Clone", "Widget::Spawn", "Widget::CloneFill", "Widget::SpawnFill" }
                        .Select(function => new OwnedResultConfiguration { function = function, releaseThreadDomain = "any" }).ToImmutableArray(),
                    callbackParameters = [Callback("CallPlain", false, "-7"), Callback("CallState", true, "-9"),
                        Callback("Spawn", true, "-11"), Callback("Touch", true, null), Callback("Composed", true, "-13"), Callback("SpawnFill", true, "-15")],
                    valueParameters = [new() { function = "Widget::Composed", parameter = "input", direction = NativeParameterDirection.Input,
                        lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false },
                        new() { function = "Widget::Composed", parameter = "same", direction = NativeParameterDirection.InputOutput,
                            lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false }],
                    bufferParameters = [new() { function = "Widget::Composed", parameter = "buffer", capacityParameter = "count",
                        direction = NativeParameterDirection.InputOutput, lengthUnit = NativeLengthUnit.Elements,
                        lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false },
                        new() { function = "Widget::CloneFill", parameter = "buffer", capacityParameter = "capacity", writtenCountParameter = "written",
                            direction = NativeParameterDirection.Output, lengthUnit = NativeLengthUnit.Elements,
                            lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false },
                        new() { function = "Widget::SpawnFill", parameter = "buffer", capacityParameter = "capacity", writtenCountParameter = "written",
                            direction = NativeParameterDirection.Output, lengthUnit = NativeLengthUnit.Elements,
                            lifetime = NativeLifetimeKind.Call, retainsPointer = false, allowsNull = false }]
                },
                runtime = new() { mode = sourceRuntime ? "source" : "package" },
                output = new() { layout = sourceRuntime ? "singleFile" : "split" },
                cache = new() { enabled = false }
            };
            await using BindingHost host = BindingGenerator.CreateBuilder().UseWorkerDirectory(WorkerRoot()).Build();
            GenerationResult built = await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration, root));
            Assert.True(built.success, built.GetDiagnosticSummary());
            TargetInspection target = Assert.Single(built.targetModels);
            Assert.Equal(6, target.interop.functions.SelectMany(function => function.parameters).Count(parameter => parameter.callback is not null));
            string targetRoot = Path.Combine(built.outputRoot, target.targetId);
            string binary = Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            string context = sourceRuntime ? "OwnedApiContext" : "OwnedApi";
            string factory = mode == BindingImportMode.FunctionTable ? "ApiContext.Load(binary)" : "ApiContext.Create()";
            string runtime = sourceRuntime ? "Generated.OwnedCallbacks.Runtime" : "BGCS.Runtime";
            string shared = sourceRuntime ? (mode == BindingImportMode.FunctionTable ? "OwnedApi.Load(binary);" : "OwnedApi.Create();") + """
                using (var sharedWidget=OwnedApi.CreateWidget(40)) {
                    if(OwnedApi.AliasCallState(sharedWidget,v=>v+12,30,false)!=42) return 51;
                    if(OwnedApi.Calls.Invoke(sharedWidget,v=>v+12,30,false)!=42) return 52;
                }
                OwnedApi.Dispose();
                """ : "";
            Assert.Equal(42, await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot, "managed"), binary,
                configuration.library.nativeName, $$"""
                using System;
                using System.Threading;
                using System.Threading.Tasks;
                using Generated.OwnedCallbacks;
                using ApiContext=Generated.OwnedCallbacks.{{context}};
                public static class OwnedCallbackConsumer {
                    public static int Run(string binary) {
                        using var api={{factory}};
                        using var observer={{factory}};
                        if(observer.Live()!=0) return 1;
                        try {using var invalid=api.CreateWidget(-1);return 2;} catch({{runtime}}.NativeCallException) {}
                        if(observer.Live()!=0) return 3;
                        using var widget=api.CreateWidget(40);
                        using(var clone=widget.Clone(2)) {if(clone.Value()!=42 || observer.Live()!=2) return 4;}
                        if(observer.Live()!=1) return 5;
                        if(widget.CallPlain(value=>{GC.Collect();GC.WaitForPendingFinalizers();return value+2;},40,false)!=42) return 6;
                        if(widget.CallState(value=>{GC.Collect();return value+12;},30,false)!=42) return 7;
                        if(api.CallState(widget,v=>v+12,30,false)!=42 || api.AliasCallState(widget,v=>v+12,30,false)!=42
                            || api.Calls.Invoke(widget,v=>v+12,30,false)!=42) return 8;
                        int touched=0;
                        widget.Touch(value=>touched+=value,42);if(touched!=42) return 9;
                        var marker=new ApplicationException("exact owned callback failure");
                        try {widget.CallPlain(_=>throw marker,1,false);return 10;}
                        catch(ApplicationException error) {if(!ReferenceEquals(error,marker) || observer.Last()!=-7)return 11;}
                        try {widget.CallState(_=>throw marker,1,true);return 12;}
                        catch(ApplicationException error) {if(!ReferenceEquals(error,marker) || observer.Last()!=-9)return 13;}
                        try {widget.CallState(v=>v,1,true);return 14;} catch({{runtime}}.NativeCallException) {}
                        try {widget.Touch(_=>throw marker,1);return 15;}catch(ApplicationException error){if(!ReferenceEquals(error,marker))return 16;}
                        using(var result=widget.Spawn(v=>v+12,30,false)){if(result.Value()!=42 || observer.Live()!=2)return 17;}
                        int beforeFailedResult=observer.Destroyed();
                        try {using var invalid=widget.Spawn(_=>throw marker,1,false);return 18;}
                        catch(ApplicationException error){if(!ReferenceEquals(error,marker))return 19;}
                        if(observer.Live()!=1 || observer.Destroyed()<=beforeFailedResult) return 20;
                        try {using var invalid=widget.Spawn(_=>throw marker,1,true);return 21;}
                        catch(ApplicationException error){if(!ReferenceEquals(error,marker))return 22;}
                        try {using var invalid=widget.Spawn(v=>v,1,true);return 23;}catch({{runtime}}.NativeCallException) {}
                        if(observer.Live()!=1) return 24;
                        int[] data={20,10};
                        if(widget.Composed(_=>22,in data[0],ref data[0],data)!=84 || data[0]!=84 || data[1]!=94)return 25;
                        int written;
                        using(var filled=widget.CloneFill(data,out written,0)){if(written!=2 || filled.Value()!=42 || observer.Live()!=2)return 39;}
                        try{using var invalid=widget.CloneFill(data,out written,1);return 40;}catch(InvalidOperationException){}
                        if(observer.Live()!=1 || written!=0)return 41;
                        using(var filled=widget.SpawnFill(v=>v+12,30,data,out written,0)){if(written!=2 || filled.Value()!=42 || observer.Live()!=2)return 142;}
                        try{using var invalid=widget.SpawnFill(v=>v+12,30,data,out written,1);return 43;}catch(InvalidOperationException){}
                        if(observer.Live()!=1 || written!=0)return 44;
                        try{using var invalid=widget.SpawnFill(_=>throw marker,1,data,out written,1);return 45;}
                        catch(ApplicationException error){if(!ReferenceEquals(error,marker))return 46;}
                        if(observer.Live()!=1 || written!=0)return 47;
                        try {observer.CallState(widget,v=>v,1,false);return 26;}catch(ArgumentException){}
                        widget.Dispose();if(observer.Live()!=0 || widget.releaseFailure is not null)return 27;
                        int calls=0;try{widget.CallState(v=>{calls++;return v;},1,false);return 28;}catch(ObjectDisposedException){}
                        if(calls!=0) return 29;
                        using var self=api.CreateWidget(40);
                        if(self.CallState(v=>{self.Dispose();if(observer.Live()!=1)throw new Exception("receiver released inside native frame");return v+2;},40,false)!=42)return 30;
                        if(observer.Live()!=0 || self.releaseFailure is not null)return 31;
                        using var concurrent=api.CreateWidget(40);
                        using var entered=new ManualResetEventSlim();using var resume=new ManualResetEventSlim();
                        Task<int> pending=Task.Run(()=>concurrent.CallState(v=>{entered.Set();if(!resume.Wait(TimeSpan.FromSeconds(10)))throw new Exception("native frame gate timeout");return v+2;},40,false));
                        if(!entered.Wait(TimeSpan.FromSeconds(10))){resume.Set();pending.GetAwaiter().GetResult();return 32;}
                        concurrent.Dispose();if(observer.Live()!=1){resume.Set();pending.GetAwaiter().GetResult();return 33;}
                        resume.Set();if(pending.GetAwaiter().GetResult()!=42 || observer.Live()!=0 || concurrent.releaseFailure is not null)return 34;
                        {{shared}}
                        using var closing=api.CreateWidget(40);
                        using(var result=closing.Spawn(v=>{api.Dispose();return v+2;},40,false)) {
                            if(observer.Live()!=2) return 35;
                            try{closing.Value();return 36;}catch(ObjectDisposedException){}
                            result.Dispose();if(observer.Live()!=1 || result.releaseFailure is not null)return 37;
                        }
                        closing.Dispose();if(observer.Live()!=0 || closing.releaseFailure is not null)return 38;
                        return 42;
                    }
                }
                """, "OwnedCallbackConsumer", root, sourceRuntime));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private const string Header = """
        #pragma once
        #include <atomic>
        #include <stdexcept>
        using PlainHandler=int(*)(int value);
        using StateHandler=int(*)(int value,void* userdata);
        using VoidHandler=void(*)(int value,void* userdata);
        namespace details {inline std::atomic<int> live{0},last{0},destroyed{0};}
        class Widget {
            int m_value;
        public:
            explicit Widget(int value):m_value(value){if(value<0)throw std::runtime_error("negative constructor");++details::live;}
            Widget(const Widget& other):m_value(other.m_value){++details::live;}
            Widget(Widget&& other) noexcept:m_value(other.m_value){++details::live;}
            ~Widget() noexcept {--details::live;++details::destroyed;}
            int Value() const {return m_value;}
            Widget Clone(int delta){return Widget(m_value+delta);}
            int CallPlain(PlainHandler callback,int value,bool fail){int result=callback(value);details::last=result;++m_value;if(fail)throw std::runtime_error("native after plain callback");return result;}
            int CallState(StateHandler callback,int value,bool fail,void* userdata){int result=callback(value,userdata);details::last=result;++m_value;if(fail)throw std::runtime_error("native after callback");return result;}
            Widget Spawn(StateHandler callback,int value,bool fail,void* userdata){int result=callback(value,userdata);details::last=result;++m_value;if(fail)throw std::runtime_error("native before result");return Widget(result<0?0:result);}
            void Touch(VoidHandler callback,int value,void* userdata){callback(value,userdata);++m_value;}
            int Composed(StateHandler callback,const int* input,int* same,int* buffer,int count,void* userdata){
                if(input!=same)throw std::runtime_error("borrow identity lost");*same+=callback(0,userdata);
                for(int index=0;index<count;++index)buffer[index]+=*input;return *same;
            }
            Widget CloneFill(int* buffer,int capacity,int* written,int mode){for(int i=0;i<capacity;++i)buffer[i]=42+i;*written=mode?capacity+1:capacity;return Widget(42);}
            Widget SpawnFill(StateHandler callback,int value,int* buffer,int capacity,int* written,int mode,void* userdata){int result=callback(value,userdata);for(int i=0;i<capacity;++i)buffer[i]=result+i;*written=mode?capacity+1:capacity;return Widget(result<0?0:result);}
        };
        inline int Live(){return details::live.load();}
        inline int Last(){return details::last.load();}
        inline int Destroyed(){return details::destroyed.load();}
        """;

    private static string WorkerRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "BindGen-CS.slnx"))) root = root.Parent;
        string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        return Path.Combine(root!.FullName, "extensions", "frontends", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0");
    }
}
