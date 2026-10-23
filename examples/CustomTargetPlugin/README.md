# A private target and SDK

This plugin maps the private target ID `example-private-host` to an existing,
explicitly declared compiler target and ABI. Its only project dependency is
`BGCS.Core`. It does not add a compiler, build integration or SDK distribution.

## Inputs

`target-sdk.json` declares the platform family, architecture, ABI, triple,
byte order, export/calling spellings, optional ordered header search paths,
and local SDK include directories. The checked-in profile is Windows x64/MSVC.
Use the exact facts from your SDK when editing it. GNU Linux x64 and Intel macOS
also need their own family, ABI, triple and source conventions; changing only the
private target's name does not establish those facts.

`bindgen.json` selects that target, its local plugin manifest, the profile input,
and `dotnet.coreclr`. This sample reuses the existing desktop execution and ABI
implementation. A different platform family requires its own applicable runtime
and call-ABI implementation, or fails explicitly. No console SDK is certified by
this example.

## Generate

Use the matching development candidate of the complete CLI:

```bash
dotnet build BGCS.Example.CustomTargetPlugin.csproj --configuration Release
python write-manifest.py
bindgen-cs generate bindgen.json
```

The same manifest works from a C# tool:

```csharp
await using BindingHost generator = BindingGenerator.CreateDefault();
GenerationResult result = await generator.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
```

The default composition selects host services before reading configuration and
manifests. Configuration cannot replace the host. The plugin supplies target
facts; the isolated frontend extracts layouts from the actual headers.

The provider declares the profile before reading it and fingerprints its bytes.
SDK directories remain declared inputs. Changes to the profile, headers, plugin
code or settings invalidate affected caches; late changes block publication.
Regenerate `plugin.json` after rebuilding the plugin.

The consumer maps declared input roots and compiles the generated sources using
its own build. Module providers and external function tables customize symbol
acquisition independently of source parsing.
