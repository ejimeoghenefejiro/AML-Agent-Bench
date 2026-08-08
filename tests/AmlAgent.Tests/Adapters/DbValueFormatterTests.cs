using System.Globalization;
using System.Threading;
using AmlAgent.Adapters.Normalisation;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Regression coverage for a real bug found via the live PostgreSQL test:
/// DateTime.ToString() and decimal.ToString() use the CURRENT thread
/// culture by default, not invariant -- so on a non-US-English machine
/// (e.g. en-GB), a raw ToString() renders a timestamp as
/// "19/01/2026 10:00:00" instead of ISO 8601, which then fails
/// TransactionRowMapper's InvariantCulture parsing. These tests run under
/// a forced en-GB culture specifically to catch that regression again if
/// DbValueFormatter is ever changed back to a bare ToString().
/// </summary>
public class DbValueFormatterTests
{
    [Fact]
    public void ToFieldString_DateTime_IsCultureInvariantEvenUnderNonUsLocale()
    {
        RunUnderCulture("en-GB", () =>
        {
            var dt = new DateTime(2026, 1, 19, 10, 0, 0, DateTimeKind.Utc);
            var result = DbValueFormatter.ToFieldString(dt);
            Assert.StartsWith("2026-01-19T10:00:00", result);
            Assert.DoesNotContain("19/01/2026", result);
        });
    }

    [Fact]
    public void ToFieldString_Decimal_IsCultureInvariantEvenUnderCommaDecimalLocale()
    {
        RunUnderCulture("de-DE", () => // uses ',' as decimal separator
        {
            var result = DbValueFormatter.ToFieldString(24500.50m);
            Assert.Equal("24500.50", result);
        });
    }

    [Fact]
    public void ToFieldString_Null_ReturnsNull()
    {
        Assert.Null(DbValueFormatter.ToFieldString(null));
        Assert.Null(DbValueFormatter.ToFieldString(DBNull.Value));
    }

    [Fact]
    public void ToFieldString_Boolean_ReturnsLowercaseTrueFalse()
    {
        Assert.Equal("true", DbValueFormatter.ToFieldString(true));
        Assert.Equal("false", DbValueFormatter.ToFieldString(false));
    }

    [Fact]
    public void ToFieldString_String_PassesThroughUnchanged()
    {
        Assert.Equal("T1-001", DbValueFormatter.ToFieldString("T1-001"));
    }

    private static void RunUnderCulture(string cultureName, Action action)
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            action();
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
