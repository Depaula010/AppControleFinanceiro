using System.Globalization;
using MeuSecretario.Api.Http;

namespace MeuSecretario.UnitTests.Http;

/// <summary>Spec 008 §4: formato do <c>datetime.isoformat()</c> do Python (fração só com microssegundos, fuso +00:00).</summary>
public class PythonDateFormatTests
{
    [Fact]
    public void Iso_WithMicroseconds_PrintsSixFractionDigits()
    {
        var value = new DateTimeOffset(2026, 10, 2, 13, 45, 10, TimeSpan.Zero).AddTicks(1234560);

        PythonDateFormat.Iso(value).ShouldBe("2026-10-02T13:45:10.123456+00:00");
    }

    [Fact]
    public void Iso_WithoutMicroseconds_OmitsTheFraction()
    {
        var value = new DateTimeOffset(2026, 10, 2, 13, 45, 10, TimeSpan.Zero);

        PythonDateFormat.Iso(value).ShouldBe("2026-10-02T13:45:10+00:00");
    }

    [Fact]
    public void Iso_KeepsLeadingZerosOfTheFraction()
    {
        var value = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero).AddTicks(10); // 1 microssegundo

        PythonDateFormat.Iso(value).ShouldBe("2026-01-05T00:00:00.000001+00:00");
    }

    [Fact]
    public void Iso_Null_ReturnsNull()
    {
        PythonDateFormat.Iso((DateTimeOffset?)null).ShouldBeNull();
        PythonDateFormat.Iso((DateTime?)null).ShouldBeNull();
    }

    [Fact]
    public void Iso_ConvertsOtherOffsetsToUtc()
    {
        var value = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(-3));

        PythonDateFormat.Iso(value).ShouldBe("2026-10-02T13:00:00+00:00");
    }

    [Fact]
    public void Iso_FromUtcDateTime_Works()
    {
        var value = new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc);

        PythonDateFormat.Iso(value).ShouldBe("2026-10-02T13:00:00+00:00");
    }

    [Fact]
    public void Iso_FromUnspecifiedDateTime_IsTreatedAsUtc()
    {
        var value = new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Unspecified);

        PythonDateFormat.Iso(value).ShouldBe("2026-10-02T13:00:00+00:00");
    }

    [Fact]
    public void IsoLocal_HasNoZone_AndFractionOnlyWithMicroseconds()
    {
        PythonDateFormat.IsoLocal(new DateTime(2026, 10, 2, 8, 30, 0)).ShouldBe("2026-10-02T08:30:00");
        PythonDateFormat.IsoLocal(new DateTime(2026, 10, 2, 8, 30, 0).AddTicks(1234560)).ShouldBe("2026-10-02T08:30:00.123456");
    }

    [Fact]
    public void Iso_IsCultureIndependent()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            PythonDateFormat.Iso(new DateTimeOffset(2026, 10, 2, 13, 45, 10, TimeSpan.Zero)).ShouldBe("2026-10-02T13:45:10+00:00");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
