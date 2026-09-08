using System.Diagnostics;

namespace ViewerForTelegram.Data;

/// <summary>Small filesystem helpers shared across the Data/UI layers.</summary>
public static class IoUtil
{
    /// <summary>
    /// Opens <paramref name="path"/> in File Explorer. Launches
    /// <c>explorer.exe</c> with the literal, quoted path instead of
    /// shell-executing the folder directly: the latter resolves a path that
    /// sits under a redirected known folder (e.g. the Music library moved into
    /// OneDrive) to that library's virtual node and then opens its root rather
    /// than the actual sub-folder. Throws if the folder can't be opened.
    /// </summary>
    public static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true
        });
    }

    /// <summary>Deletes <paramref name="path"/> if it exists; swallows any failure.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // never mind
        }
    }

    /// <summary>
    /// Keeps a log file from growing without bound: once <paramref name="path"/>
    /// reaches <paramref name="maxBytes"/> it is moved to <c>&lt;path&gt;.1</c>
    /// (replacing any previous <c>.1</c>) and logging continues in a fresh file,
    /// so at most ~2× <paramref name="maxBytes"/> is ever kept. Any failure is
    /// swallowed - rotation must never break logging.
    /// </summary>
    public static void RollIfTooLarge(string path, long maxBytes)
    {
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length < maxBytes)
            {
                return;
            }

            string backup = path + ".1";
            TryDelete(backup);
            File.Move(path, backup);
        }
        catch
        {
            // never mind - keep appending to the current file
        }
    }
}
