using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class TelegramConfigTests
{
    [Fact]
    public void Empty_IstNichtVollstaendig()
    {
        Assert.False(TelegramConfig.Empty.IsComplete);
    }

    [Fact]
    public void AlleFelderGefuellt_IstVollstaendig()
    {
        var c = new TelegramConfig(12345, "abcdef", "+491701234567");
        Assert.True(c.IsComplete);
    }

    [Theory]
    [InlineData(0, "abc", "+49170")]        // api_id fehlt
    [InlineData(-1, "abc", "+49170")]       // api_id ungültig
    [InlineData(12345, "", "+49170")]       // api_hash leer
    [InlineData(12345, "   ", "+49170")]    // api_hash nur Leerzeichen
    [InlineData(12345, "abc", "")]          // Telefon leer
    [InlineData(12345, "abc", "   ")]       // Telefon nur Leerzeichen
    public void FehlendesFeld_IstNichtVollstaendig(int apiId, string apiHash, string phone)
    {
        var c = new TelegramConfig(apiId, apiHash, phone);
        Assert.False(c.IsComplete);
    }

    [Fact]
    public void Standardwerte_CacheLeerenAn_KeinOrdner()
    {
        var c = new TelegramConfig(1, "h", "+1");
        Assert.True(c.ClearCacheOnStart);
        Assert.False(c.UseDownloadFolder);
        Assert.Equal("", c.DownloadFolder);
    }

    [Fact]
    public void Wertgleichheit_AlsRecord()
    {
        var a = new TelegramConfig(1, "h", "+1", ClearCacheOnStart: false);
        var b = new TelegramConfig(1, "h", "+1", ClearCacheOnStart: false);
        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { PhoneNumber = "+2" });
    }
}
