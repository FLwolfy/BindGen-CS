namespace BGCS.Runtime;

/// <summary>Declares which threads may execute one allocator's allocation and release callbacks.</summary>
public enum NativeAllocatorThreadDomain
{
    /// <summary>No thread contract is known; owned allocation is rejected.</summary>
    Unknown,
    /// <summary>The caller asserts that both callbacks safely support arbitrary concurrent threads, including finalization.</summary>
    AnyThread,
    /// <summary>Both callbacks must run on the explicitly supplied dispatcher's thread.</summary>
    Dispatcher
}
