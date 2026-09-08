namespace ViewerForTelegram.Data;

/// <summary>
/// Thrown from the login flow when the account has a cloud password (2FA)
/// enabled. WTelegramClient asks us for that password and we have no UI for
/// it yet. Derives from <see cref="System.NotSupportedException"/> so the
/// existing "don't retry this" checks still apply; the UI turns it into a
/// localised message.
/// </summary>
public sealed class TwoFactorAuthNotSupportedException : System.NotSupportedException
{
    public TwoFactorAuthNotSupportedException()
        : base("This account uses a cloud password (2FA), which is not supported yet.")
    {
    }
}
