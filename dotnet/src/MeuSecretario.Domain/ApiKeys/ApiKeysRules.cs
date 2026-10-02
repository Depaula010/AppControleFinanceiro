using System.Globalization;

namespace MeuSecretario.Domain.ApiKeys;

/// <summary>Rótulo da preferência "chave própria × chave do sistema" (RN-008-05).</summary>
public static class PreferenceKind
{
    public static string Label(bool ownKey) => ownKey ? "própria (grátis)" : "sistema (cobrado)";
}

/// <summary>Uma linha agregada de <c>rastreamentousoapi</c> (provedor, tipo de chave, total de chamadas).</summary>
public sealed record UsageRow(string Provider, string KeyType, long TotalCalls, string Month, DateTime? LastUpdate);

/// <summary>Resumo do uso do mês (§4.8): soma por tipo de chave; outros tipos não entram no total.</summary>
public sealed record UsageSummary(long TotalCalls, long OwnKeyCalls, long SystemKeyCalls)
{
    public static UsageSummary From(IEnumerable<UsageRow> rows)
    {
        long own = 0;
        long system = 0;
        foreach (var row in rows)
        {
            if (row.KeyType == KeyTypes.Own)
            {
                own += row.TotalCalls;
            }
            else if (row.KeyType == KeyTypes.System)
            {
                system += row.TotalCalls;
            }
        }

        return new UsageSummary(own + system, own, system);
    }
}

/// <summary>Limite mensal do plano (§5): <c>null</c> é ilimitado; excedido quando <c>usado &gt;= limite</c>.</summary>
public static class PlanLimit
{
    public static bool Exceeded(int? limit, long used) => limit is not null && used >= limit;
}

/// <summary>Mês de uso/cobrança <c>yyyy-MM</c> a partir da hora local de São Paulo (RN-008-07).</summary>
public static class UsageMonth
{
    public static string Of(DateTime localNow) => localNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}

/// <summary>Confirmação exata exigida por <c>deletar-conta</c> (RN-008-11).</summary>
public static class AccountDeletion
{
    public const string Confirmation = "CONFIRMO_DELECAO";

    public static bool IsConfirmed(string? value) => string.Equals(value, Confirmation, StringComparison.Ordinal);
}
