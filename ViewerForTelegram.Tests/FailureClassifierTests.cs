using System.Net.Http;
using System.Net.Sockets;
using ViewerForTelegram.Data;
using ViewerForTelegram.Logic;

namespace ViewerForTelegram.Tests;

public class FailureClassifierTests
{
    [Fact]
    public void Classify_NaudioStyleBareException_WhilePlaying_IsUnreadable() =>
        // NAudio throws a plain System.Exception with an English message ("Incorrect length for long.")
        Assert.Equal(FailureKind.Unreadable,
            FailureClassifier.Classify(new Exception("Incorrect length for long."), whilePlaying: true).Kind);

    [Fact]
    public void Classify_FormatProblemWhileLoading_IsUnreadable() =>
        Assert.Equal(FailureKind.Unreadable, FailureClassifier.Classify(new FormatException()).Kind);

    [Fact]
    public void Classify_MissingFile_WhilePlaying_IsFileMissing() =>
        Assert.Equal(FailureKind.FileMissing,
            FailureClassifier.Classify(new FileNotFoundException(), whilePlaying: true).Kind);

    [Fact]
    public void Classify_MissingFolder_IsFileMissing() =>
        Assert.Equal(FailureKind.FileMissing, FailureClassifier.Classify(new DirectoryNotFoundException()).Kind);

    [Fact]
    public void Classify_UnauthorizedAccess_IsAccessDenied() =>
        Assert.Equal(FailureKind.AccessDenied, FailureClassifier.Classify(new UnauthorizedAccessException()).Kind);

    [Theory]
    [InlineData(0x70)]
    [InlineData(0x27)]
    public void Classify_DiskFull_IsDiskFull(int win32Error)
    {
        var io = new IOException("There is not enough space on the disk.", unchecked((int)0x80070000 | win32Error));

        Assert.Equal(FailureKind.DiskFull, FailureClassifier.Classify(io).Kind);
    }

    [Fact]
    public void Classify_OtherIoError_IsNetwork() =>
        Assert.Equal(FailureKind.Network, FailureClassifier.Classify(new IOException("connection reset")).Kind);

    [Fact]
    public void Classify_HttpAndSocketErrors_AreNetwork()
    {
        Assert.Equal(FailureKind.Network, FailureClassifier.Classify(new HttpRequestException()).Kind);
        Assert.Equal(FailureKind.Network, FailureClassifier.Classify(new SocketException()).Kind);
        Assert.Equal(FailureKind.Network, FailureClassifier.Classify(new TimeoutException()).Kind);
    }

    [Fact]
    public void Classify_FloodWait_CarriesTheSeconds()
    {
        Failure failure = FailureClassifier.Classify(new Exception("Telegram says: 420 FLOOD_WAIT_31"));

        Assert.Equal(FailureKind.RateLimit, failure.Kind);
        Assert.Equal(31, failure.Seconds);
    }

    [Fact]
    public void Classify_FloodWaitInAnInnerException_IsFound()
    {
        var outer = new InvalidOperationException("wrapped", new Exception("FLOOD_WAIT_9"));

        Assert.Equal(9, FailureClassifier.Classify(outer).Seconds);
    }

    [Fact]
    public void Classify_FileNotKnown_IsNotKnown() =>
        Assert.Equal(FailureKind.NotKnown, FailureClassifier.Classify(new FileNotKnownException()).Kind);

    [Fact]
    public void Classify_UnknownException_IsOther() =>
        Assert.Equal(FailureKind.Other, FailureClassifier.Classify(new InvalidOperationException("odd")).Kind);

    [Fact]
    public void Classify_UnknownWrapperAroundNetworkError_LooksInside() =>
        Assert.Equal(FailureKind.Network,
            FailureClassifier.Classify(new InvalidOperationException("wrapped", new HttpRequestException())).Kind);

    [Fact]
    public void TryGetRateLimit_PlainError_IsFalse() =>
        Assert.False(FailureClassifier.TryGetRateLimit(new Exception("boom"), out _));
}
