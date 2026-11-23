using ErikwnkCore;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class JsonUiStateStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsVolumeAndMute()
    {
        using var file = TempPath.File();
        var store = new JsonUiStateStore(file.Path);

        store.Save(new UiState(
            LastChatId: 7, RangeDays: 14,
            Volume: new AudioVolumeSnapshot { Volume = 0.42, IsMuted = true }));
        UiState loaded = store.Load();

        Assert.Equal(7, loaded.LastChatId);
        Assert.Equal(14, loaded.RangeDays);
        Assert.NotNull(loaded.Volume);
        Assert.Equal(0.42, loaded.Volume!.Volume, 3);
        Assert.True(loaded.Volume.IsMuted);
    }

    [Fact]
    public void Load_FileFromOlderVersion_KeepsItsVolumeAndMute()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, """{ "LastChatId": 5, "VolumePercent": 35, "VolumeMuted": true }""");

        AudioVolumeSnapshot start = new JsonUiStateStore(file.Path).Load().StartVolume;

        Assert.Equal(0.35, start.Volume, 3);
        Assert.True(start.IsMuted);
    }

    [Fact]
    public void StartVolume_NewSnapshotWinsOverTheLegacyFields()
    {
        var state = new UiState(
            VolumePercent: 90, VolumeMuted: true,
            Volume: new AudioVolumeSnapshot { Volume = 0.2, IsMuted = false });

        Assert.Equal(0.2, state.StartVolume.Volume, 3);
        Assert.False(state.StartVolume.IsMuted);
    }

    [Fact]
    public void StartVolume_LegacyPercentOutOfRange_IsClamped()
    {
        Assert.Equal(1.0, new UiState(VolumePercent: 500).StartVolume.Volume, 3);
        Assert.Equal(0.0, new UiState(VolumePercent: -5).StartVolume.Volume, 3);
    }

    [Fact]
    public void Save_DoesNotWriteTheStartVolumeProperty()
    {
        using var file = TempPath.File();
        new JsonUiStateStore(file.Path).Save(UiState.Default);

        Assert.DoesNotContain("StartVolume", File.ReadAllText(file.Path));
    }

    [Fact]
    public void Load_BrokenFile_FallsBackToDefault()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, "{ not json");

        UiState loaded = new JsonUiStateStore(file.Path).Load();

        Assert.Equal(UiState.Default.RangeDays, loaded.RangeDays);
        Assert.Null(loaded.Volume);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheChatOfTheLastPlayedTrack()
    {
        using var file = TempPath.File();
        var store = new JsonUiStateStore(file.Path);

        store.Save(new UiState(LastPlayedFileId: 42, LastPlayedChatId: 7));
        UiState loaded = store.Load();

        Assert.Equal(42, loaded.LastPlayedFileId);
        Assert.Equal(7, loaded.LastPlayedChatId);
    }

    [Fact]
    public void Load_FileFromOlderVersion_HasNoChatForTheLastPlayedTrack()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, """{ "LastPlayedFileId": 42 }""");

        Assert.Equal(0, new JsonUiStateStore(file.Path).Load().LastPlayedChatId);
    }
}
