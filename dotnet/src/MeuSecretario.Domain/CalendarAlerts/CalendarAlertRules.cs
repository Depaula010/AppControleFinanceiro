using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.Domain.CalendarAlerts;

/// <summary>Linha de <c>calendaralertconfigs</c> que as rotas devolvem (<c>config</c>).</summary>
public sealed record CalendarAlertConfig(bool Active, int MinutesBefore);

public enum FieldState
{
    /// <summary>Ausente ou <c>null</c> (o Python trata os dois como "não informado").</summary>
    Absent,
    Valid,
    Invalid,
}

/// <summary>Resultado de conferir <c>minutos_antes</c>; <see cref="Value"/> só vale com <see cref="FieldState.Valid"/>.</summary>
public readonly record struct MinutesCheck(FieldState State, int Value);

/// <summary>Resultado de conferir <c>ativo</c>; <see cref="Value"/> só vale com <see cref="FieldState.Valid"/>.</summary>
public readonly record struct ActiveCheck(FieldState State, bool Value);

/// <summary>Regras de entrada das rotas de alertas (spec 014 §3.2, RN-014-02).</summary>
public static class CalendarAlertRules
{
    public const int MinMinutes = 1;
    public const int MaxMinutes = 60;

    /// <summary>
    /// <c>isinstance(minutos, int) and 1 &lt;= minutos &lt;= 60</c>: só inteiro JSON (sem parte decimal, expoente, texto
    /// ou booleano). Fora disso, <see cref="FieldState.Invalid"/>.
    /// </summary>
    public static MinutesCheck ValidateMinutes(RawValue value) => value switch
    {
        RawValue.AbsentValue or RawValue.NullValue => new MinutesCheck(FieldState.Absent, 0),
        RawValue.IntegerValue i when i.Value >= MinMinutes && i.Value <= MaxMinutes => new MinutesCheck(FieldState.Valid, (int)i.Value),
        _ => new MinutesCheck(FieldState.Invalid, 0),
    };

    /// <summary>O <c>if ativo:</c> do Python: decide se a rota consulta o Google antes de gravar.</summary>
    public static bool IsActivationRequested(RawValue value) => PyTruth.IsTruthy(value);

    /// <summary>Só <c>true</c>/<c>false</c> JSON valem (desvio 120 propõe o contrato estrito; hoje é a divergência Q-014-3).</summary>
    public static ActiveCheck ParseActive(RawValue value) => value switch
    {
        RawValue.AbsentValue or RawValue.NullValue => new ActiveCheck(FieldState.Absent, false),
        RawValue.BoolValue b => new ActiveCheck(FieldState.Valid, b.Value),
        _ => new ActiveCheck(FieldState.Invalid, false),
    };

    /// <summary>Mensagem de sucesso (<c>calendar_alert_config_service.py:68-75</c>): partes presentes unidas por " e ".</summary>
    public static string SuccessMessage(bool? active, int? minutes)
    {
        var parts = new List<string>(2);
        if (active is { } flag)
        {
            parts.Add(flag ? "Alertas de tarefas ativados" : "Alertas de tarefas desativados");
        }

        if (minutes is { } value)
        {
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"alerta configurado para {value} minuto(s) antes"));
        }

        return parts.Count > 0 ? string.Join(" e ", parts) : "Configuração atualizada";
    }
}
