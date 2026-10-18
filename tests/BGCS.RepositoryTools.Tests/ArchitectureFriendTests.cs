using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BGCS.RepositoryTools.Tests;

public sealed class ArchitectureFriendTests
{
    [Theory]
    [InlineData("source")]
    [InlineData("alias")]
    [InlineData("conditional")]
    [InlineData("project-item")]
    [InlineData("generated-attribute")]
    public void ExactlyRegisteredFrontendTestFriendIsAccepted(string declaration)
    {
        using Repository repository = new();
        repository.Register();
        repository.Declare(declaration, "BGCS.Frontend.Clang.Tests");
        Assert.Equal(0, repository.Validate());
    }

    [Theory]
    [InlineData("missing-registry")]
    [InlineData("production-target")]
    [InlineData("test-spoofs-production")]
    [InlineData("wrong-test-assembly")]
    [InlineData("packable-test")]
    [InlineData("missing-owner-reference")]
    [InlineData("unregistered-source")]
    [InlineData("unregistered-assembly")]
    [InlineData("dynamic-attribute")]
    [InlineData("duplicate")]
    [InlineData("conditional-production")]
    [InlineData("stale-registry")]
    [InlineData("path-escape")]
    [InlineData("shared-props")]
    public void FriendPolicyCannotBeBypassedByProductionOrUnregisteredDeclarations(string invalid)
    {
        using Repository repository = new();
        if (invalid != "missing-registry") repository.Register(invalid);
        if (invalid != "stale-registry")
        {
            string target = invalid is "production-target" or "conditional-production" ? "BGCS" : invalid == "unregistered-assembly" ? "Other.Tests" : "BGCS.Frontend.Clang.Tests";
            repository.Declare(invalid == "conditional-production" ? "conditional" : invalid == "dynamic-attribute" ? "dynamic" : "source", target,
                invalid == "unregistered-source" ? "BGCS" : "BGCS.Frontend.Clang");
            if (invalid == "duplicate") repository.Declare("project-item", target);
            if (invalid == "shared-props") File.WriteAllText(Path.Combine(repository.root,"Directory.Build.props"),"<Project><ItemGroup><InternalsVisibleTo Include='BGCS'/></ItemGroup></Project>");
        }
        Assert.Equal(1, repository.Validate());
    }

    [Fact]
    public void AStringContainingAnAttributeDoesNotGrantAFalseFriend()
    {
        using Repository repository = new();
        repository.WriteSource("BGCS.Frontend.Clang", "public class Literal { public const string Value = \"[assembly: InternalsVisibleTo(\\\"BGCS\\\")]\"; }");
        Assert.Equal(0, repository.Validate());
    }

    private sealed class Repository : IDisposable
    {
        private const string Owner = "src/BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj";
        private const string Friend = "tests/BGCS.Frontend.Clang.Tests/BGCS.Frontend.Clang.Tests.csproj";
        internal string root { get; } = Path.Combine(Path.GetTempPath(), "bgcs-friend-policy-" + Guid.NewGuid().ToString("N"));
        internal Repository()
        {
            DirectoryInfo? actual = new(AppContext.BaseDirectory);
            while (actual is not null && !File.Exists(Path.Combine(actual.FullName, "eng", "architecture", "project-dependencies.json"))) actual = actual.Parent;
            Directory.CreateDirectory(Path.Combine(root, "eng", "architecture"));
            string manifest = Path.Combine(actual!.FullName, "eng", "architecture", "project-dependencies.json");
            File.Copy(manifest, Path.Combine(root, "eng", "architecture", "project-dependencies.json"));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (JsonProperty project in document.RootElement.GetProperty("projects").EnumerateObject())
                WriteProject(Project(project.Name), project.Name is "BGCS.Tool" or "BGCS.Frontend.Clang.Worker" ? "<PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>" : "");
        }
        internal void Register(string invalid = "")
        {
            string assembly = invalid == "test-spoofs-production" ? "BGCS" : invalid == "wrong-test-assembly" ? "Wrong.Tests" : "BGCS.Frontend.Clang.Tests";
            string test = invalid == "production-target" ? "src/BGCS/BGCS.csproj" : invalid == "path-escape" ? "../outside.csproj" : Friend;
            string content = "<PropertyGroup><IsPackable>" + (invalid == "packable-test" ? "true" : "false") + "</IsPackable><AssemblyName>" + assembly + "</AssemblyName></PropertyGroup>";
            if (invalid != "missing-owner-reference") content += "<ItemGroup><ProjectReference Include='../../src/BGCS.Frontend.Clang/BGCS.Frontend.Clang.csproj'/></ItemGroup>";
            WriteProject(Friend, content);
            File.WriteAllText(Path.Combine(root, "eng", "architecture", "test-friends.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, exceptions = new[] { new { sourceProject = Owner, friendProject = test, friendAssembly = assembly, reason = "Frontend parser regression tests" } }
            }));
        }
        internal void Declare(string kind, string target, string owner = "BGCS.Frontend.Clang")
        {
            if (kind is "project-item" or "generated-attribute")
            {
                string attribute = kind == "project-item" ? "<InternalsVisibleTo Include='" + target + "'/>"
                    : "<AssemblyAttribute Include='System.Runtime.CompilerServices.InternalsVisibleToAttribute'><_Parameter1>" + target + "</_Parameter1></AssemblyAttribute>";
                WriteProject(Project(owner), "<ItemGroup>" + attribute + "</ItemGroup>");
            }
            else
            {
                string attribute = "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"" + target + "\")]";
                if (kind == "alias") attribute = "using Friend = System.Runtime.CompilerServices.InternalsVisibleToAttribute; [assembly: Friend(\"" + target + "\")]";
                if (kind == "dynamic") attribute = "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(Names.Target)] internal static class Names { internal const string Target = \"" + target + "\"; }";
                if (kind == "conditional") attribute = "#if FRIEND_ENABLED\n" + attribute + "\n#endif\n";
                WriteSource(owner, attribute);
            }
        }
        internal void WriteSource(string owner, string text) => File.WriteAllText(Path.Combine(root, Path.GetDirectoryName(Project(owner))!, "Friend.cs"), text);
        private static string Project(string owner) => (owner == "BGCS.MSBuild" ? "integrations/" : "src/") + owner + "/" + owner + ".csproj";
        private void WriteProject(string relative, string content)
        {
            string path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project Sdk='Microsoft.NET.Sdk'>" + content + "</Project>");
        }
        internal int Validate()
        {
            using StringWriter output = new(), error = new();
            return CliInvocation.Run(["validate", "architecture", root], root, output, error);
        }
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
