namespace MeuSecretario.Application.Admin;

public enum AdminFieldKind
{
    Absent,
    Null,
    Text,
    WholeNumber,
    Boolean,
    Other,
}

/// <summary>
/// Valor de um campo do corpo JSON já classificado pela Api, para que as regras de validação das rotas admin (tipos
/// estritos, desvio 079; "campo falso" do <c>validate_required_fields</c>) morem na Application sem conhecer HTTP.
/// <see cref="Truthy"/> reproduz o <c>bool(valor)</c> do Python (<c>not data.get(f)</c>): ausente, nulo, <c>""</c>, <c>0</c>,
/// <c>false</c>, <c>[]</c> e <c>{}</c> são falsos.
/// </summary>
public readonly record struct AdminField(
    AdminFieldKind Kind,
    string? Text = null,
    long Whole = 0,
    bool Boolean = false,
    bool Truthy = false)
{
    public static AdminField Absent { get; } = new(AdminFieldKind.Absent);

    public static AdminField NullValue { get; } = new(AdminFieldKind.Null);

    public static AdminField OfText(string value) => new(AdminFieldKind.Text, Text: value, Truthy: value.Length > 0);

    public static AdminField OfWhole(long value) => new(AdminFieldKind.WholeNumber, Whole: value, Truthy: value != 0);

    public static AdminField OfBoolean(bool value) => new(AdminFieldKind.Boolean, Boolean: value, Truthy: value);

    public static AdminField OtherValue(bool truthy) => new(AdminFieldKind.Other, Truthy: truthy);

    /// <summary>Texto não vazio (depois do <c>trim</c> se pedido); <c>null</c> se ausente, nulo, vazio ou de outro tipo.</summary>
    public string? NonEmptyText(bool trim)
    {
        if (Kind != AdminFieldKind.Text)
        {
            return null;
        }

        var value = trim ? Text!.Trim() : Text!;
        return value.Length == 0 ? null : value;
    }
}
