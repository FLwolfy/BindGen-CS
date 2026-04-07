using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BGCS.Configuration.Tests;

public class AllEntriesTests : ConfigurationEntryTestBase
{
    public static IEnumerable<object[]> EntryFolders()
    {
        Assert.True(Directory.Exists(EntryTestsRoot), $"Entry tests root not found: {EntryTestsRoot}");

        string[] entryFolderPaths = Directory
            .GetDirectories(EntryTestsRoot)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(entryFolderPaths);

        foreach (string entryFolderPath in entryFolderPaths)
        {
            yield return [entryFolderPath];
        }
    }

    [Theory]
    [MemberData(nameof(EntryFolders))]
    public void EntryFolder_ShouldGenerateAndMatchExpected(string entryFolderPath)
    {
        using var output = GenerateFromEntryFolder(entryFolderPath, "config.json");
        PrintBindings(output);
        AssertGenerationSucceeded(output);
        AssertExpectedForEntryFolder(output, entryFolderPath);
    }
}
