using System.Reflection;
using System.Text.RegularExpressions;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.UI.Localization;

namespace ViewerForTelegram.Tests;

public class LocTests
{
    private static Dictionary<string, string> Dict(string field) =>
        (Dictionary<string, string>)typeof(Loc)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static readonly Dictionary<string, string> En = Dict("English");
    private static readonly Dictionary<string, string> De = Dict("German");

    [Fact]
    public void BothLanguages_HaveTheSameKeys()
    {
        Assert.Equal(
            En.Keys.OrderBy(k => k),
            De.Keys.OrderBy(k => k));
    }

    [Fact]
    public void PlaceholdersMatch_BetweenLanguages()
    {
        static SortedSet<string> Slots(string s) =>
            new(Regex.Matches(s, @"\{(\d+)\}").Select(m => m.Value));

        foreach (string key in En.Keys)
        {
            Assert.True(
                Slots(En[key]).SetEquals(Slots(De[key])),
                $"'{key}': placeholders differ ({En[key]} | {De[key]})");
        }
    }

    [Fact]
    public void NoValueIsBlank()
    {
        Assert.All(En.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
        Assert.All(De.Values, v => Assert.False(string.IsNullOrWhiteSpace(v)));
    }

    [Fact]
    public void Current_SwitchesTheLookup()
    {
        Loc.Register();

        Loc.Current = DisplayLanguage.English;
        Assert.Equal(DisplayLanguage.English, Loc.Current);
        Assert.Equal("Newest 50 audios", Loc.T("range.newest", 50));

        Loc.Current = DisplayLanguage.German;
        Assert.Equal(DisplayLanguage.German, Loc.Current);
        Assert.Equal("Neueste 50 Audios", Loc.T("range.newest", 50));

        Loc.Current = DisplayLanguage.English;   // leave the default in place
    }

    [Fact]
    public void MissingKey_FallsBackToTheKey_NotACrash()
    {
        Loc.Register();
        Assert.Equal("no.such.key", Loc.S("no.such.key"));
    }
}
