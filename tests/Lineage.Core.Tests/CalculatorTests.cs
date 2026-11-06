using Lineage.Core.Analysis;

namespace Lineage.Core.Tests;

public class CalculatorTests
{
    [Fact]
    public void TimeBetweenDates() =>
        Assert.Equal("69 years, 7 months, 26 days (25,441 days)", Calculators.Between(new DateTime(1842, 3, 14), new DateTime(1911, 11, 9)));

    [Fact]
    public void BirthFromAnAge()
    {
        Assert.Equal((new DateTime(1806, 6, 7), new DateTime(1807, 6, 6)), Calculators.BornFromAge(new DateTime(1841, 6, 6), 34));
        Assert.Equal((new DateTime(1900, 3, 10), new DateTime(1900, 3, 10)), Calculators.BornFromAge(new DateTime(1911, 5, 15), 11, 2, 5));
    }

    [Fact]
    public void JulianAndOldStyle()
    {
        Assert.Equal(new DateTime(1741, 3, 25), Calculators.FromJulian(1741, 3, 14));     // 11 days behind in the 1700s
        Assert.Equal((1752, 9, 2), Calculators.ToJulian(new DateTime(1752, 9, 13)));      // Britain's change: 2 Sep was followed by 14 Sep
        Assert.Equal("14 March 1741/42", Calculators.OldStyle(1742, 3, 14));
        Assert.Equal("25 March 1742", Calculators.OldStyle(1742, 3, 25));
    }

    [Fact]
    public void QuakerMonthsCountedFromMarchBefore1752()
    {
        Assert.Equal("5 May 1745", Calculators.Quaker(5, 3, 1745)!.Value.Written);
        Assert.Equal(new DateTime(1745, 5, 16), Calculators.Quaker(5, 3, 1745)!.Value.Gregorian);
        Assert.Equal("10 February 1745/46", Calculators.Quaker(10, 12, 1745)!.Value.Written);
        Assert.Equal("5 March 1800", Calculators.Quaker(5, 3, 1800)!.Value.Written);   // after 1752 the 3rd month is March
        Assert.Null(Calculators.Quaker(5, 13, 1800));
    }

    [Fact]
    public void HebrewDates()
    {
        Assert.Equal("23 Tevet 5760", Calculators.ToHebrew(new DateTime(2000, 1, 1)));
        Assert.Equal(new DateTime(2000, 1, 1), Calculators.FromHebrew(5760, 4, 23));
        Assert.Equal(13, Calculators.HebrewMonthNames(5760).Count);                       // 5760 was a leap year
        Assert.Null(Calculators.ToHebrew(new DateTime(1500, 1, 1)));
    }

    [Fact]
    public void FrenchRepublicanDates()
    {
        Assert.Equal(new DateTime(1794, 7, 27), Calculators.FromRepublican(2, 11, 9));    // 9 Thermidor An II
        Assert.Equal("9 Thermidor An II", Calculators.ToRepublican(new DateTime(1794, 7, 27)));
        Assert.Equal("18 Brumaire An VIII", Calculators.ToRepublican(new DateTime(1799, 11, 9)));
        Assert.Null(Calculators.ToRepublican(new DateTime(1806, 6, 1)));
        Assert.Null(Calculators.FromRepublican(2, 13, 7));                                 // no 7th extra day
    }
}
