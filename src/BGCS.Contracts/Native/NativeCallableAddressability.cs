namespace BGCS.Contracts.Native;

/// <summary>Separates a callable source expression from an addressable native function.</summary>
public enum NativeCallableAddressability
{
    /// <summary>No independent address-expression fact has been established.</summary>
    Unknown,
    /// <summary>The frontend accepted an address expression; linkage and export presence remain separate facts.</summary>
    Addressable,
    /// <summary>The compiler requires direct invocation and rejects taking this callable's address.</summary>
    DirectCallOnly
}
