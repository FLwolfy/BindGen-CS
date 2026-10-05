namespace BGCS.Tool.Tests;

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
