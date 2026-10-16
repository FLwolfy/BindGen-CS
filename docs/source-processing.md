# Managed source processing

`output.processors` applies ordered operations to managed candidate artifacts after C# emission and before final import/layout checking, requested compilation and publication. The configuration contains data; it cannot name executable classes or deserialize delegates.

```json
{
  "output": {
    "layout": "singleFile",
    "singleFileName": "Bindings.cs",
    "processors": [
      {
        "id": "application.extra-api",
        "priority": 30,
        "kind": "addSource",
        "path": "Helpers/Api.Part.cs",
        "content": "namespace Example; public partial class Api { public static int Marker() { return 10; } }"
      },
      {
        "id": "application.literal",
        "priority": 20,
        "kind": "replaceText",
        "paths": ["Helpers/Api.Part.cs"],
        "match": "return 10;",
        "replacement": "return 20;"
      },
      {
        "id": "application.regex",
        "priority": 10,
        "kind": "regexReplace",
        "paths": ["Helpers/*.cs"],
        "match": "return (?<value>20);",
        "replacement": "return ${value} + 1;",
        "timeoutMilliseconds": 1000
      }
    ]
  }
}
```

Use the generated API's actual namespace and context name in an additional partial declaration. The example assumes instance style with namespace `Example` and API name `Api`. Higher priority runs first; equal priorities use ordinal processor IDs. IDs must also be unique across configured operations and registered extensions.

`paths` are case-sensitive paths relative to one target's `managed/` root. `*` and `?` match within a segment; a complete `**` segment matches directories. Absolute paths, backslashes, traversal and empty segments are rejected. Each processor receives the candidate set after the preceding operation, so an added file can be selected by later rules. Added filenames cannot collide by case with existing candidates. New C# must parse as a regular library compilation unit; top-level executable statements are rejected.

Literal replacement is ordinal; `ignoreCase` selects ordinal case-insensitive matching. Regex replacement uses invariant .NET matching and standard dollar substitutions, including named groups. Optional `multiline`, `singleline` and `explicitCapture` have their usual regex meanings. The finite per-file matching and total budget is `timeoutMilliseconds`, from 1 through 30000, with default 1000. By default, replacement rules fail if they match no text. Set `requireMatch: false` explicitly for an optional operation. Declarations and candidate text also have bounded sizes.

All added files pass through the artifact owner and appear in its manifest. With `singleFile`, every managed C# candidate, including additions and source Runtime units, is composed into the configured `singleFileName`. No separate helper C# file remains. Split layout preserves added relative paths.

The final contract reader still checks actual raw imports, forwarding and native storage. A processor that changes those contracts fails before publication. Additional ordinary API methods can live in a partial context; this facility does not grant permission to alter ABI fields or add a second raw storage declaration. A requested `build` also compiles the complete candidate with warnings as errors. Previous formal output survives a failed required check.

Every built-in operation fingerprints its ID, priority, selectors, content, matching flags, timeout and no-match policy. Configuration and implementation identities also enter the emission key. The reusable stage contains unprocessed emission, so processors run again against each restored candidate and cannot accumulate an earlier edit.

For embedded SDK use, the equivalent immutable implementations are `TextArtifactProcessor`, `RegexArtifactProcessor` and `AdditionalSourceArtifactProcessor` in `BGCS.CSharp.Processing`. Register their factories with the Host's artifact processor port. Advanced SDK extensions can derive `SyntaxArtifactProcessor` to transform detached Roslyn compilation units. Such a subclass has no cache fingerprint by default and therefore disables affected generation caching; implement `ICacheFingerprintProvider` only when every artifact-affecting input can be declared. Contracts-only extensions continue to implement `IArtifactProcessor` without taking a dependency on Roslyn.
