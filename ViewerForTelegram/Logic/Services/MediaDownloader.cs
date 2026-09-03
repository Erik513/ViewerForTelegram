using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic.Services;

/// <summary>
/// Makes sure an audio file is present locally in the cache. Coalesces parallel
/// requests for the same file, allows per-file cancellation, and trims the
/// cache to the size limit after every download.
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
    /// Returns the local path, downloading the file first if it is not yet fully
    /// in the cache. A second call for the same file waits for the running
    /// download instead of starting it again.
    /// </summary>
    /// <param name="progress">Progress 0..100, optional.</param>
    /// <exception cref="OperationCanceledException">
    /// Cancelled (via <see cref="Cancel"/> / <see cref="CancelAll"/>) or a
    /// parallel download of the same file ran into the timeout.
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
            // Already running elsewhere - wait (with an upper bound so a hung
            // download does not block a waiting caller forever).
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
                throw new OperationCanceledException(); // cancelled or timed out
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
            _cache.PruneToLimit(_cacheLimitBytes); // safety net within the session
        }
    }

    /// <summary>Cancels a running download for this file.</summary>
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

    /// <summary>Cancels all running downloads (e.g. on logout).</summary>
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
