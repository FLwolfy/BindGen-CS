# Native module leases and owned object cleanup

Status: implemented for the neutral module runtime and declared owned status/out results;
remaining callback, asynchronous and thread-affine generation paths require separate acceptance.

Native function addresses remain valid only while their module remains loaded. Closing an
API context must stop new public calls while preserving cleanup for objects and operations
that already own native resources.

`NativeModuleSession` owns a provider and reference-counted call and resource leases. Its
`Dispose` stops admission and releases the context reference. The final lease triggers
provider disposal. `DisposeAsync` also waits for that event; callers must release owned
objects before awaiting completion.

`OwnedNativeHandle` retains its creating module and the matching release delegate. Public
object operations protect the handle with `SafeHandle.DangerousAddRef` and acquire a normal
call lease. Cleanup acquires a resource cleanup call, which remains available after context
closure. Cleanup failures are captured and do not escape the finalizer or unload the module
under a potentially live resource. Borrowed views follow the owner's validity and acquire
no destruction responsibility.

The C# planner requires explicit ownership, owner lifetime, allocation/release domain,
matching pointer and destructor signatures, and `releaseThreadDomain = "any"` before
generating automatic owned wrappers. A user declaration remains `UserAsserted`; selecting
it does not create independent ABI or lifecycle verification. An unknown or affine release
domain omits this wrapper. Raw ABI access remains available.

Generated factories retain the resource lease before entering native construction and
transfer it only after constructing the managed handle. Typed object methods use that
handle's call admission and keep error cleanup inside the same protected invocation.
Release dispatch calls the planned native import directly under the cleanup lease, rather
than reentering a public Raw method after the context has closed.

FunctionTable contexts own independent providers and immutable required-symbol resolution.
DllImport and LibraryImport contexts govern their own call and object lifetimes while the
.NET import loader owns the shared native import module. These mechanisms do not establish
native object thread safety or a general thread-affine release scheduler.

The source Runtime catalog includes the same handle and borrowed-view closure as the
package implementation. Actual C++ fixtures exercise all three import modes with package
and source Runtime, constructor/member failures, context closure, repeated disposal,
borrowed-view invalidation and delayed provider unloading. These local fixtures do not
replace platform-specific NativeAOT and browser certification.
