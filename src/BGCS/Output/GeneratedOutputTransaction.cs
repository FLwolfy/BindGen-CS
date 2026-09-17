namespace BGCS.Output;

using BGCS.Core.IO;

internal sealed class GeneratedOutputTransaction : IDisposable
{
    private readonly OutputDirectoryTransaction transaction;

    internal GeneratedOutputTransaction(string destinationPath)
    {
        transaction = new(destinationPath);
    }

    internal string StagingPath => transaction.StagingPath;

    internal void Commit() => transaction.Commit();

    public void Dispose() => transaction.Dispose();
}
