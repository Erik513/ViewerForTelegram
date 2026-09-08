using ViewerForTelegram.Data;
using Xunit;

namespace ViewerForTelegram.Tests;

public class IoUtilTests
{
    [Fact]
    public void RollIfTooLarge_UnderLimit_LeavesFileAlone()
    {
        using TempPath dir = TempPath.Dir();
        string log = Path.Combine(dir.Path, "app.log");
        File.WriteAllText(log, "small");

        IoUtil.RollIfTooLarge(log, maxBytes: 1024);

        Assert.Equal("small", File.ReadAllText(log));
        Assert.False(File.Exists(log + ".1"));
    }

    [Fact]
    public void RollIfTooLarge_AtOrOverLimit_MovesCurrentToBackup()
    {
        using TempPath dir = TempPath.Dir();
        string log = Path.Combine(dir.Path, "app.log");
        File.WriteAllText(log, new string('x', 2048));

        IoUtil.RollIfTooLarge(log, maxBytes: 1024);

        Assert.False(File.Exists(log));
        Assert.Equal(new string('x', 2048), File.ReadAllText(log + ".1"));
    }

    [Fact]
    public void RollIfTooLarge_RollingAgain_ReplacesPreviousBackup()
    {
        using TempPath dir = TempPath.Dir();
        string log = Path.Combine(dir.Path, "app.log");

        File.WriteAllText(log, new string('a', 2048));
        IoUtil.RollIfTooLarge(log, maxBytes: 1024);

        File.WriteAllText(log, new string('b', 2048));
        IoUtil.RollIfTooLarge(log, maxBytes: 1024);

        Assert.Equal(new string('b', 2048), File.ReadAllText(log + ".1"));
        Assert.False(File.Exists(log));
    }

    [Fact]
    public void RollIfTooLarge_MissingFile_DoesNothing()
    {
        using TempPath dir = TempPath.Dir();
        string log = Path.Combine(dir.Path, "does-not-exist.log");

        IoUtil.RollIfTooLarge(log, maxBytes: 1024);

        Assert.False(File.Exists(log));
        Assert.False(File.Exists(log + ".1"));
    }
}
