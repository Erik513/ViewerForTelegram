using ViewerForTelegram.Logic;
using Xunit;

namespace ViewerForTelegram.Tests;

public class TrackSearchTests
{
    [Theory]
    [InlineData("vorname nachname", "vorname_nachname.mp3", true)]   // the reported case
    [InlineData("vorname nachname", "Vorname - Nachname (2020).flac", true)]
    [InlineData("vorname.nachname", "vorname nachname", true)]
    [InlineData("nachname vorname", "vorname_nachname.mp3", true)]   // order-independent
    [InlineData("vorname nachname", "vorname_othername.mp3", false)] // one term missing
    [InlineData("", "anything at all", true)]                        // no query matches all
    [InlineData("   ", "anything", true)]                            // separators only = no terms
    [InlineData("MP3", "song.mp3", true)]                            // extension is searchable
    [InlineData("live", "Song (Live).m4a", true)]
    public void Matches_IsSeparatorTolerant(string query, string text, bool expected)
    {
        Assert.Equal(expected, TrackSearch.Matches(text, TrackSearch.Terms(query)));
    }

    [Fact]
    public void Terms_NormalisesAndSplits()
    {
        Assert.Equal(new[] { "vorname", "nachname" }, TrackSearch.Terms("  Vorname__Nachname.. "));
        Assert.Empty(TrackSearch.Terms("---"));
    }
}
