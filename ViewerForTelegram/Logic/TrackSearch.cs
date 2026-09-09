using System.Text;

namespace ViewerForTelegram.Logic;

/// <summary>
/// Separator-tolerant text matching for the track-list filter. Any run of
/// non-alphanumeric characters (spaces, <c>_ - . +</c> …) counts as a single
/// separator on both the query and the text, so "vorname nachname" matches
/// "vorname_nachname.mp3", "vorname-nachname", "Vorname.Nachname", etc.
/// </summary>
public static class TrackSearch
{
    /// <summary>The search-box entry as lower-case terms (order-independent).</summary>
    public static string[] Terms(string text) =>
        Normalize(text).Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when every term appears somewhere in the (same-normalised) text. No terms ⇒ always true.</summary>
    public static bool Matches(string text, string[] terms)
    {
        if (terms.Length == 0)
        {
            return true;
        }

        string haystack = Normalize(text);
        foreach (string term in terms)
        {
            if (!haystack.Contains(term, System.StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }
                pendingSpace = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSpace = true;   // collapses runs, drops leading/trailing
            }
        }
        return sb.ToString();
    }
}
