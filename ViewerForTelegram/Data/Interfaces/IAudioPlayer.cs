namespace ViewerForTelegram.Data.Interfaces;

/// <summary>What the player is currently doing.</summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}

/// <summary>
/// Plays one local audio file at a time. Knows nothing about Telegram or the
/// cache - it is handed a ready file path. Events are raised on the thread that
/// created the player (the UI thread), so handlers need no marshalling.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    PlaybackState State { get; }

    /// <summary>Total length of the loaded file, or <see cref="TimeSpan.Zero"/> if nothing is loaded.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// Encoded bit rate in kbit/s read from the loaded file's own headers when it
    /// can be determined (currently .mp3), otherwise <c>null</c>. This is the
    /// real audio rate - unlike file-size ÷ duration, it ignores cover art.
    /// </summary>
    int? BitrateKbps { get; }

    /// <summary>Current playback position; settable to seek.</summary>
    TimeSpan Position { get; set; }

    /// <summary>Volume from 0.0 to 1.0. Persists across <see cref="Load"/> calls.</summary>
    float Volume { get; set; }

    /// <summary>
    /// Raised when the volume was changed from outside the app (the Windows
    /// volume mixer). The argument is the new 0..1 value; setting
    /// <see cref="Volume"/> from a handler must not loop back.
    /// </summary>
    event EventHandler<float>? VolumeChangedExternally;

    /// <summary>Raised when playback reaches the end of the file on its own.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>Raised when the decoder fails during playback (bad/unsupported file).</summary>
    event EventHandler<Exception>? PlaybackFailed;

    /// <summary>
    /// Loads <paramref name="filePath"/> and gets ready to play from the start.
    /// Throws if the format cannot be decoded.
    /// </summary>
    void Load(string filePath);

    void Play();
    void Pause();

    /// <summary>Stops and unloads the current file.</summary>
    void Stop();
}
