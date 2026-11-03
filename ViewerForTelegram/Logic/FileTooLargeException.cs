namespace ViewerForTelegram.Logic;

/// <summary>
/// A file is bigger than the whole cache, so downloading it would only fill
/// the disk and then be pruned away again.
/// </summary>
public sealed class FileTooLargeException : Exception
{
    public long SizeBytes { get; }
    public long LimitBytes { get; }

    public FileTooLargeException(long sizeBytes, long limitBytes)
        : base($"File is {sizeBytes} bytes, larger than the cache limit of {limitBytes} bytes.")
    {
        SizeBytes = sizeBytes;
        LimitBytes = limitBytes;
    }
}
