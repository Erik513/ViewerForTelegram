namespace ViewerForTelegram.Data;

/// <summary>
/// Kleine Helfer für Telefonnummern. Telegram führt Nummern intern als
/// reine Ziffernfolge ohne "+" (z. B. <c>4917647123238</c>).
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Nur die Ziffern - "+", Leerzeichen, Klammern, Bindestriche fallen weg.</summary>
    public static string DigitsOnly(string? input) =>
        new((input ?? "").Where(char.IsDigit).ToArray());

    /// <summary>Anzeigeform: "+" plus Ziffern. Leerer String bleibt leer.</summary>
    public static string ToPlusForm(string? input)
    {
        string digits = DigitsOnly(input);
        return digits.Length == 0 ? "" : "+" + digits;
    }
}
