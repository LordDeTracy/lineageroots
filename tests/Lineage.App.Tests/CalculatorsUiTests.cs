using Avalonia.Headless.XUnit;
using Lineage.App.ViewModels;

namespace Lineage.App.Tests;

public class CalculatorsUiTests
{
    [AvaloniaFact]
    public async Task EachCalculatorAnswersAsYouType()
    {
        var (vm, dialogs) = await Demo.OpenAsync();
        await vm.OpenCalculatorsCommand.ExecuteAsync(null);
        var c = Assert.Single(dialogs.Calculators);

        c.SoundexName = "Whitfeild";
        Assert.Equal("W314", c.SoundexCode);
        Assert.Contains("Whitfield", c.SoundexMatches);

        c.FromDate = "14 Mar 1842";
        c.ToDate = "9 Nov 1911";
        Assert.StartsWith("69 years, 7 months, 26 days", c.BetweenText);
        Assert.Contains("14 March 1842 was a Monday", c.BetweenText);

        c.AgeOnDate = "6 Jun 1841";
        c.AgeYears = "34";
        Assert.Equal("Born between 7 June 1806 and 6 June 1807.", c.BornText);

        c.CalendarIndex = 4;                                     // French Republican
        c.CalDay = "9"; c.CalMonth = "11"; c.CalYear = "2";
        Assert.Contains("Gregorian: 27 July 1794 (Sunday)", c.CalendarResult);
        c.CalendarIndex = 2;                                     // Quaker
        c.CalDay = "10"; c.CalMonth = "12"; c.CalYear = "1745";
        Assert.Contains("Written: 10 February 1745/46", c.CalendarResult);
        c.CalMonth = "14";
        Assert.Contains("doesn't exist", c.CalendarResult);
    }
}
