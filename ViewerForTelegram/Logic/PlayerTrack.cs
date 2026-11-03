using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic;

/// <summary>
/// Which track the player panel shows. Browsing the list must not swap the
/// track out from under a running download or playback, so the list selection
/// only counts while the player is otherwise empty.
/// </summary>
public static class PlayerTrack
{
    public static AudioMessage? Choose(
        AudioMessage? downloading, AudioMessage? loaded, AudioMessage? selected) =>
        downloading ?? loaded ?? selected;
}
