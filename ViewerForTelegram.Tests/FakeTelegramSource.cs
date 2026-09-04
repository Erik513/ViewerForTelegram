using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

/// <summary>In-memory stand-in for <see cref="ITelegramSource"/> in service tests.</summary>
internal sealed class FakeTelegramSource : ITelegramSource
{
    public List<AudioMessage> Audios { get; } = new();
    public int DownloadCalls { get; private set; }

    /// <summary>
    /// Optional custom download simulation. Default: writes
    /// <see cref="AudioMessage.SizeBytes"/> zero bytes and reports 100%.
    /// </summary>
    public Func<AudioMessage, string, IProgress<int>?, CancellationToken, Task>? DownloadBehavior { get; set; }

    public Task<IReadOnlyList<AudioMessage>> GetAudioMessagesSinceAsync(
        long chatId, DateTime sinceUtc, CancellationToken ct, int maxAudios = int.MaxValue,
        IProgress<int>? progress = null) =>
        Task.FromResult<IReadOnlyList<AudioMessage>>(
            Audios.Where(a => a.ChatId == chatId && a.DateUtc >= sinceUtc)
                  .OrderByDescending(a => a.DateUtc)
                  .Take(Math.Max(0, maxAudios))
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
