using Lineage.Core.Model;

namespace Lineage.Core.Tests;

public class GenDateTests
{
    [Theory]
    [InlineData("11 JAN 1901")]
    [InlineData("JAN 1901")]
    [InlineData("1901")]
    [InlineData("ABT 1905")]
    [InlineData("BEF 5 MAR 1980")]
    [InlineData("AFT 1901")]
    [InlineData("CAL 1905")]
    [InlineData("EST 1985")]
    [InlineData("BET 1932 AND 1933")]
    [InlineData("FROM 1920 TO 1960")]
    [InlineData("3 JAN 1691/1692")]
    [InlineData("28/29 SEP 1764")]
    [InlineData("Deceased")]
    public void RoundTrips(string text) => Assert.Equal(text, GenDate.Parse(text).ToGedcom());

    // The app shows dates in words ("about 1905"); typing them back must give the same date.
    [Theory]
    [InlineData("about 12 Mar 1905", "ABT 12 MAR 1905")]
    [InlineData("before 1980", "BEF 1980")]
    [InlineData("after Jan 1901", "AFT JAN 1901")]
    [InlineData("calculated 1905", "CAL 1905")]
    [InlineData("estimated 1985", "EST 1985")]
    [InlineData("between 1932 and 1933", "BET 1932 AND 1933")]
    [InlineData("from 1920 to 1960", "FROM 1920 TO 1960")]
    public void ParsesWrittenOutWords(string text, string gedcom) => Assert.Equal(gedcom, GenDate.Parse(text).ToGedcom());

    [Fact]
    public void ParsesStructure()
    {
        var d = GenDate.Parse("abt 2 february 1902");
        Assert.Equal(DateQualifier.About, d.Qualifier);
        Assert.Equal((1902, 2, 2), (d.Year, d.Month, d.Day));
        Assert.Equal("ABT 2 FEB 1902", d.ToGedcom());
    }

    [Fact]
    public void AqStyleMixedCasePeriodParses()
    {
        var d = GenDate.Parse("From 1925 to 1945");
        Assert.Equal(DateKind.Period, d.Kind);
        Assert.Equal(1945, d.End!.Year);
    }

    [Fact]
    public void UnknownTextIsKept()
    {
        var d = GenDate.Parse("In Progress");
        Assert.Equal(DateKind.Text, d.Kind);
        Assert.Equal("In Progress", d.ToGedcom());
    }

    [Fact]
    public void SortKeyOrdersChronologically()
    {
        Assert.True(GenDate.Parse("5 MAR 1980").SortKey > GenDate.Parse("JAN 1980").SortKey);
        Assert.True(GenDate.Parse("1980").SortKey > GenDate.Parse("31 DEC 1979").SortKey);
    }
}
