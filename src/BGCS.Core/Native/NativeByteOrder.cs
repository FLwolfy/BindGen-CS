namespace BGCS.Core.Native;

/// <summary>Identifies native storage byte order without inferring it from a target name or the generator host.</summary>
public enum NativeByteOrder
{
    /// <summary>The target provider has not established byte order.</summary>
    Unknown,
    /// <summary>The least significant byte occupies the lowest address.</summary>
    LittleEndian,
    /// <summary>The most significant byte occupies the lowest address.</summary>
    BigEndian
}
