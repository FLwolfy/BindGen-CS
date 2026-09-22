namespace BGCS.Runtime;

using System.Runtime.InteropServices;

/// <summary>
/// Opaque 12-byte representation of an extended-precision native <c>long double</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 12)]
public struct NativeLongDouble12
{
}

/// <summary>
/// Opaque 16-byte representation of an extended-precision native <c>long double</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct NativeLongDouble16
{
}
