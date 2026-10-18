namespace BGCS.RepositoryTools.Tests;

public sealed class ApiSnapshotFixture<T>
{
    public sealed class Nested<TValue>
    {
    }

    public int[,,]? matrix { get; set; }
    public ApiSnapshotFixture<int>.Nested<string>? nested { get; set; }

    public void Read(
        ref int value,
        out string result,
        in long input
    ) {
        value = (int)input;
        result = value.ToString();
    }
}

public sealed unsafe class FunctionPointerSnapshotFixture
{
    public FunctionPointerSnapshotFixture(delegate* unmanaged[Stdcall]<byte*, long> callback) => stdcall = callback;
    public delegate* unmanaged[Cdecl]<int, float, double> cdecl;
    public delegate* unmanaged[Stdcall]<byte*, long> stdcall;
    public delegate* managed<int, int> managed;
    public delegate* unmanaged[Cdecl]<int, float, double> callback { get; set; }
    public delegate* unmanaged[Cdecl]<void> nested(delegate* unmanaged[Cdecl]<int, int>* input) => null;
}

public sealed unsafe class GenericFunctionPointerSnapshotFixture<T> where T : unmanaged
{
    public delegate* managed<T, T> generic;
    public delegate* managed<TValue, TValue> Identity<TValue>(delegate* managed<TValue, TValue> callback) where TValue : unmanaged => callback;
}

public enum ApiSurfaceEnum : ulong
{
    High = 0x8000000000000000UL,
    All = ulong.MaxValue
}

public abstract class ApiSurfaceBase { }

public sealed class ApiSurfaceFixture<T> : ApiSurfaceBase, System.IDisposable where T : class, System.IDisposable, new()
{
    public const ulong Maximum = ulong.MaxValue;
    public const decimal Exact = -12.50m;
    public const double NegativeZero = -0.0;
    public static readonly int UninitializedProbe = ThrowIfInitialized();
    public string label { get; init; } = string.Empty;
    public int this[int index] => index;
    public static ApiSurfaceFixture<T> Create(int count = 17) => throw new System.NotSupportedException();
    public void Consume(params int[] values) { }
    public void Dispose() { }
    public TValue Identity<TValue>(TValue value) where TValue : unmanaged => value;
    public static implicit operator int(ApiSurfaceFixture<T> value) => 0;
    private static int ThrowIfInitialized() => throw new System.InvalidOperationException("API snapshots must read metadata without running initialization.");
}
