# Share physical path resolution with the standard host implementation

Status: accepted for4.0.

The standard host provider observes actual filesystem identity and validates the process host. These operations must use the same symlink traversal and path-boundary implementation used by snapshots and publication.

BGCS.Hosting.Standard therefore references BGCS.Infrastructure in addition to Core, solely for PhysicalPathResolver. The edge is explicit in the reviewed matrix: Core ← Infrastructure ← Hosting.Standard. Engine and Infrastructure do not reference the concrete host. Ordinary custom providers still implement only the Core interfaces and need not use this implementation.

Moving physical IO into Core would pollute neutral contracts. Copying security-sensitive traversal into each host would create competing identity rules. This dependency reuses a general mechanism without moving host/SDK/ABI selection back into the core.

Verification: project dependency validation rejects any unreviewed edge; independent Engine tests supply their own host facilities; filesystem tests cover directory-specific semantics and link identity.
