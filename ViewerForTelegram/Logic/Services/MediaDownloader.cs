using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic.Services;

/// <summary>
/// Sorgt dafür, dass eine Audiodatei lokal im Cache liegt. Bündelt parallele
/// Anfragen auf dieselbe Datei, erlaubt Abbruch pro Datei und stutzt den Cache
/// nach jedem Download auf die Obergrenze.
/// </summary>
public sealed class MediaDownloader
{
    private readonly ITelegramSource _telegram;
    private readonly IMediaCache _cache;
    private readonly long _cacheLimitBytes;

    private readonly object _gate = new();
    private readonly HashSet<long> _inFlight = new();
    private readonly Dictionary<long, CancellationTokenSource> _cts = new();

    public MediaDownloader(ITelegramSource telegram, IMediaCache cache, long cacheLimitBytes)
    {
        _telegram = telegram;
        _cache = cache;
        _cacheLimitBytes = cacheLimitBytes;
    }

    /// <summary>
    /// Gibt den lokalen Pfad zurück und lädt die Datei vorher herunter, falls
    /// sie noch nicht vollständig im Cache liegt. Ein zweiter Aufruf für
    /// dieselbe Datei wartet auf den laufenden Download, statt ihn erneut zu
    /// starten.
    /// </summary>
    /// <param name="progress">Fortschritt 0..100, optional.</param>
    /// <exception cref="OperationCanceledException">
    /// Abgebrochen (per <see cref="Cancel"/> / <see cref="CancelAll"/>) oder ein
    /// paralleler Download derselben Datei lief in die Zeitüberschreitung.
    /// </exception>
    public async Task<string> EnsureLocalAsync(
        AudioMessage audio, IProgress<int>? progress, CancellationToken ct)
    {
        string path = _cache.GetPath(audio);
        if (_cache.Contains(audio))
        {
            return path;
        }

        bool mine;
        lock (_gate)
        {
            mine = _inFlight.Add(audio.FileId);
        }

        if (!mine)
        {
            // Läuft schon woanders - warten (mit Obergrenze, damit ein hängender
            // Download nicht ewig einen wartenden Aufrufer blockiert).
            for (int i = 0; i < 600; i++)
            {
                lock (_gate)
                {
                    if (!_inFlight.Contains(audio.FileId))
                    {
                        break;
                    }
                }
                await Task.Delay(200, ct);
            }
            if (!_cache.Contains(audio))
            {
                throw new OperationCanceledException(); // abgebrochen oder Zeitüberschreitung
            }
            return path;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_gate)
        {
            _cts[audio.FileId] = linked;
        }
        try
        {
            progress?.Report(0);
            await _telegram.DownloadAsync(audio, path, progress, linked.Token);
            return path;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(audio.FileId);
                _cts.Remove(audio.FileId);
            }
            linked.Dispose();
            _cache.PruneToLimit(_cacheLimitBytes); // Sicherheitsnetz innerhalb der Sitzung
        }
    }

    /// <summary>Bricht einen laufenden Download für diese Datei ab.</summary>
    public void Cancel(long fileId)
    {
        lock (_gate)
        {
            if (_cts.TryGetValue(fileId, out CancellationTokenSource? cts))
            {
                cts.Cancel();
            }
        }
    }

    /// <summary>Bricht alle laufenden Downloads ab (z. B. beim Abmelden).</summary>
    public void CancelAll()
    {
        lock (_gate)
        {
            foreach (CancellationTokenSource cts in _cts.Values.ToArray())
            {
                cts.Cancel();
            }
        }
    }
}
