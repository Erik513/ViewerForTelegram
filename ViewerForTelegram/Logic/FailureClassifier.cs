using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NAudio;
using TL;
using ViewerForTelegram.Data;

namespace ViewerForTelegram.Logic;

public enum FailureKind
{
    FileMissing,
    AccessDenied,
    DiskFull,
    Network,
    RateLimit,
    Refused,
    NotKnown,
    Unreadable,
    Other,
}

/// <param name="Seconds">For <see cref="FailureKind.RateLimit"/>: how long Telegram asks to wait.</param>
/// <param name="Code">For <see cref="FailureKind.Refused"/>: Telegram's error code (e.g. FILE_REFERENCE_EXPIRED).</param>
public readonly record struct Failure(FailureKind Kind, int Seconds = 0, string Code = "");

/// <summary>
/// Sorts an exception into a few kinds the UI can describe in the user's language.
/// The message of an exception comes from .NET, NAudio or Telegram - always English -
/// so it is never shown; the full text goes to the error log instead.
/// </summary>
public static class FailureClassifier
{
    private const int ErrorHandleDiskFull = 0x27;
    private const int ErrorDiskFull = 0x70;

    /// <param name="whilePlaying">
    /// The exception came from opening / decoding an audio file: whatever it was, an
    /// unknown cause means the file itself cannot be played.
    /// </param>
    public static Failure Classify(Exception ex, bool whilePlaying = false)
    {
        if (TryGetRateLimit(ex, out int seconds))
        {
            return new Failure(FailureKind.RateLimit, seconds);
        }

        switch (ex)
        {
            case FileNotFoundException or DirectoryNotFoundException:
                return new Failure(FailureKind.FileMissing);
            case UnauthorizedAccessException:
                return new Failure(FailureKind.AccessDenied);
            case IOException io when (io.HResult & 0xFFFF) is ErrorDiskFull or ErrorHandleDiskFull:
                return new Failure(FailureKind.DiskFull);
            case FileNotKnownException:
                return new Failure(FailureKind.NotKnown);
            case RpcException rpc:
                return new Failure(FailureKind.Refused, Code: rpc.Message);
        }

        if (whilePlaying)
        {
            return new Failure(FailureKind.Unreadable);
        }

        switch (ex)
        {
            case HttpRequestException or SocketException or TimeoutException or IOException or WTelegram.WTException:
                return new Failure(FailureKind.Network);
            case NotSupportedException or FormatException or InvalidDataException or COMException or MmException:
                return new Failure(FailureKind.Unreadable);
        }

        return ex.InnerException is { } inner
            ? Classify(inner)
            : new Failure(FailureKind.Other);
    }

    /// <summary>Matches WTelegramClient's "FLOOD_WAIT_&lt;seconds&gt;" rate-limit error (also in an inner exception).</summary>
    public static bool TryGetRateLimit(Exception ex, out int seconds)
    {
        seconds = 0;
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            Match m = Regex.Match(e.Message ?? "", @"FLOOD_WAIT_(\d+)");
            if (m.Success)
            {
                seconds = int.Parse(m.Groups[1].Value);
                return true;
            }
        }
        return false;
    }
}
