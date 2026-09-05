using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Flac;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ViewerForTelegram.Data.Interfaces;
// Our own playback-state enum, not NAudio's (which is also in scope via NAudio.Wave).
using PlaybackState = ViewerForTelegram.Data.Interfaces.PlaybackState;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="IAudioPlayer"/> on top of NAudio. .flac goes through
/// <see cref="FlacReader"/> (Media Foundation's FLAC source reports no
/// duration); .aiff/.aif go through NAudio's own <see cref="AiffFileReader"/>
/// (Media Foundation has no AIFF source reader at all); everything else is
/// tried with <see cref="AudioFileReader"/> first (native PCM / IEEE-float
/// .wav and .mp3) and falls back to <see cref="MediaFoundationReader"/> for
/// anything it cannot open - notably WAVE_FORMAT_EXTENSIBLE .wav files and
/// m4a / aac / wma. ogg/opus are not covered and surface as a load error.
/// Output goes through one <see cref="WaveOutEvent"/>.
/// </summary>
public sealed class AudioPlayer : IAudioPlayer
{
    private SynchronizationContext _sync = new();
    private WaveOutEvent? _output;
    private WaveStream? _stream;          // the decoder - position / duration / seek
    private AudioFileReader? _fileReader; // set only for the AudioFileReader path (has its own Volume)
    private SampleChannel? _sampleChannel; // set only for the FLAC path (volume goes here)
    private float _volume = 0.5f;
    private bool _stopIsIntentional;

    // Volume is routed through this process's own entry in the Windows volume
    // mixer (its per-app audio session), so this slider and the "Viewer for
    // Telegram" slider in the mixer are the same control and stay in sync. The
    // decoder chain is left at unity gain; the session does the attenuation.
    private MMDeviceEnumerator? _deviceEnum;
    private MMDevice? _mmDevice;
    private AudioSessionControl? _session;
    private SimpleAudioVolume? _sessionVolume;
    private SessionEvents? _sessionEvents;
    private float _lastAppliedScalar = -1f;

    // The Windows volume mixer maps a per-app session's slider position roughly
    // 1:1 to the linear SimpleAudioVolume scalar, so this slider does the same -
    // that keeps it and the mixer's "Viewer for Telegram" slider at the same
    // position and rising at the same, even rate.
    private static float ToScalar(float sliderPos) => Math.Clamp(sliderPos, 0f, 1f);
    private static float ToSliderPos(float scalar) => Math.Clamp(scalar, 0f, 1f);

    // Applied to NAudio's own linear volume only when there is no per-app session
    // to route through (rare - e.g. no audio device). A -40 dB perceptual taper
    // so a linear amplitude doesn't feel far too loud in the lower half.
    private float FallbackGain => _volume <= 0f ? 0f : MathF.Pow(10f, (_volume - 1f) * 2f);

    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    public TimeSpan Duration => _stream?.TotalTime ?? TimeSpan.Zero;

    public int? BitrateKbps { get; private set; }

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
            ApplyVolume();
        }
    }

    private void ApplyVolume()
    {
        if (_sessionVolume is not null)
        {
            try
            {
                float scalar = ToScalar(_volume);
                _lastAppliedScalar = scalar;
                _sessionVolume.Volume = scalar;
                SetChannelVolume(1f);   // session does the attenuation
                return;
            }
            catch
            {
                ReleaseSession();   // session went stale - drop to the fallback
            }
        }

        SetChannelVolume(FallbackGain);
    }

    private void SetChannelVolume(float gain)
    {
        if (_fileReader is not null)
        {
            _fileReader.Volume = gain;
        }
        if (_sampleChannel is not null)
        {
            _sampleChannel.Volume = gain;
        }
    }

    /// <summary>Raised when the volume was changed from the Windows volume mixer; the argument is the new 0..1 slider position.</summary>
    public event EventHandler<float>? VolumeChangedExternally;

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
            output = OpenFlac(filePath);
        }
        else if (filePath.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase)
                 || filePath.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
        {
            // Media Foundation has no AIFF source reader at all ("stream type
            // not supported", 0xC00D36C4) - AudioFileReader's fallback to it
            // fails the same way. NAudio ships its own AiffFileReader though.
            output = WrapAsSampleChannel(new AiffFileReader(filePath));
        }
        else
        {
            try
            {
                _fileReader = new AudioFileReader(filePath) { Volume = FallbackGain };
                _stream = _fileReader;
                output = _fileReader;
            }
            catch (Exception ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                // AudioFileReader routes anything that is not plain PCM / IEEE
                // float through ACM - a WAVE_FORMAT_EXTENSIBLE .wav (common for
                // 24-bit / multichannel exports) then fails with
                // "NoDriver calling acmFormatSuggest". Media Foundation decodes
                // those (and m4a/aac/wma) directly.
                AppLog.Error("Audio",
                    $"AudioFileReader failed for {Path.GetExtension(filePath)}: " +
                    $"{ex.GetType().Name}: {ex.Message} - falling back to Media Foundation");

                output = WrapAsSampleChannel(new MediaFoundationReader(filePath));
            }
        }

        BitrateKbps = ReadMp3BitrateKbps(filePath);

        _output = new WaveOutEvent();
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(output);
        State = PlaybackState.Stopped;

        AcquireSession();
        ApplyVolume();
    }

    private Stream? _ownedStream;   // a FileStream we opened ourselves and must dispose

    /// <summary>
    /// Opens a .flac file. Falls back for the two common failures of
    /// <see cref="FlacReader"/>: a non-standard leading ID3v2 tag ("fLaC" sync
    /// not found - re-open past the tag) and anything else (Media Foundation).
    /// </summary>
    private ISampleProvider OpenFlac(string filePath)
    {
        try
        {
            return WrapAsSampleChannel(new FlacReader(filePath));
        }
        catch (Exception ex)
        {
            long skip = LeadingId3v2Length(filePath);
            AppLog.Error("Audio",
                $"FlacReader failed: {ex.GetType().Name}: {ex.Message}" +
                (skip > 0 ? $" - retrying past a {skip}-byte ID3v2 tag" : " - falling back to Media Foundation"));

            if (skip > 0)
            {
                try
                {
                    var fs = File.OpenRead(filePath);
                    fs.Position = skip;
                    var flac = new FlacReader(fs);
                    _ownedStream = fs;
                    return WrapAsSampleChannel(flac);
                }
                catch (Exception ex2)
                {
                    AppLog.Error("Audio", $"FlacReader still failed past the tag: {ex2.Message} - Media Foundation");
                }
            }

            return WrapAsSampleChannel(new MediaFoundationReader(filePath));
        }
    }

    /// <summary>Sets <see cref="_stream"/>/<see cref="_sampleChannel"/> for <paramref name="stream"/> and returns the channel.</summary>
    private ISampleProvider WrapAsSampleChannel(WaveStream stream)
    {
        _stream = stream;
        _sampleChannel = new SampleChannel(stream, forceStereo: false) { Volume = FallbackGain };
        return _sampleChannel;
    }

    /// <summary>Length of a leading ID3v2 tag (some taggers wrongly add one to FLAC), or 0.</summary>
    private static long LeadingId3v2Length(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            Span<byte> h = stackalloc byte[10];
            if (fs.Read(h) < 10 || h[0] != 'I' || h[1] != 'D' || h[2] != '3')
            {
                return 0;
            }
            // bytes 6..9 are a 7-bit "syncsafe" integer: the tag size after the header
            int size = (h[6] << 21) | (h[7] << 14) | (h[8] << 7) | h[9];
            bool hasFooter = (h[5] & 0x10) != 0;
            return 10 + size + (hasFooter ? 10 : 0);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// The real MP3 audio bit rate from its frame / Xing header (so a file with
    /// heavy cover art doesn't inflate it the way file-size ÷ duration does).
    /// <c>null</c> for non-mp3 or if it can't be read.
    /// </summary>
    private static int? ReadMp3BitrateKbps(string filePath)
    {
        if (!filePath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var reader = new Mp3FileReader(filePath);

            // VBR file with a Xing/Info/VBRI header -> exact average from its counters.
            if (reader.XingHeader is { } xing && xing.Bytes > 0 && reader.TotalTime.TotalSeconds > 0)
            {
                return (int)Math.Round(xing.Bytes * 8 / reader.TotalTime.TotalSeconds / 1000);
            }

            // CBR -> the frame-header bit rate is the real one.
            int bytesPerSec = reader.Mp3WaveFormat.AverageBytesPerSecond;
            return bytesPerSec > 0 ? (int)Math.Round(bytesPerSec * 8 / 1000d) : null;
        }
        catch
        {
            return null;
        }
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

        if (_session is null)
        {
            // The session sometimes only appears once audio is actually running.
            AcquireSession();
            ApplyVolume();
        }
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

        try { _ownedStream?.Dispose(); } catch { }
        _ownedStream = null;

        ReleaseSession();
    }

    // ---------- Windows per-app volume session ----------

    private void AcquireSession()
    {
        ReleaseSession();
        try
        {
            _deviceEnum ??= new MMDeviceEnumerator();
            _mmDevice = _deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            SessionCollection sessions = _mmDevice.AudioSessionManager.Sessions;
            uint pid = (uint)Environment.ProcessId;

            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl candidate = sessions[i];
                if (candidate.GetProcessID != pid)
                {
                    continue;
                }

                _session = candidate;
                _sessionVolume = candidate.SimpleAudioVolume;
                _sessionEvents = new SessionEvents(OnSessionVolume);
                candidate.RegisterEventClient(_sessionEvents);
                return;
            }
        }
        catch
        {
            ReleaseSession();
        }
    }

    private void ReleaseSession()
    {
        try
        {
            if (_session is not null && _sessionEvents is not null)
            {
                _session.UnRegisterEventClient(_sessionEvents);
            }
        }
        catch { }

        _sessionEvents = null;
        try { _sessionVolume?.Dispose(); } catch { }
        _sessionVolume = null;
        try { _session?.Dispose(); } catch { }
        _session = null;
        try { _mmDevice?.Dispose(); } catch { }
        _mmDevice = null;
        _lastAppliedScalar = -1f;
    }

    private void OnSessionVolume(float scalar, bool muted)
    {
        // Ignore the notification our own ApplyVolume just triggered - it comes
        // back bit-for-bit equal, whereas a real mixer drag is a step of ~8%.
        if (!muted && _lastAppliedScalar > 0f
            && MathF.Abs(scalar - _lastAppliedScalar) < _lastAppliedScalar * 0.01f)
        {
            return;
        }

        float pos = muted ? 0f : ToSliderPos(scalar);
        _volume = pos;
        _lastAppliedScalar = ToScalar(pos);
        _sync.Post(_ => VolumeChangedExternally?.Invoke(this, pos), null);
    }

    private sealed class SessionEvents : IAudioSessionEventsHandler
    {
        private readonly Action<float, bool> _onVolume;
        public SessionEvents(Action<float, bool> onVolume) => _onVolume = onVolume;

        public void OnVolumeChanged(float volume, bool isMuted) => _onVolume(volume, isMuted);

        public void OnDisplayNameChanged(string displayName) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnStateChanged(AudioSessionState state) { }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) { }
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

    public void Dispose()
    {
        Stop();
        try { _deviceEnum?.Dispose(); } catch { }
        _deviceEnum = null;
    }
}
