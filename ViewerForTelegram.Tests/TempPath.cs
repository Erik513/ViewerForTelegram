namespace ViewerForTelegram.Tests;

/// <summary>Ein eindeutiger Pfad im Temp-Ordner, der beim Dispose weggeräumt wird.</summary>
internal sealed class TempPath : IDisposable
{
    public string Path { get; }

    private TempPath(string path) => Path = path;

    public static TempPath File() =>
        new(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "tv-test-" + Guid.NewGuid().ToString("N") + ".json"));

    public static TempPath Dir()
    {
        string dir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "tv-test-" + Guid.NewGuid().ToString("N"));
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
            // Aufräumen im Test ist best effort.
        }
    }
}
