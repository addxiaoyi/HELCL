using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace HelL.Tests;

internal static class TestPaths
{
    // Test binaries may live on another drive when the checkout's disk is full.
    internal static string Root([CallerFilePath] string source = "")
    {
        foreach (string start in new[] { Path.GetDirectoryName(source), Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "HelL.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
        }
        throw new DirectoryNotFoundException("Cannot locate the HelL source checkout for source verification.");
    }
}
