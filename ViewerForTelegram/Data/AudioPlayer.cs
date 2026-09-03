using NAudio.Flac;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ViewerForTelegram.Data.Interfaces;
// Our own playback-state enum, not NAudio's (which is also in scope via NAudio.Wave).
using PlaybackState = ViewerForTelegram.Data.Interfaces.PlaybackState;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="IAudioPlayer"/> on top of NAudio. .wav / .mp3 are decoded
/// natively, .flac via <see cref="FlacReader"/> (Media Foundation's FLAC source
/// does not report a duration), everything else (m4a, aac, wma, …) via Windows
/// Media Foundation. ogg/opus are not covered and surface as a load error.
/// Output goes through a single <see cref="WaveOutEvent"/>.
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    private SynchronizationContext _sync = new();
    private WaveOutEvent? _output;
    private WaveStream? _stream;          // the decoder - position / duration / seek
    private AudioFileReader? _fileReader; // set only for the AudioFileReader path (has its own Volume)
    private SampleChannel? _sampleChannel; // set only for the FLAC path (volume goes here)
    private float _volume = 0.1f;
    private bool _stopIsIntentional;

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public TimeSpan Duration => _stream?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _stream?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_stream is null)
            {
                return;
            }
            TimeSpan clamped = value < TimeSpan.Zero ? TimeSpan.Zero
                : value > _stream.TotalTime ? _stream.TotalTime
                : value;
            _stream.CurrentTime = clamped;
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_fileReader is not null)
            {
                _fileReader.Volume = _volume;
            }
            if (_sampleChannel is not null)
            {
                _sampleChannel.Volume = _volume;
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

        ISampleProvider output;
        if (filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
        {
            var flac = new FlacReader(filePath);
            _stream = flac;
            _sampleChannel = new SampleChannel(flac, forceStereo: false) { Volume = _volume };
            output = _sampleChannel;
        }
        else
        {
            _fileReader = new AudioFileReader(filePath) { Volume = _volume };
            _stream = _fileReader;
            output = _fileReader;
        }

        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(output);
        State = PlaybackState.Stopped;
    }

    public void Play()
    {
        if (_output is null || _stream is null)
        {
            return;
        }

        // Restart from the beginning if the last playback ran to the end.
        if (State == PlaybackState.Stopped && _stream.Position >= _stream.Length)
        {
            _stream.Position = 0;
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

        _stream?.Dispose();
        _stream = null;
        _fileReader = null;
        _sampleChannel = null;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopIsIntentional)
        {
            return;
        }

        bool reachedEnd = _stream is not null && _stream.Position >= _stream.Length;
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
