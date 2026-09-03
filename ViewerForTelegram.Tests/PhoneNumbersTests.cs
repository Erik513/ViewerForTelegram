using ViewerForTelegram.Data;

namespace ViewerForTelegram.Tests;

public class PhoneNumbersTests
{
    [Theory]
    [InlineData("+49 176 4712 3238", "4917647123238")]
    [InlineData("+49-176-47123238", "4917647123238")]
    [InlineData("(0176) 47123238", "017647123238")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("no digits", "")]
    public void DigitsOnly_RemovesEverythingButDigits(string? input, string expected)
    {
        Assert.Equal(expected, PhoneNumbers.DigitsOnly(input));
    }

    [Theory]
    [InlineData("+4917647123238", "+4917647123238")]
    [InlineData("49 176 47123238", "+4917647123238")]
    [InlineData("  +49-176-4712-3238  ", "+4917647123238")]
    public void ToPlusForm_NormalizesToPlusAndDigits(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumbers.ToPlusForm(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("abc")]
    public void ToPlusForm_EmptyStaysEmpty(string? input)
    {
        Assert.Equal("", PhoneNumbers.ToPlusForm(input));
    }
}
