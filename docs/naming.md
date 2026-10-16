# Naming flat native identifiers

BGCS preserves its ordinary naming behavior unless a naming policy explicitly opts
into dictionary segmentation. This policy changes managed presentation only; native
export names, type identities, layouts and calling conventions remain unchanged.

```json
{
  "api": {
    "naming": {
      "flatIdentifierSegmentation": "english",
      "functions": { "casing": "pascal" }
    }
  }
}
```

The fixed English vocabulary converts `CREATEWINDOW`, `DEPTHBUFFER` and
`GETMOUSEPOS` to `CreateWindow`, `DepthBuffer` and `GetMousePos`. Matching takes
the longest vocabulary prefix at each position. Unknown fragments and digits stay
in the result. Lowercase and mixed-case words retain their ordinary case-boundary
behavior. Exact `api.names` mappings and `casing: "preserve"` bypass segmentation;
an unspecified category casing also retains its existing behavior.

Use `flatIdentifierSegmentation: "custom"` with `flatIdentifierWords` for an exact
project vocabulary, for example `["create", "window", "mouse", "pos"]`. Custom
lists must contain 1–4096 distinct case-insensitive words, each consisting only of
letters and at most 128 UTF-16 characters. Empty words, duplicates and words
containing punctuation or digits fail validation. Supplying words with `none` or
`english` also fails. The default mode is `none`.

The same immutable `ManagedNamingPolicy` works in `ManagedProjectionRequest` and
the grouped configuration. Each projection owns its custom index. English uses
one shared immutable index from the versioned embedded vocabulary, with a fixed
content digest; it performs no filesystem discovery. The emission cache includes
the complete configuration and backend assembly bytes, so custom word edits and
built-in vocabulary updates invalidate generated names.
