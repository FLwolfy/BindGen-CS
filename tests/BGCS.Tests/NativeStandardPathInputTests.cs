using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BGCS.Configuration;
using BGCS.Contracts;
using BGCS.Contracts.Artifacts;
using BGCS.Contracts.Bridge;
using BGCS.Contracts.Interop;
using BGCS.Contracts.Inputs;
using BGCS.CSharp.Planning;
using Xunit;

namespace BGCS.Tests;

public sealed class NativeStandardPathInputTests
{
    [Theory]
    [InlineData(BindingImportMode.FunctionTable,false)]
    [InlineData(BindingImportMode.DllImport,false)]
    [InlineData(BindingImportMode.LibraryImport,false)]
    [InlineData(BindingImportMode.FunctionTable,true)]
    [InlineData(BindingImportMode.DllImport,true)]
    [InlineData(BindingImportMode.LibraryImport,true)]
    public async Task StandardFilesystemPathCopiesCountedUtf8WithoutUsingTheSystemCodePage(BindingImportMode mode,bool sourceRuntime)
    {
        string root=Path.Combine(Path.GetTempPath(),"bgcs-standard-path-input-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root,"paths.hpp"),Header);
            BindingConfiguration configuration=new()
            {
                schemaVersion=1,library=new(){id="paths",nativeName="path_input_fixture"},
                source=new(){language=SourceLanguage.Cpp,headers=["paths.hpp"],parseMacros=false,includeTransitivelyReferencedHeaders=false,
                    selection=new(){includeDeclarations=["paths::CheckValue","paths::CheckRef","paths::CountUnits"]}},
                api=new(){@namespace="Generated.PathInput",name="PathApi",importMode=mode,
                    style=sourceRuntime?BGCS.Contracts.Managed.ManagedApiStyle.Static:BGCS.Contracts.Managed.ManagedApiStyle.Instance,nestGeneratedTypesInApi=sourceRuntime},
                cpp=new(){sequenceInputAdapters=[Request("paths::CheckValue"),Request("paths::CheckRef"),Request("paths::CountUnits")]},
                runtime=new(){mode=sourceRuntime?"source":"package"},output=new(){layout=sourceRuntime?"singleFile":"split"},
                cache=new(){enabled=false}
            };
            await using BindingHost host=BindingHost.CreateBuilder().AddBuiltIns().WithWorkerAssetRoot(WorkerRoot()).Build();
            GenerationResult built=await NativeConsumerHarness.GenerateAndCompileAsync(host, new(configuration,root));
            Assert.True(built.success,built.GetDiagnosticSummary());
            var target=Assert.Single(built.targetModels);
            CppBridgeModule bridge=target.bridge!;
            Assert.Equal(3,bridge.exports.Count(export=>export.inputAdapterOf is not null));
            foreach(BridgeExport adapter in bridge.exports.Where(export=>export.inputAdapterOf is not null))
            {
                Assert.Contains(bridge.exports,export=>export.id==adapter.inputAdapterOf);
                var text=target.interop.functions.Single(function=>function.id==adapter.id).parameters[0].text!;
                Assert.Equal(NativeTextEncoding.Utf8,text.encoding);
                Assert.Equal(NativeLengthUnit.CodeUnits,text.lengthUnit);
                Assert.False(text.zeroTerminated);
                Assert.NotNull(text.lengthParameter);
            }
            string targetRoot=Path.Combine(built.outputRoot,target.targetId);
            string binary=Assert.Single(Directory.GetFiles(NativeConsumerHarness.NativeDirectory(targetRoot)));
            CSharpEmissionPlan plan=new CSharpEmissionPlanner().Plan(target.managed!,new(configuration.library.nativeName,new NoWrites())
                {importMode=mode,runtimeNamespace=sourceRuntime?"Generated.PathInput.Runtime":"BGCS.Runtime"});
            string context=sourceRuntime?"PathApiContext":"PathApi";
            string factory=mode==BindingImportMode.FunctionTable?"ApiContext.Load(binary)":"ApiContext.Create()";
            Assert.Equal(42,await NativeConsumerHarness.CompileAndInvokeAsync(Path.Combine(targetRoot,"managed"),binary,configuration.library.nativeName,$$"""
                using System;
                using System.Text;
                using Generated.PathInput;
                using ApiContext=Generated.PathInput.{{context}};
                public static unsafe class PathInputConsumer{
                    public static int Run(string binary){using var api={{factory}};{{Consumer(bridge,plan)}}return 42;}
                }
                ""","PathInputConsumer",root,sourceRuntime));
        }
        finally{Directory.Delete(root,recursive:true);}
    }

    private static string Consumer(CppBridgeModule bridge,CSharpEmissionPlan plan)
    {
        StringBuilder source=new();
        foreach(string name in new[]{"CheckValue","CheckRef","CountUnits"})
        {
            BridgeExport export=Assert.Single(bridge.exports,item=>item.inputAdapterOf is not null&&bridge.source.GetDeclaration(item.operation.declaration!.Value).qualifiedName=="paths::"+name);
            CSharpMethodPlan method=plan.methods.Single(candidate=>candidate.nativeExport==export.id);
            Assert.Contains(plan.statusMethods,status=>plan.methods[status.methodIndex].nativeExport==export.id);
            Assert.Single(method.stringInputs);
            if(name!="CountUnits")source.Append("if(api.").Append(method.name).AppendLine("(\"dir\\\\😀.txt\")!=42)throw new InvalidOperationException(\"counted managed UTF8 path\");");
            else source.Append("if(api.").Append(method.name).Append("(\"A\\0B\")!=3 || api.").Append(method.name).Append("(null)!=0 || api.")
                .Append(method.name).AppendLine("(\"\")!=0)throw new InvalidOperationException(\"path embedded zero or empty count\");");
            string Raw(params string[] arguments)=>"api.Raw."+method.name+"("+string.Join(',',arguments.Select((argument,index)=>"("+method.parameterTypes[index]+")("+argument+")"))+ ")";
            CSharpMethodPlan release=plan.methods.Single(candidate=>candidate.exportName==bridge.errorReleaseExport);
            string cleanup="api.Raw."+release.name+"(("+release.parameterTypes[0]+")error);error=0;";
            source.AppendLine("{byte[] units=Encoding.UTF8.GetBytes(\"dir\\\\😀.txt\");nint error=0;int result=0;fixed(byte* data=units){");
            source.Append("if(").Append(Raw("data","(ulong)units.Length","&result","&error")).Append("!=0 || error!=0 || result!=")
                .Append(name=="CountUnits"?"units.Length":"42").AppendLine(")throw new InvalidOperationException(\"raw UTF8 path identity\");}");
            source.Append("if(").Append(Raw("null","1UL","&result","&error")).AppendLine("==0 || error==0)throw new InvalidOperationException(\"path null/count\");").AppendLine(cleanup);
            source.AppendLine("byte unit=65;");
            source.Append("if(").Append(Raw("&unit","1048577UL","&result","&error")).AppendLine("==0 || error==0)throw new InvalidOperationException(\"path copy cap\");").AppendLine(cleanup).AppendLine("}");
        }
        return source.ToString();
    }

    private static SequenceInputAdapterRequest Request(string function)=>new(function,[new("value"){retainsArgument=false,lifetime=NativeLifetimeKind.Call}]);
    private static string WorkerRoot()
    {
        DirectoryInfo? root=new(AppContext.BaseDirectory);
        while(root is not null&&!File.Exists(Path.Combine(root.FullName,"BindGen-CS.slnx")))root=root.Parent;
        string configuration=AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar+"Release"+Path.DirectorySeparatorChar,StringComparison.Ordinal)?"Release":"Debug";
        return Path.Combine(root!.FullName,"src","BGCS.Frontend.Clang.Worker","bin",configuration,"net10.0");
    }
    private sealed class NoWrites:IArtifactSink{public string WriteText(string relativePath,string content)=>throw new InvalidOperationException("Planning cannot write.");}
    private const string Header="""
        #include <filesystem>
        #include <string>
        namespace paths {
        inline int CheckValue(std::filesystem::path value){return value.u8string()==std::u8string(u8"dir\\\U0001F600.txt")?42:-1;}
        inline int CheckRef(const std::filesystem::path& value){return CheckValue(value);}
        inline int CountUnits(std::filesystem::path value){return static_cast<int>(value.u8string().size());}
        }
        """;
}
