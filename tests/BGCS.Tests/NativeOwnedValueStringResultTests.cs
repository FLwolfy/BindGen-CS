using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Managed;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeOwnedValueStringResultTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable,false)]
    [InlineData(BindingImportMode.DllImport,false)]
    [InlineData(BindingImportMode.LibraryImport,false)]
    [InlineData(BindingImportMode.FunctionTable,true)]
    [InlineData(BindingImportMode.DllImport,true)]
    [InlineData(BindingImportMode.LibraryImport,true)]
    public async Task OwnedMembersKeepTheirStableReceiverWhileCopyingCountedTextInputsAndResults(BindingImportMode mode,bool sourceRuntime)
    {
        string root=Path.Combine(Path.GetTempPath(),"bgcs-owned-value-text-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root,"widget.hpp"),Header);
            string[] selected=["Widget::Widget(int)","Widget::~Widget","Widget::SetName","Widget::GetName","Widget::Prefix","Widget::CheckName","Widget::LongName","Live","Observed"];
            BindingConfiguration configuration=new()
            {
                schemaVersion=1,library=new(){id="owned_text",nativeName="owned_text_fixture"},
                source=new(){language=SourceLanguage.Cpp,headers=["widget.hpp"],parseMacros=false,includeTransitivelyReferencedHeaders=false,selection=new(){includeDeclarations=selected.ToImmutableArray()}},
                api=new(){@namespace="Generated.OwnedValueText",name="OwnedTextApi",importMode=mode,maximumValueStringCodeUnits=32,
                    style=sourceRuntime?ManagedApiStyle.Static:ManagedApiStyle.Instance,nestGeneratedTypesInApi=sourceRuntime,
                    names=ImmutableDictionary<string,string>.Empty.Add("Widget::Widget(int)","CreateWidget").Add("Widget::SetName","SetName").Add("Widget::GetName","GetName")
                        .Add("Widget::Prefix","Prefix").Add("Widget::CheckName","CheckName").Add("Widget::LongName","LongName").Add("Live","Live").Add("Observed","Observed")},
                semantics=new(){ownedResults=[new(){function="Widget::Widget(int)",releaseThreadDomain="any"}]},
                cpp=new(){familyBindings=[new(){nativeType="std::string",family=StandardValueKind.String,stringEncoding=NativeTextEncoding.Utf8,evidence=new(EvidenceLevel.UserAsserted,"Widget-name-is-counted-UTF8") }],
                    sequenceInputAdapters=[Input("Widget::SetName"),Input("Widget::Prefix"),Input("Widget::CheckName")]},
                runtime=new(){mode=sourceRuntime?"source":"package"},output=new(){layout=sourceRuntime?"singleFile":"split"},cache=new(){enabled=false}
            };
            await using BindingHost host=BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult built=await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration,root));Assert.True(built.success,built.GetDiagnosticSummary());
            TargetInspection target=Assert.Single(built.targetModels);
            CSharpEmissionPlan plan=new CSharpEmissionPlanner().Plan(target.managed!,new(configuration.library.nativeName,new NoWrites())
                {importMode=mode,runtimeNamespace=sourceRuntime?"Generated.OwnedValueText.Runtime":"BGCS.Runtime",maximumValueStringCodeUnits=32});
            foreach(string member in new[]{"GetName","Prefix","LongName"})
            {
                CSharpValueStringResultPlan text=Assert.Single(plan.valueStringResults,operation=>operation.receiver is not null
                    &&target.interop.functions.Single(function=>function.id==plan.methods[operation.factory.methodIndex].nativeExport).sourceQualifiedName=="Widget::"+member);
                Assert.Equal("string",text.receiver!.signature.returnType);
                InteropFunction function=target.interop.functions.Single(function=>function.id==plan.methods[text.factory.methodIndex].nativeExport);
                Assert.Null(function.parameters[0].ownership!.retainsPointer);
            }
            BridgeExport prefix=Assert.Single(target.bridge!.exports,export=>export.inputAdapterOf is not null&&target.bridge.source.GetDeclaration(export.operation.declaration!.Value).qualifiedName=="Widget::Prefix");
            Assert.Equal(BridgeConversionKind.OwnedValue,prefix.resultConversion);Assert.NotNull(prefix.resultRelease);
            InteropFunction prefixFunction=target.interop.functions.Single(function=>function.id==prefix.id);
            Assert.Contains(prefixFunction.parameters,parameter=>parameter.text is {zeroTerminated:false,lengthUnit:NativeLengthUnit.CodeUnits,retainsPointer:false});
            string targetRoot=Path.Combine(built.outputRoot,target.targetId),binary=Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            using JsonDocument metadata=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(targetRoot,"metadata","abi.manifest.json")));
            JsonElement[] described=metadata.RootElement.GetProperty("managedValueStringResults").EnumerateArray()
                .Where(value=>value.GetProperty("receiver").ValueKind==JsonValueKind.Object).ToArray();
            Assert.Equal(new[]{"GetName","LongName","PrefixWithSequence"},described.Select(value=>value.GetProperty("receiver").GetProperty("signature").GetProperty("name").GetString()).Order(StringComparer.Ordinal));
            InteropModule describedAbi=metadata.RootElement.GetProperty("module").Deserialize<InteropModule>()!;
            foreach(JsonElement value in described)
            {
                Assert.True(value.GetProperty("requiresOwnedReceiver").GetBoolean());
                Assert.Equal("WidgetObject",value.GetProperty("receiver").GetProperty("objectName").GetString());
                int method=value.GetProperty("factory").GetProperty("methodIndex").GetInt32();
                InteropFunction native=describedAbi.functions.Single(function=>function.id==plan.methods[method].nativeExport);
                Assert.Equal(NativeOwnershipKind.Borrowed,native.parameters[0].ownership!.kind);
                Assert.Equal(NativeLifetimeKind.Call,native.parameters[0].ownership!.lifetime.kind);
                Assert.Null(native.parameters[0].ownership!.retainsPointer);
            }
            string context=sourceRuntime?"OwnedTextApiContext":"OwnedTextApi",runtime=sourceRuntime?"Generated.OwnedValueText.Runtime":"BGCS.Runtime";
            string factory=mode==BindingImportMode.FunctionTable?"ApiContext.Load(binary)":"ApiContext.Create()";
            string shared=sourceRuntime?(mode==BindingImportMode.FunctionTable?"OwnedTextApi.Load(binary);":"OwnedTextApi.Create();")+"""
                using(var sharedWidget=OwnedTextApi.CreateWidget(42)){
                    sharedWidget.SetNameWithSequence("shared\0😀");
                    if(OwnedTextApi.GetName(sharedWidget)!="shared\0😀"||OwnedTextApi.PrefixWithSequence(sharedWidget,"P\0")!="P\0shared\0😀")return 20;
                    if(sharedWidget.PrefixWithSequence("M:")!="M:shared\0😀")return 21;
                }
                OwnedTextApi.Dispose();
                """:"";
            Assert.Equal(42,await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot,"managed"),binary,configuration.library.nativeName,$$"""
                using System;
                using System.Text;
                using Generated.OwnedValueText;
                using ApiContext=Generated.OwnedValueText.{{context}};
                public static class OwnedTextConsumer{
                    public static int Run(string binary){
                        using var api={{factory}};using var observer={{factory}};
                        using var widget=api.CreateWidget(42);
                        if(observer.Live()!=1||widget.GetName()!="seed"||observer.Observed()!=42)return 1;
                        string expected="A\0😀B";widget.SetNameWithSequence(expected);
                        if(widget.GetName()!=expected||widget.CheckNameWithSequence(expected)!=42||widget.PrefixWithSequence("P\0😀")!="P\0😀"+expected)return 2;
                        if(api.GetName(widget)!=expected||api.PrefixWithSequence(widget,"context:")!="context:"+expected)return 3;
                        try{observer.GetName(widget);return 4;}catch(ArgumentException){}
                        GC.Collect();GC.WaitForPendingFinalizers();if(widget.GetName()!=expected)return 5;
                        try{widget.SetNameWithSequence("\uD800");return 6;}catch(EncoderFallbackException){}
                        if(widget.GetName()!=expected)return 7;
                        try{widget.PrefixWithSequence("throw");return 8;}catch({{runtime}}.NativeCallException){}
                        if(widget.GetName()!=expected||observer.Live()!=1)return 9;
                        try{widget.LongName();return 10;}catch(ArgumentOutOfRangeException){}
                        if(widget.GetName()!=expected)return 11;
                        api.Dispose();try{widget.GetName();return 12;}catch(ObjectDisposedException){}
                        if(observer.Live()!=1)return 13;
                        widget.Dispose();if(observer.Live()!=0||observer.Observed()!=0||widget.releaseFailure is not null)return 14;
                        try{widget.GetName();return 15;}catch(ObjectDisposedException){}
                        {{shared}}
                        if(observer.Live()!=0||observer.Observed()!=0)return 16;
                        return 42;
                    }
                }
                ""","OwnedTextConsumer",root,sourceRuntime));
        }
        finally{Directory.Delete(root,recursive:true);}
    }
    private static SequenceInputAdapterRequest Input(string function)=>new(function,[new("value"){retainsArgument=false,lifetime=NativeLifetimeKind.Call}]);
    private static string WorkerRoot()
    {
        DirectoryInfo? root=new(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"BindGen-CS.slnx")))root=root.Parent;
        string configuration=AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar+"Release"+Path.DirectorySeparatorChar,StringComparison.Ordinal)?"Release":"Debug";
        return Path.Combine(root!.FullName,"src","BGCS.Frontend.Clang.Worker","bin",configuration,"net10.0");
    }
    private sealed class NoWrites:IArtifactSink{public string WriteText(string relativePath,string content)=>throw new InvalidOperationException("Planning cannot emit artifacts.");}
    private const string Header="""
        #include <string>
        #include <utility>
        #include <atomic>
        #include <stdexcept>
        class Widget;
        inline std::atomic<int> live{0};
        inline std::atomic<const Widget*> observed{nullptr};
        class Widget {
            std::string name;
        public:
            explicit Widget(int seed):name("seed"){if(seed!=42)throw std::invalid_argument("seed");++live;}
            ~Widget()noexcept{const Widget* self=this;observed.compare_exchange_strong(self,nullptr);--live;}
            void SetName(std::string value){name=std::move(value);}
            std::string GetName()const{observed.store(this);return name;}
            std::string Prefix(std::string value)const{observed.store(this);if(value=="throw")throw std::runtime_error("prefix");return value+name;}
            int CheckName(std::string value)const{return value==name?42:-1;}
            std::string LongName()const{return std::string(33,'x');}
        };
        inline int Live(){return live.load();}
        inline int Observed(){return observed.load()!=nullptr?42:0;}
        """;
}
