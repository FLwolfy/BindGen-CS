# BGCS.Runtime

`BGCS.Runtime` is the .NET 10 runtime consumed by BGCS 3.0 bindings. It has no
generator, Contracts, Clang, Roslyn or MSBuild dependency.

```xml
<PackageReference Include="BGCS.Runtime" Version="3.0.0" />
```

Generated instance contexts use independent module sessions. Active calls,
owned handles, callback registrations and native operations retain the module
until their required cleanup ends. Context disposal rejects new public calls;
the final lease permits module unload. Dynamic loading, verified static
adapters and explicitly leased external tables are separate provider choices.

Runtime includes fixed-width bool/pointer helpers, module and call leases,
owned/borrowed handles, callback exception/registration support, explicit
asynchronous resource retention, UTF-8/UTF-16/UTF-32 codecs and matched aligned
allocation. These mechanisms require native ownership, unregister and
completion protocols; they do not infer safe lifecycle behavior from names.

`NativeAllocator` binds real allocate/free callbacks to an explicit thread
contract and optional module session. Its `NativeBuffer` protects in-flight
access and retains the matching cleanup module. Thread-affine frees use a
caller-owned dispatcher; asynchronous disposal waits for actual cleanup.
Ambiguous free failures are recorded without retry or unsafe module unload.
A session roots its provider through unresolved cleanup, including custom
providers with finalizable native handles; successful unload releases that
retention without preventing ordinary owned-handle finalization.
Providers that retain their owning session require explicit session disposal.
This consumer abstraction does not implicitly change generated string
marshalling. See the repository's CustomAllocator example for real native use.
Strict UTF input codec overloads accept an explicit `NativeAllocator` and
return `NativeStringLease` with the matching buffer/module ownership.
Synchronous disposal reports known cleanup failure; asynchronous disposal waits
for actual queued cleanup. Omitting that argument keeps framework allocation.
Generated text contexts can select the pair through an optional per-instance
factory receiving the exact module session; the returned allocator must bind
that same session. Cleanup callbacks must work after public context closure.

The default grouped generator configuration is `runtime.mode = "package"`.
`runtime.mode = "source"` emits the selected Runtime source closure in an
API-local namespace; single-file output can include it. Source and package
consumers are independently compiled and invoked during acceptance.

Native libraries remain separate application dependencies. BGCS construction
can produce verified artifacts and typed consumer references when explicitly
configured, but Runtime itself does not install toolchains or compile native
sources. A successful Runtime call does not certify every platform/ABI.

- [Getting started](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md)
- [Runtime lifetimes](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/runtime-lifetime.md)
- [ABI safety](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/abi-safety.md)
- [Packages](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/packages.md)
- [Testing](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/testing.md)

BindGen-CS uses the MIT License. Retained third-party portions preserve their
original notices. Current support statements require their exact validation
receipts; package Runtime helpers and automatic generated callback/async
projection are different capabilities.
