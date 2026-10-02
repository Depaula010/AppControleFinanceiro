namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Único ponto de acesso a "agora". Todo horário de negócio é America/Sao_Paulo,
/// independentemente do fuso do container (constitution.md §7).
/// </summary>
public interface IClock
{
    /// <summary>Instante atual em UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Data e hora locais de São Paulo (Kind = Unspecified), como o <c>datetime.now()</c> do Flask.</summary>
    DateTime Now { get; }

    /// <summary>Data de hoje em São Paulo, como o <c>date.today()</c> do Flask.</summary>
    DateOnly Today { get; }

    TimeZoneInfo TimeZone { get; }
}
