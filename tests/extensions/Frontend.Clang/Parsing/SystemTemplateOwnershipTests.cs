using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BGCS.Analysis.Interop;
using BGCS.Core.Hosting;
using BGCS.Core.Inputs;
using BGCS.Core.Native;
using BGCS.Core.Requests;
using BGCS.Core.Targets;
using BGCS.Hosting.Standard;
using BGCS.Targets.Desktop;
using Xunit;

namespace BGCS.Frontend.Clang.Tests;

public sealed class SystemTemplateOwnershipTests
{
    [Theory]
    [InlineData("using value_type = int;")]
    [InlineData("typedef int value_type;")]
    public async Task SystemTemplateReferencesReuseTheOriginalUserAliasIdentity(string alias)
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bgcs-sdk-template-alias-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string sdk = Directory.CreateDirectory(Path.Combine(root, "sdk")).FullName;
            await File.WriteAllTextAsync(Path.Combine(sdk, "holder.hpp"), """
                namespace SDK {
                    template<class T> struct Holder {
                        typename T::value_type value;
                        typename T::value_type Copy() const { return value; }
                    };
                }
                """);
            string header = Path.Combine(root, "user.hpp");
            await File.WriteAllTextAsync(header, $$"""
                struct UserRecord { {{alias}} };
                #include <holder.hpp>
                static_assert(sizeof(SDK::Holder<UserRecord>) == sizeof(int));
                SDK::Holder<UserRecord>* get_holder();
                UserRecord::value_type* get_value();
                """);
            await using HostServices host = new HostServicesProvider().Create(new(root, JsonSerializer.SerializeToElement(new { })));
            Assert.True(new DesktopTargetProvider().TryResolve(new(new("host")) { language = SourceLanguage.Cpp }, out NativeTargetDescriptor? target));
            FrontendResult result = await new ClangFrontend(host).ParseAsync(new(target!, SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                languageStandard = "c++23",
                systemIncludeDirectories = [sdk],
                parseSystemIncludes = true,
                parseMacros = false
            }, CancellationToken.None);
            Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Empty(new NativeModuleVerifier().Validate(result.module));
            NativeDeclaration user = Assert.Single(result.module.declarations, declaration => declaration.qualifiedName == "UserRecord" && declaration.kind == "Class");
            NativeDeclaration declaration = Assert.Single(result.module.declarations, declaration => declaration.qualifiedName == "UserRecord::value_type" && declaration.kind == "Typedef");
            Assert.False(declaration.isSystemDeclaration);
            Assert.Equal(user.id, declaration.parent);
            Assert.Single(user.children, child => child == declaration.id);
            Assert.All(result.module.declarations, owner => Assert.Equal(owner.children.Length, owner.children.Distinct().Count()));
            Assert.Contains(result.module.declarations, declaration => declaration.isSystemDeclaration && declaration.qualifiedName.StartsWith("SDK::Holder<UserRecord>", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SystemTemplateReferencesPreserveTheSingleOriginalOwnerOfUserFieldsConstructorsAndMethods()
    {
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bgcs-sdk-template-owner-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string sdk = Directory.CreateDirectory(Path.Combine(root, "sdk")).FullName;
            await File.WriteAllTextAsync(Path.Combine(sdk, "holder.hpp"), """
                namespace SDK {
                    template<class T> struct Holder {
                        T value;
                        T Copy() const { return value; }
                    };
                }
                """);
            string header = Path.Combine(root, "user.hpp");
            await File.WriteAllTextAsync(header, """
                struct UserRecord {
                    int field;
                    explicit UserRecord(int input) : field(input) {}
                    UserRecord(const UserRecord& other) : field(other.field) {}
                    int Read() const { return field; }
                };
                #include <holder.hpp>
                static_assert(sizeof(SDK::Holder<UserRecord>) >= sizeof(UserRecord));
                SDK::Holder<UserRecord>* get_holder();
                """);
            await using HostServices host = new HostServicesProvider().Create(new(root, JsonSerializer.SerializeToElement(new { })));
            Assert.True(new DesktopTargetProvider().TryResolve(new(new("host")) { language = SourceLanguage.Cpp }, out NativeTargetDescriptor? target));
            FrontendResult result = await new ClangFrontend(host).ParseAsync(new(target!, SourceLanguage.Cpp, [header])
            {
                pathBase = root,
                languageStandard = "c++23",
                systemIncludeDirectories = [sdk],
                parseSystemIncludes = true,
                parseMacros = false
            }, CancellationToken.None);
            Assert.DoesNotContain(result.module.diagnostics, diagnostic => diagnostic.isFailure);
            Assert.Empty(new NativeModuleVerifier().Validate(result.module));
            NativeDeclaration user = Assert.Single(result.module.declarations, declaration => declaration.qualifiedName == "UserRecord" && declaration.kind == "Class");
            Assert.False(user.isSystemDeclaration);
            Assert.Equal(user.children.Length, user.children.Distinct().Count());
            NativeDeclaration[] children = user.children.Select(result.module.GetDeclaration).ToArray();
            Assert.All(children, child => Assert.Equal(user.id, child.parent));
            Assert.Single(children, child => child.kind == "Field" && child.name == "field");
            Assert.Contains(children, child => child.kind == "Function" && child.name == "Read");
            Assert.True(children.Count(child => child.kind == "Function" && child.role == "Constructor") >= 2);
            Assert.Contains(result.module.declarations, declaration => declaration.isSystemDeclaration && declaration.qualifiedName.StartsWith("SDK::Holder<UserRecord>", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
