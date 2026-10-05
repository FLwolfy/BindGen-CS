using System.Collections.Generic;

namespace BGCS.Cpp2C.Lowering;

/// <summary>Explicit, user-owned C ABI shim copied into bridge output and its build manifest.</summary>
public sealed class CppNativeShim
{
    /// <summary>
    /// Stable registration name, unique within this extension category.
    /// </summary>
    public string name { get; set; } = string.Empty;
    /// <summary>
    /// User-owned C headers copied into the bridge and parsed for managed bindings.
    /// </summary>
    public List<string> publicHeaders { get; set; } = [];
    /// <summary>
    /// User-owned native implementation files copied into the bridge build.
    /// </summary>
    public List<string> sourceFiles { get; set; } = [];
    /// <summary>
    /// Evidence level checked against the selected lowering safety policy.
    /// </summary>
    public CppLoweringSafety safety { get; set; } = CppLoweringSafety.UserAsserted;
}
