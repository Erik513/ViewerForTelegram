using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class TelegramConfigTests
{
    [Fact]
    public void Empty_IsNotComplete()
    {
        Assert.False(TelegramConfig.Empty.IsComplete);
    }

    [Fact]
    public void AllFieldsFilled_IsComplete()
    {
        var c = new TelegramConfig(12345, "abcdef", "+491701234567");
        Assert.True(c.IsComplete);
    }

    [Theory]
    [InlineData(0, "abc", "+49170")]        // api_id missing
    [InlineData(-1, "abc", "+49170")]       // api_id invalid
    [InlineData(12345, "", "+49170")]       // api_hash empty
    [InlineData(12345, "   ", "+49170")]    // api_hash whitespace only
    [InlineData(12345, "abc", "")]          // phone empty
    [InlineData(12345, "abc", "   ")]       // phone whitespace only
    public void MissingField_IsNotComplete(int apiId, string apiHash, string phone)
    {
        var c = new TelegramConfig(apiId, apiHash, phone);
        Assert.False(c.IsComplete);
    }

    [Fact]
    public void Defaults_ClearCacheOn_NoFolder()
    {
        var c = new TelegramConfig(1, "h", "+1");
        Assert.True(c.ClearCacheOnStart);
        Assert.False(c.UseDownloadFolder);
        Assert.Equal("", c.DownloadFolder);
    }

    [Fact]
    public void EffectiveDownloadFolder_FallsBackToOsDownloads_WhenBlank()
    {
        var c = new TelegramConfig(1, "h", "+1");
        Assert.Equal(AppPaths.DownloadsFolder, c.EffectiveDownloadFolder);
        Assert.Equal(AppPaths.DownloadsFolder, (c with { DownloadFolder = "   " }).EffectiveDownloadFolder);
    }

    [Fact]
    public void EffectiveDownloadFolder_UsesChosenFolder_WhenSet()
    {
        var c = new TelegramConfig(1, "h", "+1", DownloadFolder: @"D:\Songs");
        Assert.Equal(@"D:\Songs", c.EffectiveDownloadFolder);
    }

    [Fact]
    public void ValueEquality_AsRecord()
    {
        var a = new TelegramConfig(1, "h", "+1", ClearCacheOnStart: false);
        var b = new TelegramConfig(1, "h", "+1", ClearCacheOnStart: false);
        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { PhoneNumber = "+2" });
    }
}
