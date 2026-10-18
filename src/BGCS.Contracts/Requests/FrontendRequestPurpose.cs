namespace BGCS.Contracts.Requests;

/// <summary>Identifies why a detached source graph is parsed without exposing a Host pipeline service.</summary>
public enum FrontendRequestPurpose
{
    /// <summary>Extracts the user's original API and explicitly requested source adapters.</summary>
    SourceAnalysis,
    /// <summary>Checks the emitted C ABI header against the planned native bridge.</summary>
    BridgeVerification,
}
