using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public void SpeichernUndLaden_ErgibtGleicheConfig()
    {
        using var file = TempPath.File();
        var store = new JsonConfigStore(file.Path);

        var config = new TelegramConfig(
            12345, "abcdef123456", "+491701234567",
            ClearCacheOnStart: false, DownloadFolder: @"C:\Musik", UseDownloadFolder: true);

        store.Save(config);
        TelegramConfig loaded = store.Load();

        Assert.Equal(config, loaded);
    }

    [Fact]
    public void Laden_DateiFehlt_GibtEmpty()
    {
        using var file = TempPath.File(); // nicht angelegt
        var store = new JsonConfigStore(file.Path);

        Assert.Equal(TelegramConfig.Empty, store.Load());
    }

    [Fact]
    public void Laden_KaputteDatei_GibtEmpty()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, "{ das ist kein JSON ");
        var store = new JsonConfigStore(file.Path);

        Assert.Equal(TelegramConfig.Empty, store.Load());
    }

    [Fact]
    public void Laden_AltesFormatOhneNeueFelder_NimmtStandardwerte()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path,
            """
            { "ApiId": 555, "ApiHash": "hash", "PhoneNumber": "+49170", "IsComplete": true }
            """);

        TelegramConfig loaded = new JsonConfigStore(file.Path).Load();

        Assert.Equal(555, loaded.ApiId);
        Assert.Equal("hash", loaded.ApiHash);
        Assert.Equal("+49170", loaded.PhoneNumber);
        Assert.True(loaded.ClearCacheOnStart);   // Standardwert
        Assert.False(loaded.UseDownloadFolder);
        Assert.Equal("", loaded.DownloadFolder);
        Assert.True(loaded.IsComplete);
    }

    [Fact]
    public void Speichern_SchreibtKeineBerechnetenFelder()
    {
        using var file = TempPath.File();
        new JsonConfigStore(file.Path).Save(new TelegramConfig(1, "h", "+1"));

        string json = File.ReadAllText(file.Path);
        Assert.DoesNotContain("IsComplete", json);
    }
}
