namespace ViewerForTelegram.Data;

/// <summary>
/// A download was asked for a file Telegram no longer returns for its message
/// (the message was deleted, or the list is out of date).
/// </summary>
public sealed class FileNotKnownException : InvalidOperationException
{
    public FileNotKnownException()
        : base("File not known - load the list first.")
    {
    }
}
