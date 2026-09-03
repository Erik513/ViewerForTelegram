namespace ViewerForTelegram.Tests;

/// <summary>A unique path in the temp folder, cleaned up on dispose.</summary>
internal sealed class TempPath : IDisposable
{
    public string Path { get; }

    private TempPath(string path) => Path = path;

    public static TempPath File() =>
        new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "vft-test-" + Guid.NewGuid().ToString("N") + ".json"));

    public static TempPath Dir()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "vft-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new TempPath(dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
            else if (System.IO.File.Exists(Path))
            {
                System.IO.File.Delete(Path);
            }
        }
        catch
        {
            // Cleanup in tests is best effort.
        }
    }
}
