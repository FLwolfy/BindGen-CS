# Custom native allocation

`NativeAllocator` retains a real allocation/free pair, its concrete identity, an explicit thread contract and an optional `NativeModuleSession`. Each `NativeBuffer` belongs to that pair. Callers never choose a different allocator when disposing it.

This example builds a real C++ implementation behind a C ABI. Every allocator has its own native state. The native release function checks the owning state, physical extent and alignment. The consumer writes thirteen bytes, asks native code for an independently computed weighted checksum, then closes the public module context while its buffer is alive. The module stays loaded until the matching native free and native allocator-state destruction finish.

```sh
bindgen-cs generate examples/CustomAllocator/bindgen.json
dotnet run --project examples/CustomAllocator/Consumer -- <absolute-native-library-path>
dotnet run --project examples/CustomAllocator/GeneratedConsumer -- <absolute-native-library-path>
```

The library is under `Generated/<concrete-target>/native/bin`. Supply the actual `.dll`, `.so` or `.dylib` path. The consumer references only Runtime and uses the raw C function addresses to define the allocator callbacks. Its separate provider owns both allocator state and library loading; no mutable process-wide allocator is installed.

`GeneratedConsumer` also compiles the generated C# files. Its ordinary `AllocatorApi.FromProvider(provider, temporaryAllocatorFactory)` uses the selected pair for the friendly `ExampleUtf8Length("A😀")` call; native code observes five UTF-8 bytes. A second explicit codec lease remains alive while the API closes, then its actual matching free permits allocator-state destruction and module unload. The provider's final cleanup owns that state, so the native callbacks remain valid independently of ordinary public `api.Raw` admission. Override `BGCSGeneratedRoot` if generated sources were written elsewhere.

`Allocate(logicalLength, alignment)` checks a positive power-of-two alignment, uses a nonzero rounded physical extent and verifies the returned address. The exact physical extent and alignment reach the matching release callback. Use `buffer.Invoke((address, logicalLength) => ...)` while accessing storage so concurrent disposal cannot free it during that operation. An address must not escape that scope unless the caller separately establishes its lifetime.

`AnyThread` asserts that allocation and free safely support arbitrary concurrent threads, including finalization. A thread-affine native allocator must use `Dispatcher` with `INativeReleaseDispatcher`. Allocation and buffer operations then require `CheckAccess()`. Off-thread disposal posts cleanup; keep the dispatcher alive and pumping until `DisposeAsync` completes. No finalizer blocks waiting for that dispatcher.

An exception from a free callback has ambiguous native state: it might occur after the allocation was freed. Runtime records `releaseFailure`, faults `DisposeAsync`, retains the module and performs no automatic retry. Retrying an arbitrary free could double-free native memory. A native API that supports recoverable release needs its own explicit protocol.

The source catalog feature `memory.native-allocator` supplies the same Runtime implementation with a caller-selected namespace. Strict UTF input codecs can use this pair explicitly, for example `Utf8Codec.EncodeNullTerminated(text, false, 64, allocator)`; dispose the resulting string lease only after native use ends, and await `DisposeAsync()` when queued cleanup must finish. UTF-16/UTF-32 overloads also take explicit byte order. Their source closures include allocator support. Generated conversions require their own declared service/configuration; a release-domain string alone does not specify an allocator implementation.

For generated friendly text inputs, the ordinary context factory accepts optional `temporaryAllocatorFactory`. Its callback receives the new instance's exact `NativeModuleSession`, after complete import resolution. Construct the allocator with that same session; a different session or null result is rejected. The allocation/free callbacks should capture independently checked function addresses and native state. Calling ordinary `api.Raw` from a free callback after context closure is rejected, so it cannot serve as a delayed release mechanism. The `NativeTemporaryAllocatorTests` fixture verifies actual native pairs independently for two contexts and a shared facade across all three imports with package/source Runtime, including closing the context inside matching text cleanup.

Compile/link the emitted bridge and native implementation externally, then compile the generated C# consumer. BGCS only generates sources.
