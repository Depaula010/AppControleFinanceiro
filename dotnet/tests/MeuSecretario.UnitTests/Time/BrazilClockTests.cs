using MeuSecretario.Infrastructure.Time;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Time;

public class BrazilClockTests
{
    [Fact]
    public void Today_BetweenMidnightUtcAndMidnightSaoPaulo_IsStillThePreviousDay()
    {
        // 02:30 UTC de 03/10 = 23:30 de 02/10 em São Paulo (UTC-3). É a janela 21h–0h em que
        // CURRENT_DATE do banco (UTC) e date.today() do Flask divergiam (data-model.md §1).
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 2, 30, 0, TimeSpan.Zero));

        var clock = new BrazilClock(time);

        clock.Today.ShouldBe(new DateOnly(2026, 10, 2));
        clock.Now.Hour.ShouldBe(23);
    }

    [Fact]
    public void Now_IsUnspecifiedKind_LikePythonNaiveDatetime()
    {
        var clock = new BrazilClock(new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero)));

        clock.Now.Kind.ShouldBe(DateTimeKind.Unspecified);
        clock.Now.Hour.ShouldBe(12);
    }

    [Fact]
    public void UtcNow_FollowsTheTimeProvider()
    {
        var instant = new DateTimeOffset(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

        new BrazilClock(new FakeTimeProvider(instant)).UtcNow.ShouldBe(instant);
    }
}
