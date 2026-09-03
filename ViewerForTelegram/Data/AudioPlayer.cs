using NAudio.Wave;
using ViewerForTelegram.Data.Interfaces;
// Our own playback-state enum, not NAudio's (which is also in scope via NAudio.Wave).
using PlaybackState = ViewerForTelegram.Data.Interfaces.PlaybackState;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="IAudioPlayer"/> on top of NAudio. <see cref="AudioFileReader"/>
/// handles .wav / .mp3 natively and everything else (m4a, aac, flac, wma) via
/// Windows Media Foundation; ogg/opus are not covered and surface as a load
/// error. Output goes through a single <see cref="WaveOutEvent"/>.
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    private SynchronizationContext _sync = new();
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private float _volume = 0.1f;
    private bool _stopIsIntentional;

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader is null)
            {
                return;
            }
            TimeSpan clamped = value < TimeSpan.Zero ? TimeSpan.Zero
                : value > _reader.TotalTime ? _reader.TotalTime
                : value;
            _reader.CurrentTime = clamped;
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_reader is not null)
            {
                _reader.Volume = _volume;
            }
        }
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler<Exception>? PlaybackFailed;

    public void Load(string filePath)
    {
        Stop();

        // Load is always called from the UI thread - capture its context so the
        // PlaybackStopped callback (raised on a pool thread) can post events back.
        _sync = SynchronizationContext.Current ?? _sync;

        _reader = new AudioFileReader(filePath) { Volume = _volume };
        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(_reader);
        State = PlaybackState.Stopped;
    }

    public void Play()
    {
        if (_output is null || _reader is null)
        {
            return;
        }

        // Restart from the beginning if the last playback ran to the end.
        if (State == PlaybackState.Stopped
            && _reader.Position >= _reader.Length)
        {
            _reader.Position = 0;
        }

        _output.Play();
        State = PlaybackState.Playing;
    }

    public void Pause()
    {
        if (_output is null || State != PlaybackState.Playing)
        {
            return;
        }
        _output.Pause();
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        State = PlaybackState.Stopped;

        if (_output is not null)
        {
            _stopIsIntentional = true;
            _output.PlaybackStopped -= OnPlaybackStopped;
            try { _output.Stop(); } catch { }
            _output.Dispose();
            _output = null;
            _stopIsIntentional = false;
        }

        _reader?.Dispose();
        _reader = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopIsIntentional)
        {
            return;
        }

        bool reachedEnd = _reader is not null && _reader.Position >= _reader.Length;
        State = PlaybackState.Stopped;

        _sync.Post(_ =>
        {
            if (e.Exception is not null)
            {
                PlaybackFailed?.Invoke(this, e.Exception);
            }
            else if (reachedEnd)
            {
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }, null);
    }

    public void Dispose() => Stop();
}
