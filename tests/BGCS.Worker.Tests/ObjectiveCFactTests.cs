using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Declarations;
using BGCS.Analysis.Interop;
using BGCS.Contracts.Common;
using BGCS.Contracts.Inputs;
using BGCS.Contracts.Native;
using BGCS.Contracts.Requests;
using BGCS.Contracts.Targets;
using BGCS.Infrastructure.Workers;
using Xunit;

namespace BGCS.Worker.Tests;

public sealed class ObjectiveCFactTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealWorkerDetachesObjectiveCSelectorsPropertiesRelationshipsAndGenericTypes(bool retainBodies)
    {
        string root = Path.Combine(Path.GetTempPath(), "bgcs-worker-objc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string repository = Repository();
            string header = Path.Combine(root, "facts.h");
            File.Copy(Path.Combine(repository, "tests", "fixtures", "native", "ObjectiveCFacts.h"), header);
            string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
            FrontendWorkerClient client = new(Path.Combine(repository, "src", "BGCS.Frontend.Clang.Worker", "bin", configuration, "net10.0", "BGCS.Frontend.Clang.Worker.dll"));
            NativeTargetDescriptor target = new(new("objc-facts-arm64"), "apple", "arm64", "darwin", "arm64-apple-darwin", new());
            FrontendResult parsed = await client.ParseAsync(new(target, SourceLanguage.ObjC, [header])
            { pathBase = root, parseMacros = false, arguments = ["-fblocks", "-fobjc-runtime=macosx-10.12", "-fobjc-weak"], retainFunctionBodies = retainBodies }, CancellationToken.None);
            NativeModule module = parsed.module;
            Assert.DoesNotContain(module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Empty(new NativeModuleVerifier().Validate(module));
            Assert.Equal(SourceLanguage.ObjC, module.language);
            Assert.All(module.declarations.Where(declaration=>declaration.objectiveCMethod is not null),method=>
            {
                Assert.Equal("Objective-C",method.linkage); Assert.Null(method.callingConvention);
                Assert.Equal(NativeCallableAddressability.Unknown,method.addressability); Assert.Null(method.addressabilityEvidence);
                // Clang reports external declaration linkage for selectors; the
                // separate dispatch/addressability facts must still forbid C imports.
                Assert.Equal(NativeLinkageKind.External,method.nativeLinkage);
            });
            NativeDeclaration bag = Container("Bag", NativeObjectiveCContainerKind.Interface);
            NativeDeclaration rootClass = Container("Root", NativeObjectiveCContainerKind.Interface);
            NativeDeclaration combined = Container("CombinedProtocol", NativeObjectiveCContainerKind.Protocol);
            Assert.Equal(rootClass.id, bag.objectiveCContainer!.baseInterface);
            Assert.Equal(combined.id, Assert.Single(bag.objectiveCContainer.protocols));
            Assert.Equal(new[] { "RootProtocol", "ExtraProtocol" }, combined.objectiveCContainer!.protocols.Select(id => module.GetDeclaration(id).name));
            Assert.Equal(2,bag.objectiveCContainer.genericParameters.Length);
            StableId parameter = bag.objectiveCContainer.genericParameters[0], secondParameter=bag.objectiveCContainer.genericParameters[1];
            Assert.NotEqual(parameter,secondParameter);
            Assert.Equal(NativeTypeCategory.Template, module.GetType(parameter).category);
            Assert.Equal(NativeTypeCategory.Template, module.GetType(secondParameter).category);
            Assert.Empty(bag.baseClasses); Assert.Empty(bag.templateParameters); Assert.Empty(bag.templateArguments);
            NativeDeclaration item = Method(bag, "itemAt:");
            Assert.Equal(parameter, item.returnType);
            Assert.Equal(secondParameter,Method(bag,"secondAt:").returnType);
            NativeDeclaration setter = Method(bag, "setItem:at:");
            Assert.Equal(2, setter.children.Length);
            Assert.Equal(parameter, module.GetDeclaration(setter.children[0]).type);
            Assert.Equal("Int", Resolve(module.GetDeclaration(setter.children[1]).type!.Value).primitiveKind);
            NativeDeclaration make = Method(rootClass, "make");
            Assert.Equal(NativeObjectiveCDispatchKind.Class, make.objectiveCMethod!.dispatch);
            NativeType result = Resolve(make.returnType!.Value);
            Assert.Equal(NativeTypeCategory.Pointer, result.category);
            Assert.Equal(rootClass.id, Resolve(result.elementType!.Value).declaration);
            Assert.Equal(NativeObjectiveCDispatchKind.Instance, Method(rootClass, "measure").objectiveCMethod!.dispatch);
            Assert.Equal("Float", Resolve(Method(rootClass, "measure").returnType!.Value).primitiveKind);
            NativeDeclaration weight = Property("weight");
            Assert.Equal("Float", Resolve(weight.type!.Value).primitiveKind);
            Assert.True(weight.objectiveCProperty!.attributes!.Value.HasFlag(NativeObjectiveCPropertyAttributes.ReadOnly));
            Assert.Null(weight.objectiveCProperty.setter);
            NativeDeclaration count = Property("count");
            Assert.Equal("Int", Resolve(count.type!.Value).primitiveKind);
            Assert.Equal("count", module.GetDeclaration(count.objectiveCProperty!.getter!.Value).objectiveCMethod!.selector);
            Assert.Equal("setCount:", module.GetDeclaration(count.objectiveCProperty.setter!.Value).objectiveCMethod!.selector);
            Assert.True(Property("explicitCount").objectiveCProperty!.attributes!.Value.HasFlag(NativeObjectiveCPropertyAttributes.Assign | NativeObjectiveCPropertyAttributes.ReadWrite));
            NativeDeclaration name = Property("name");
            Assert.Equal("displayName", name.objectiveCProperty!.getterSelector);
            Assert.Equal("setDisplayName:", name.objectiveCProperty.setterSelector);
            Assert.True(name.objectiveCProperty.attributes!.Value.HasFlag(NativeObjectiveCPropertyAttributes.Copy | NativeObjectiveCPropertyAttributes.Nonatomic | NativeObjectiveCPropertyAttributes.Getter | NativeObjectiveCPropertyAttributes.Setter));
            foreach ((string property, NativeObjectiveCPropertyAttributes attribute) in new[]
            {
                ("weakTarget", NativeObjectiveCPropertyAttributes.Weak), ("strongTarget", NativeObjectiveCPropertyAttributes.Strong | NativeObjectiveCPropertyAttributes.Atomic),
                ("unsafeTarget", NativeObjectiveCPropertyAttributes.UnsafeUnretained), ("retainedTarget", NativeObjectiveCPropertyAttributes.Retain)
            }) Assert.True(Property(property).objectiveCProperty!.attributes!.Value.HasFlag(attribute));
            NativeDeclaration shared = Property("shared");
            Assert.True(shared.objectiveCProperty!.attributes!.Value.HasFlag(NativeObjectiveCPropertyAttributes.Class));
            Assert.Equal(NativeObjectiveCDispatchKind.Class, module.GetDeclaration(shared.objectiveCProperty.getter!.Value).objectiveCMethod!.dispatch);
            NativeDeclaration category = Container("Bag", NativeObjectiveCContainerKind.Category);
            Assert.Equal("Extras", category.objectiveCContainer!.categoryName);
            Assert.Equal(bag.id, category.objectiveCContainer.categoryTarget);
            Assert.Equal(NativeObjectiveCDispatchKind.Instance, Method(category, "extra").objectiveCMethod!.dispatch);
            Assert.Contains(module.types, type => type.category == NativeTypeCategory.Alias && type.spelling.Contains("Element", StringComparison.Ordinal) && Resolve(type.id).id == parameter);
            Assert.Equal(bag.id,Assert.Single(module.declarations,declaration=>declaration.kind=="Typedef"&&declaration.name=="Element").parent);
            NativeType block = Assert.Single(module.types, type => type.category == NativeTypeCategory.Block);
            Assert.Equal("Float", Resolve(block.returnType!.Value).primitiveKind);
            Assert.Equal(2, block.parameterTypes.Length);
            Assert.Equal("Int", Resolve(block.parameterTypes[0]).primitiveKind);
            Assert.Equal(NativeTypeCategory.Pointer, Resolve(block.parameterTypes[1]).category);
            Assert.All(module.declarations, declaration => Assert.False(Path.IsPathRooted(declaration.source.file)));
            string json = JsonSerializer.Serialize(module, WorkerProtocol.jsonOptions);
            NativeModule roundTrip = JsonSerializer.Deserialize<NativeModule>(json, WorkerProtocol.jsonOptions)!;
            Assert.Equal(json, JsonSerializer.Serialize(roundTrip, WorkerProtocol.jsonOptions));
            Assert.Empty(new NativeModuleVerifier().Validate(roundTrip));
            Assert.Contains(new CDeclarationAnalyzer().Analyze(module, "objc").diagnostics, diagnostic => diagnostic.isFailure);

            NativeDeclaration Container(string name, NativeObjectiveCContainerKind kind) => Assert.Single(module.declarations,
                declaration => declaration.name == name && declaration.objectiveCContainer?.kind == kind);
            NativeDeclaration Method(NativeDeclaration owner, string selector) => Assert.Single(module.declarations,
                declaration => declaration.parent == owner.id && declaration.objectiveCMethod?.selector == selector);
            NativeDeclaration Property(string name) => Assert.Single(module.declarations, declaration => declaration.parent == bag.id && declaration.kind == "Property" && declaration.name == name);
            NativeType Resolve(StableId id)
            {
                NativeType type = module.GetType(id);
                while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified) type = module.GetType(type.elementType!.Value);
                return type;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Repository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BindGen-CS.slnx"))) directory = directory.Parent;
        return directory!.FullName;
    }
}
