namespace ViewerForTelegram.Data;

/// <summary>Small filesystem helpers shared across the Data/UI layers.</summary>
public static class IoUtil
{
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
}
