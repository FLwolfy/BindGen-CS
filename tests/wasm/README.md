The browser gate shares scalar, wide integer/enum, native long, bool, record,
buffer, handle, callback, release and C++ invocation fixtures with NativeAOT.
It executes DllImport and LibraryImport, the imports admitted by the Browser
profile. FunctionTable must be rejected before replacing existing output; its
trimmed interpreter signature contract is not established. Desktop FunctionTable
ownership and invocation tests remain in the NativeAOT/desktop fixture.

The four original-address alias cases per import compare native pointer equality
and ordered ref/ref, in/out, record-field and Span/ref mutations. Current evidence
must identify the exact selected cases, runtime, trimming and execution mode.

The manual nonblocking lifecycle fixture retains 11 checks per admitted import.
It rejects unregister/stop inside an active native frame and accesses retained
memory after managed completion returns. Both interpreter and AOT must report
every selected case. Draft source or desktop compilation cannot certify it.

The targeted CI fixture separately verifies exact high-bit UInt64 returns,
mixed scalar arguments, UInt64 callbacks, anonymous aggregate input/output and
callbacks, closed-context admission, and FunctionTable refusal with preserved
output. Both execution modes use trimming. The independent repository harness
owns compilation; no consumer build capability is added to BGCS.
