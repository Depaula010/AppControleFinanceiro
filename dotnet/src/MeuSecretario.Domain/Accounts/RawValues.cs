using System.Numerics;

namespace MeuSecretario.Domain.Accounts;

/// <summary>
/// Valor JSON bruto de um campo do corpo, sem referência a <c>System.Text.Json</c>. Existe para reproduzir as
/// conversões <c>float()</c>/<c>int()</c> do Python e para distinguir "ausente" de <c>null</c> explícito.
/// </summary>
public abstract record RawValue
{
    private protected RawValue()
    {
    }

    public static RawValue Absent { get; } = new AbsentValue();

    public static RawValue Null { get; } = new NullValue();

    /// <summary>Objeto ou lista JSON: nunca é um número nem um texto.</summary>
    public static RawValue Other { get; } = new OtherValue();

    public static RawValue Text(string value) => new TextValue(value);

    public static RawValue Whole(BigInteger value) => new IntegerValue(value);

    /// <summary>Número JSON com parte decimal ou expoente.</summary>
    public static RawValue Fractional(double value) => new DecimalValue(value);

    public static RawValue Bool(bool value) => new BoolValue(value);

    public sealed record AbsentValue : RawValue;

    public sealed record NullValue : RawValue;

    public sealed record OtherValue : RawValue;

    public sealed record TextValue(string Value) : RawValue;

    public sealed record IntegerValue(BigInteger Value) : RawValue;

    public sealed record DecimalValue(double Value) : RawValue;

    public sealed record BoolValue(bool Value) : RawValue;
}

/// <summary>Corpo JSON já lido: propriedade a propriedade, com <see cref="RawValue.Absent"/> para chaves que não vieram.</summary>
public sealed class RawBody(IReadOnlyDictionary<string, RawValue> values)
{
    public bool IsEmpty => values.Count == 0;

    public bool Has(string name) => values.ContainsKey(name);

    public RawValue Get(string name) => values.TryGetValue(name, out var value) ? value : RawValue.Absent;
}
