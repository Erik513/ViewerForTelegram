using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;

namespace ViewerForTelegram.Tests;

public class AudioPlayerTests
{
    // A minimal 16-bit mono PCM WAV of the given length, filled with silence.
    private static void WriteSilenceWav(string path, int seconds, int sampleRate = 8000)
    {
        int dataBytes = seconds * sampleRate * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                       // fmt chunk size
        w.Write((short)1);                 // PCM
        w.Write((short)1);                 // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);           // byte rate
        w.Write((short)2);                 // block align
        w.Write((short)16);                // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        w.Write(new byte[dataBytes]);
    }

    [Fact]
    public void Load_ReadsDuration_AndSeeks_WithoutTouchingAnAudioDevice()
    {
        using var dir = TempPath.Dir();
        string wav = Path.Combine(dir.Path, "tone.wav");
        WriteSilenceWav(wav, seconds: 2);

        using IAudioPlayer player = new AudioPlayer();
        player.Load(wav);

        Assert.Equal(PlaybackState.Stopped, player.State);
        Assert.InRange(player.Duration.TotalSeconds, 1.9, 2.1);

        player.Position = TimeSpan.FromSeconds(1);
        Assert.InRange(player.Position.TotalSeconds, 0.9, 1.1);

        // Clamped, not thrown.
        player.Position = TimeSpan.FromSeconds(99);
        Assert.InRange(player.Position.TotalSeconds, 1.9, 2.1);

        player.Stop();
        Assert.Equal(PlaybackState.Stopped, player.State);
        Assert.Equal(TimeSpan.Zero, player.Duration);
    }

    [Fact]
    public void Volume_IsClampedAndKept()
    {
        using IAudioPlayer player = new AudioPlayer { Volume = 2f };
        Assert.Equal(1f, player.Volume);

        player.Volume = -1f;
        Assert.Equal(0f, player.Volume);

        player.Volume = 0.5f;
        Assert.Equal(0.5f, player.Volume);
    }

    [Fact]
    public void Load_UnreadableFile_Throws()
    {
        using var dir = TempPath.Dir();
        string bad = Path.Combine(dir.Path, "not-audio.wav");
        File.WriteAllText(bad, "this is not a wav file");

        using IAudioPlayer player = new AudioPlayer();
        Assert.ThrowsAny<Exception>(() => player.Load(bad));
    }
}
