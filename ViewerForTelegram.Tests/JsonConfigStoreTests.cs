using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class JsonConfigStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var file = TempPath.File();
        var store = new JsonConfigStore(file.Path);

        var config = new TelegramConfig(
            12345, "abcdef123456", "+491701234567",
            ClearCacheOnStart: false, DownloadFolder: @"C:\Music", UseDownloadFolder: true,
            Language: DisplayLanguage.German);

        store.Save(config);
        TelegramConfig loaded = store.Load();

        Assert.Equal(config, loaded);
    }

    [Fact]
    public void Save_WritesLanguageAsName_NotNumber()
    {
        using var file = TempPath.File();
        new JsonConfigStore(file.Path).Save(
            new TelegramConfig(1, "h", "+1", Language: DisplayLanguage.German));

        string json = File.ReadAllText(file.Path);
        Assert.Contains("\"German\"", json);
    }

    [Fact]
    public void Load_FileMissing_ReturnsEmpty()
    {
        using var file = TempPath.File(); // not created
        var store = new JsonConfigStore(file.Path);

        Assert.Equal(TelegramConfig.Empty, store.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsEmpty()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, "{ this is not JSON ");
        var store = new JsonConfigStore(file.Path);

        Assert.Equal(TelegramConfig.Empty, store.Load());
    }

    [Fact]
    public void Load_OldFormatWithoutNewFields_UsesDefaults()
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
        Assert.True(loaded.ClearCacheOnStart);   // default value
        Assert.False(loaded.UseDownloadFolder);
        Assert.Equal("", loaded.DownloadFolder);
        Assert.Equal(DisplayLanguage.English, loaded.Language);   // default value
        Assert.True(loaded.IsComplete);
    }

    [Fact]
    public void Save_DoesNotWriteComputedFields()
    {
        using var file = TempPath.File();
        new JsonConfigStore(file.Path).Save(new TelegramConfig(1, "h", "+1"));

        string json = File.ReadAllText(file.Path);
        Assert.DoesNotContain("IsComplete", json);
    }
}
