using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

/// <summary>In-Memory-Ersatz für <see cref="ITelegramSource"/> in Service-Tests.</summary>
internal sealed class FakeTelegramSource : ITelegramSource
{
    public List<AudioMessage> Audios { get; } = new();
    public int DownloadCalls { get; private set; }

    /// <summary>
    /// Optionale eigene Download-Simulation. Standard: schreibt
    /// <see cref="AudioMessage.SizeBytes"/> Nullbytes und meldet 100 %.
    /// </summary>
    public Func<AudioMessage, string, IProgress<int>?, CancellationToken, Task>? DownloadBehavior { get; set; }

    public Task<IReadOnlyList<AudioMessage>> GetAudioMessagesSinceAsync(
        long chatId, DateTime sinceUtc, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AudioMessage>>(
            Audios.Where(a => a.ChatId == chatId && a.DateUtc >= sinceUtc)
                  .OrderByDescending(a => a.DateUtc)
                  .ToList());

    public async Task DownloadAsync(
        AudioMessage message, string targetPath, IProgress<int>? progress, CancellationToken ct)
    {
        DownloadCalls++;

        if (DownloadBehavior is not null)
        {
            await DownloadBehavior(message, targetPath, progress, ct);
            return;
        }

        ct.ThrowIfCancellationRequested();
        await File.WriteAllBytesAsync(targetPath, new byte[message.SizeBytes], ct);
        progress?.Report(100);
    }

    public Task ConnectAsync(Func<Task<string>> requestVerificationCode, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<TelegramChat>> GetChatsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TelegramChat>>(new List<TelegramChat>());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
