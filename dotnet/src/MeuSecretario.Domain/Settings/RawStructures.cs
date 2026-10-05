using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Domain.Settings;

/// <summary>Objeto JSON aninhado (bloco de notificações, <c>coordinates</c>). Estende o <see cref="RawValue"/> da 002 sem alterá-lo.</summary>
public sealed record ObjectValue(RawBody Body) : RawValue;

/// <summary>Lista JSON; só o tamanho importa (truthiness do Python).</summary>
public sealed record ListValue(int Count) : RawValue;

/// <summary>Truthiness do Python sobre um valor JSON bruto: <c>false</c>, <c>0</c>, <c>""</c>, <c>null</c>, vazio e ausente são falsos.</summary>
public static class PyTruth
{
    public static bool IsTruthy(RawValue value) => value switch
    {
        RawValue.BoolValue b => b.Value,
        RawValue.IntegerValue i => !i.Value.IsZero,
        RawValue.DecimalValue d => d.Value != 0,
        RawValue.TextValue t => t.Value.Length > 0,
        ObjectValue o => !o.Body.IsEmpty,
        ListValue l => l.Count > 0,
        RawValue.AbsentValue or RawValue.NullValue => false,
        _ => true,
    };
}
