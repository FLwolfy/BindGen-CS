using System.Reflection;
using System.Runtime.Loader;
using System.Text;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: BGCS.ApiSnapshot <assembly> <output>");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[1]);
using SnapshotLoadContext context = new(assemblyPath);
Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
List<string> lines = [];
foreach (Type type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
{
    string kind = type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class";
    lines.Add($"{kind} {type.FullName}");
    BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    foreach (FieldInfo field in type.GetFields(flags).OrderBy(field => field.Name, StringComparer.Ordinal))
        lines.Add($"  field {FormatType(field.FieldType)} {field.Name}");
    foreach (ConstructorInfo constructor in type.GetConstructors(flags).OrderBy(constructor => constructor.ToString(), StringComparer.Ordinal))
        lines.Add($"  ctor({FormatParameters(constructor.GetParameters())})");
    foreach (PropertyInfo property in type.GetProperties(flags).OrderBy(property => property.Name, StringComparer.Ordinal))
    {
        List<string> accessors = [];
        if (property.GetMethod?.IsPublic == true) accessors.Add("get");
        if (property.SetMethod?.IsPublic == true) accessors.Add("set");
        if (accessors.Count > 0)
            lines.Add($"  property {FormatType(property.PropertyType)} {property.Name} {{ {string.Join("; ", accessors)} }}");
    }
    foreach (EventInfo eventInfo in type.GetEvents(flags).OrderBy(eventInfo => eventInfo.Name, StringComparer.Ordinal))
        lines.Add($"  event {FormatType(eventInfo.EventHandlerType!)} {eventInfo.Name}");
    foreach (MethodInfo method in type.GetMethods(flags).Where(method => !method.IsSpecialName)
                 .OrderBy(method => method.ToString(), StringComparer.Ordinal))
        lines.Add($"  method {FormatType(method.ReturnType)} {method.Name}({FormatParameters(method.GetParameters())})");
}
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllLines(outputPath, lines, new UTF8Encoding(false));
return 0;

static string FormatParameters(IEnumerable<ParameterInfo> parameters) =>
    string.Join(", ", parameters.Select(parameter => $"{FormatType(parameter.ParameterType)} {parameter.Name}"));

static string FormatType(Type type)
{
    if (type.IsByRef) return FormatType(type.GetElementType()!) + "&";
    if (type.IsPointer) return FormatType(type.GetElementType()!) + "*";
    if (type.IsArray) return FormatType(type.GetElementType()!) + "[]";
    if (!type.IsGenericType) return type.FullName ?? type.Name;
    string name = type.GetGenericTypeDefinition().FullName!;
    int marker = name.IndexOf('`');
    if (marker >= 0) name = name[..marker];
    return name + "<" + string.Join(",", type.GetGenericArguments().Select(FormatType)) + ">";
}

internal sealed class SnapshotLoadContext : AssemblyLoadContext, IDisposable
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly string packagesRoot;

    public SnapshotLoadContext(string assemblyPath) : base(isCollectible: true)
    {
        resolver = new(assemblyPath);
        packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? path = resolver.ResolveAssemblyToPath(assemblyName);
        bool frameworkAssembly = assemblyName.Name is "System" or "System.Runtime" or "Microsoft.CSharp" or "netstandard" ||
            assemblyName.Name?.StartsWith("System.", StringComparison.Ordinal) == true;
        if (path == null && !frameworkAssembly && !string.IsNullOrWhiteSpace(assemblyName.Name))
        {
            string package = Path.Combine(packagesRoot, assemblyName.Name.ToLowerInvariant());
            if (Directory.Exists(package))
            {
                path = Directory.GetDirectories(package)
                    .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(directory => Directory.GetFiles(directory, assemblyName.Name + ".dll", SearchOption.AllDirectories))
                    .FirstOrDefault(candidate => candidate.Contains(Path.DirectorySeparatorChar + "lib" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            }
        }
        return path == null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path == null ? 0 : LoadUnmanagedDllFromPath(path);
    }

    public void Dispose() => Unload();
}
