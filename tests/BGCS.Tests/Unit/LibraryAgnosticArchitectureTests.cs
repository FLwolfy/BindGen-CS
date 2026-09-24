using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace BGCS.Tests.Unit;

public sealed class LibraryAgnosticArchitectureTests
{
    [Fact]
    public void CoreSourceDoesNotMentionSpecificNativeLibraries()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "BGCS.Core")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        string sourceRoot = Path.Combine(directory.FullName, "src");
        string[] forbiddenMarkers =
        [
            "ImVec", "ImGui", "SDL_", "cimgui", "bgfx", "bimg", "miniaudio",
            "InnoEngine", "GLFW_", "sqlite3_", "Assimp::", "gbf::",
            "HWND", "HINSTANCE", "HRESULT", "Uint8", "Sint8"
        ];
        List<string> offenders = [];
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                continue;
            string content = File.ReadAllText(path);
            foreach (string marker in forbiddenMarkers)
            {
                if (content.Contains(marker, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetRelativePath(sourceRoot, path)}: {marker}");
            }
        }
        Assert.Empty(offenders);
    }
}
