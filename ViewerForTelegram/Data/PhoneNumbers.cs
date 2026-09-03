namespace ViewerForTelegram.Data;

/// <summary>
/// Small helpers for phone numbers. Telegram stores numbers internally as a
/// plain digit sequence without a "+" (e.g. <c>4917647123238</c>).
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Digits only - "+", spaces, parentheses and dashes are dropped.</summary>
    public static string DigitsOnly(string? input) =>
        new((input ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Display form: "+" plus digits. An empty string stays empty.</summary>
    public static string ToPlusForm(string? input)
    {
        string digits = DigitsOnly(input);
        return digits.Length == 0 ? "" : "+" + digits;
    }
}
