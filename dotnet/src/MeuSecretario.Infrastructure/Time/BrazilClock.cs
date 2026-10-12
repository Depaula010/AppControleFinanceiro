using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Infrastructure.Time;

public sealed class BrazilClock(TimeProvider timeProvider) : IClock
{
    private static readonly TimeZoneInfo SaoPaulo = ResolveTimeZone();

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateTime Now => DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(UtcNow, SaoPaulo).DateTime, DateTimeKind.Unspecified);

    public DateOnly Today => DateOnly.FromDateTime(Now);

    public TimeZoneInfo TimeZone => SaoPaulo;

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows sem ICU: nome do registro do Windows para o mesmo fuso.
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
