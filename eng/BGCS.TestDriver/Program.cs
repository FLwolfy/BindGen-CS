using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace BGCS.TestDriver;

internal static class Program
{
    private const int C_SHARDS = 8;
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private static int Main(string[] args)
    {
        try
        {
            Dictionary<string, string> options = [];
            for (int index = 0; index < args.Length; index += 2)
                if (index + 1 >= args.Length || !options.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Expected unique named option/value pairs.");
            string assembly = Path.GetFullPath(options["--assembly"]);
            string sdk = Path.GetFullPath(options["--sdk-root"]);
            string output = Path.GetFullPath(options["--output"]);
            string? shardText = options.GetValueOrDefault("--shard");
            int? shard = shardText is null ? null : int.Parse(shardText, System.Globalization.CultureInfo.InvariantCulture);
            if (options.Keys.Any(key => key is not ("--assembly" or "--sdk-root" or "--output" or "--shard")) || shard is < 0 or >= C_SHARDS)
                throw new ArgumentException("Unknown driver option or invalid fixed eight-way partition.");
            string console = Path.Combine(sdk, "vstest.console.dll");
            if (!File.Exists(assembly) || !File.Exists(console)) throw new FileNotFoundException("The exact compiled test assembly and SDK VSTest console are required.");
            if (File.Exists(output)) throw new IOException("Structured evidence must use a fresh output path.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            CompiledMethod[] methods = ReadMethods(assembly);
            string settings = Settings(Path.GetDirectoryName(output)!, shard);
            ConsoleParameters parameters = new();
            foreach (string name in new[] { "DOTNET_ROOT", "DOTNET_HOST_PATH", "PATH", "VSTEST_CONNECTION_TIMEOUT" })
                if (Environment.GetEnvironmentVariable(name) is string value) parameters.EnvironmentVariables[name] = value;
            VsTestConsoleWrapper wrapper = new(console, parameters);
            try
            {
                wrapper.StartSession();
                Discovery discovery = new();
                using CancellationTokenSource discoveryDeadline = new(TimeSpan.FromMinutes(2));
                using CancellationTokenRegistration cancelDiscovery = discoveryDeadline.Token.Register(wrapper.CancelDiscovery);
                wrapper.DiscoverTests([assembly], settings, new TestPlatformOptions(), discovery);
                discoveryDeadline.Token.ThrowIfCancellationRequested();
                if (!discovery.completed || discovery.aborted || discovery.errors.Count != 0 || discovery.total != discovery.cases.Count
                    || discovery.total <= 0 || discovery.partial.Count != 0 || discovery.skipped.Count != 0 || discovery.missing.Count != 0
                    || discovery.fully.Count != 1 || !SamePath(discovery.fully[0], assembly))
                    throw new InvalidOperationException("Discovery did not completely enumerate exactly the requested source: " + string.Join("; ", discovery.errors));
                Case[] cases = ValidateCases(discovery.cases, methods, assembly);
                Case[] selected = shard is null ? [] : cases.Where(item => Partition(item.className) == shard.Value).ToArray();
                Run? run = null;
                if (shard is not null)
                {
                    if (selected.Length == 0) throw new InvalidOperationException("A required partition contains no discovered cases.");
                    HashSet<string> selectedIds = selected.Select(item => item.id).ToHashSet(StringComparer.Ordinal);
                    run = new();
                    using CancellationTokenSource runDeadline = new(TimeSpan.FromHours(4));
                    using CancellationTokenRegistration cancelRun = runDeadline.Token.Register(wrapper.CancelTestRun);
                    wrapper.RunTests(discovery.cases.Where(item => selectedIds.Contains(item.Id.ToString("D"))).ToArray(), settings, run);
                    runDeadline.Token.ThrowIfCancellationRequested();
                    if (!run.completed || run.aborted || run.cancelled || run.errors.Count != 0 || run.results.Count != selected.Length
                        || run.results.Any(item => item.Outcome != TestOutcome.Passed))
                        throw new InvalidOperationException("The exact-case run failed, skipped, aborted or returned an incomplete result set: " + string.Join("; ", run.errors));
                    ValidateResults(selected, run.results);
                }
                var document = new
                {
                    schemaVersion = 1, success = true, discoveryComplete = true, discoveryTotal = cases.Length,
                    assemblySha256 = Hash(assembly), sourceAssembly = Path.GetFileName(assembly), shardCount = C_SHARDS, shardIndex = shard,
                    compiledMethods = methods, discoveredCases = cases, selectedCases = selected,
                    executedCases = run?.results.Select(item => new
                    {
                        id = item.TestCase.Id.ToString("D"), fullyQualifiedName = item.TestCase.FullyQualifiedName,
                        displayName = item.TestCase.DisplayName, outcome = item.Outcome.ToString(), duration = item.Duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture)
                    }).OrderBy(item => item.id, StringComparer.Ordinal).ToArray(),
                    vstestIdentity = new { consoleSha256 = Hash(console), translationLayerSha256 = Hash(typeof(VsTestConsoleWrapper).Assembly.Location), objectModelSha256 = Hash(typeof(TestCase).Assembly.Location) }
                };
                File.WriteAllText(output, JsonSerializer.Serialize(document, s_json) + "\n", new UTF8Encoding(false));
                Console.WriteLine("Structured discovery: " + cases.Length + "; partition " + (shard?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "discovery-only") + "; executed " + (run?.results.Count ?? 0));
                return 0;
            }
            finally { wrapper.EndSession(); }
        }
        catch (Exception failure) { Console.Error.WriteLine(failure); return 1; }
    }

    private static CompiledMethod[] ReadMethods(string assembly)
    {
        string[] platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new InvalidOperationException("Framework metadata paths unavailable.")).Split(Path.PathSeparator);
        using MetadataLoadContext context = new(new PathAssemblyResolver(platform.Concat(Directory.GetFiles(Path.GetDirectoryName(assembly)!, "*.dll")).Distinct(StringComparer.OrdinalIgnoreCase)), "System.Private.CoreLib");
        Assembly metadata = context.LoadFromAssemblyPath(assembly);
        List<CompiledMethod> result = [];
        foreach (Type type in metadata.GetTypes().Where(type => type.IsClass && !type.IsAbstract))
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            {
                bool? theory = null;
                foreach (CustomAttributeData attribute in method.GetCustomAttributesData())
                    for (Type? kind = attribute.AttributeType; kind is not null; kind = kind.BaseType)
                    {
                        if (kind.FullName == "Xunit.TheoryAttribute") { theory = true; break; }
                        if (kind.FullName == "Xunit.FactAttribute") { theory ??= false; break; }
                    }
                if (theory is null) continue;
                if (type.ContainsGenericParameters) throw new InvalidOperationException("An open generic test class cannot supply a concrete certified case: " + type.FullName);
                string className = type.FullName!.Replace('+', '.');
                result.Add(new(className, className + "." + method.Name, theory.Value, method.GetParameters().Length, method.IsGenericMethodDefinition));
            }
        }
        if (result.Count == 0 || result.Select(item => item.fullyQualifiedName).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new InvalidOperationException("Compiled facts/theories are absent or have ambiguous overloaded/nested identities.");
        return result.OrderBy(item => item.fullyQualifiedName, StringComparer.Ordinal).ToArray();
    }

    private static Case[] ValidateCases(List<TestCase> actual, CompiledMethod[] methods, string assembly)
    {
        List<Case> result = [];
        foreach (TestCase test in actual)
        {
            string fqn = test.FullyQualifiedName.Replace('+', '.');
            CompiledMethod[] owners = methods.Where(method => fqn == method.fullyQualifiedName
                || method.generic && fqn.StartsWith(method.fullyQualifiedName + "<", StringComparison.Ordinal)).ToArray();
            if (test.Id == Guid.Empty || string.IsNullOrWhiteSpace(test.DisplayName) || test.ExecutorUri is null || !SamePath(test.Source, assembly) || owners.Length != 1)
                throw new InvalidOperationException("A discovered case has no unique compiled Fact/Theory method, stable identity or exact source: " + test.FullyQualifiedName);
            CompiledMethod owner = owners[0];
            if (owner.theory && owner.parameterCount > 0 && (test.DisplayName == test.FullyQualifiedName
                || !test.DisplayName.Contains('(') || !test.DisplayName.EndsWith(')')))
                throw new InvalidOperationException("A dynamic theory was not completely pre-enumerated into literal argument identities: " + test.FullyQualifiedName);
            result.Add(new(test.Id.ToString("D"), test.FullyQualifiedName, test.DisplayName, owner.className, owner.fullyQualifiedName, test.ExecutorUri.ToString()));
        }
        if (result.Select(item => item.id).Distinct(StringComparer.Ordinal).Count() != result.Count
            || result.Select(item => (item.fullyQualifiedName, item.displayName)).Distinct().Count() != result.Count
            || methods.Any(method => !result.Any(test => test.compiledMethod == method.fullyQualifiedName)))
            throw new InvalidOperationException("Discovery contains duplicate identities or omitted compiled Fact/Theory methods.");
        return result.OrderBy(item => item.id, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateResults(Case[] expected, List<TestResult> actual)
    {
        Dictionary<string, Case> map = expected.ToDictionary(item => item.id, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (TestResult result in actual)
            if (!seen.Add(result.TestCase.Id.ToString("D")) || !map.TryGetValue(result.TestCase.Id.ToString("D"), out Case? wanted)
                || result.TestCase.FullyQualifiedName != wanted.fullyQualifiedName || result.TestCase.DisplayName != wanted.displayName)
                throw new InvalidOperationException("Execution returned duplicate, unexpected or changed test argument identities.");
    }

    private static int Partition(string className) => (int)(BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(className))) % C_SHARDS);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string Settings(string directory, int? shard) => "<RunSettings><RunConfiguration><TargetFrameworkVersion>.NETCoreApp,Version=v10.0</TargetFrameworkVersion><MaxCpuCount>1</MaxCpuCount><ResultsDirectory>"
        + SecurityElement.Escape(directory) + "</ResultsDirectory></RunConfiguration><xUnit><PreEnumerateTheories>true</PreEnumerateTheories><ParallelizeTestCollections>false</ParallelizeTestCollections><MaxParallelThreads>1</MaxParallelThreads></xUnit>"
        + (shard is null ? "" : "<LoggerRunSettings><Loggers><Logger friendlyName=\"trx\" enabled=\"True\"><Configuration><LogFileName>BGCS.Tests.shard" + shard.Value + ".trx</LogFileName></Configuration></Logger></Loggers></LoggerRunSettings>") + "</RunSettings>";

    private sealed record CompiledMethod(string className, string fullyQualifiedName, bool theory, int parameterCount, bool generic);
    private sealed record Case(string id, string fullyQualifiedName, string displayName, string className, string compiledMethod, string executorUri);

    private sealed class Discovery : ITestDiscoveryEventsHandler2
    {
        internal readonly List<TestCase> cases = [];
        internal readonly List<string> errors = [];
        internal readonly List<string> fully = [], partial = [], skipped = [], missing = [];
        internal long total;
        internal bool completed, aborted;
        public void HandleDiscoveredTests(IEnumerable<TestCase>? tests) { if (tests is not null) cases.AddRange(tests); }
        public void HandleDiscoveryComplete(DiscoveryCompleteEventArgs complete, IEnumerable<TestCase>? last)
        {
            HandleDiscoveredTests(last); total = complete.TotalCount; completed = true; aborted = complete.IsAborted;
            fully.AddRange(complete.FullyDiscoveredSources ?? []); partial.AddRange(complete.PartiallyDiscoveredSources ?? []);
            skipped.AddRange(complete.SkippedDiscoveredSources ?? []); missing.AddRange(complete.NotDiscoveredSources ?? []);
        }
        public void HandleRawMessage(string raw) { }
        public void HandleLogMessage(TestMessageLevel level, string? message) { if (level == TestMessageLevel.Error) errors.Add(message ?? "VSTest reported an unspecified error."); }
    }

    private sealed class Run : ITestRunEventsHandler
    {
        internal readonly List<TestResult> results = [];
        internal readonly List<string> errors = [];
        internal bool completed, aborted, cancelled;
        public void HandleTestRunStatsChange(TestRunChangedEventArgs? changed) { if (changed?.NewTestResults is not null) results.AddRange(changed.NewTestResults); }
        public void HandleTestRunComplete(TestRunCompleteEventArgs complete, TestRunChangedEventArgs? last, ICollection<AttachmentSet>? attachments, ICollection<string>? executors)
        {
            HandleTestRunStatsChange(last); completed = true; aborted = complete.IsAborted; cancelled = complete.IsCanceled;
            if (complete.Error is not null) errors.Add(complete.Error.ToString());
        }
        public int LaunchProcessWithDebuggerAttached(TestProcessStartInfo info) => throw new NotSupportedException("CI does not attach a debugger.");
        public void HandleRawMessage(string raw) { }
        public void HandleLogMessage(TestMessageLevel level, string? message) { if (level == TestMessageLevel.Error) errors.Add(message ?? "VSTest reported an unspecified error."); else Console.WriteLine(message); }
    }
}
