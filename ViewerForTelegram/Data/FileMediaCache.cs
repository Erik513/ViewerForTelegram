using System.Text;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="IMediaCache"/> as a flat folder (default: <see cref="AppPaths.CacheDir"/>).
/// File name: <c>&lt;FileId&gt;__&lt;readable name&gt;.&lt;ext&gt;</c> - the FileId in front
/// makes it unique, the name at the back stays recognizable.
/// </summary>
public sealed class FileMediaCache : IMediaCache
{
    private readonly string _dir;

    public FileMediaCache(string cacheDir)
    {
        _dir = cacheDir;
        Directory.CreateDirectory(_dir);
    }

    public string GetPath(AudioMessage message) =>
        Path.Combine(_dir, BuildFileName(message));

    public bool Contains(AudioMessage message)
    {
        string path = GetPath(message);
        return File.Exists(path) && new FileInfo(path).Length == message.SizeBytes;
    }

    public (int Count, long TotalBytes) GetStats()
    {
        FileInfo[] files = Files();
        return (files.Length, files.Sum(f => f.Length));
    }

    public void Clear()
    {
        foreach (FileInfo f in Files())
        {
            TryDelete(f);
        }
    }

    public void PruneToLimit(long maxBytes)
    {
        FileInfo[] files = Files()
            .OrderBy(f => f.LastWriteTimeUtc)   // oldest first
            .ToArray();

        long total = files.Sum(f => f.Length);

        foreach (FileInfo f in files)
        {
            if (total <= maxBytes)
            {
                break;
            }

            long len = f.Length;
            if (TryDelete(f))
            {
                total -= len;
            }
        }
    }

    private FileInfo[] Files() =>
        new DirectoryInfo(_dir)
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch
        {
            return false; // e.g. currently in use
        }
    }

    private static string BuildFileName(AudioMessage m)
    {
        string ext = Path.GetExtension(m.FileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = ".bin";
        }

        string name = Sanitize(Path.GetFileNameWithoutExtension(m.FileName));
        return $"{m.FileId}__{name}{ext}";
    }

    private static string Sanitize(string value)
    {
        StringBuilder sb = new(value.Length);
        foreach (char c in value)
        {
            sb.Append(
                char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '(' or ')'
                    ? c
                    : '_');
        }

        string result = sb.ToString().Trim();
        if (result.Length == 0)
        {
            return "audio";
        }

        return result.Length > 80 ? result[..80] : result;
    }
}
