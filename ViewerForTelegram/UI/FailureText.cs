using ViewerForTelegram.Logic;
using ViewerForTelegram.UI.Localization;

namespace ViewerForTelegram.UI;

/// <summary>
/// The reason of a failure in the language of the UI. The English message of the
/// exception itself is not shown (it comes from .NET, NAudio or Telegram); callers
/// write the exception to the error log.
/// </summary>
internal static class FailureText
{
    public static string For(Exception ex, bool whilePlaying = false)
    {
        Failure failure = FailureClassifier.Classify(ex, whilePlaying);
        return failure.Kind switch
        {
            FailureKind.FileMissing => Loc.S("fail.fileMissing"),
            FailureKind.AccessDenied => Loc.S("fail.accessDenied"),
            FailureKind.DiskFull => Loc.S("fail.diskFull"),
            FailureKind.Network => Loc.S("fail.network"),
            FailureKind.RateLimit => Loc.T("fail.rateLimit", failure.Seconds),
            FailureKind.Refused => Loc.T("fail.refused", failure.Code),
            FailureKind.NotKnown => Loc.S("fail.notKnown"),
            FailureKind.Unreadable => Loc.S("fail.unreadable"),
            _ => Loc.S("fail.other"),
        };
    }
}
