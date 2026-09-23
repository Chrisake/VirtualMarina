namespace VirtualMarina.TestSupport;

/// <summary>The root of the source tree the running tests were built from.</summary>
public static class RepositoryRoot
{
    private static readonly Lazy<string> Root = new(Find);

    /// <summary>The directory holding VirtualMarina.sln, found by walking up from the test assembly.</summary>
    public static string Path => Root.Value;

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "VirtualMarina.sln"))) return directory.FullName;
        }

        throw new InvalidOperationException(
            $"No VirtualMarina.sln above {AppContext.BaseDirectory}. The tests read files from the source tree, so they have to run from a build inside it.");
    }
}
