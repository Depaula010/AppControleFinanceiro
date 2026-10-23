using MeuSecretario.Application.Jobs;

namespace MeuSecretario.Application.Ops;

/// <summary>
/// Avisos operacionais para o dono (desvio 261): uma falha de job (check-in noturno, motor de agendamentos...) vai para o WhatsApp dele, em vez de
/// só aparecer no log. <see cref="Raise"/> nunca bloqueia nem lança: o aviso é entregue em segundo plano.
/// </summary>
public interface IOpsAlerts
{
    void Raise(string source, string message);
}

/// <summary>Limita a frequência: no máximo um aviso por origem a cada <c>window</c>; os demais só são contados.</summary>
public sealed class AlertThrottle(TimeProvider time, TimeSpan window)
{
    private readonly Dictionary<string, (DateTimeOffset Last, int Suppressed)> _state = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <param name="suppressed">Quantos avisos desta origem foram engolidos desde o último enviado.</param>
    /// <returns><c>true</c> = pode enviar agora.</returns>
    public bool TryAcquire(string source, out int suppressed)
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            if (_state.TryGetValue(source, out var state) && now - state.Last < window)
            {
                _state[source] = (state.Last, state.Suppressed + 1);
                suppressed = 0;
                return false;
            }

            suppressed = _state.TryGetValue(source, out var previous) ? previous.Suppressed : 0;
            _state[source] = (now, 0);
            return true;
        }
    }
}

/// <summary>
/// Decorador do log de jobs: tudo continua indo para o log de sempre, e cada <c>Failure</c> também vira um aviso operacional. Só falha
/// (erro de job, de usuário, de agendamento, envio que não saiu); <c>Info</c> e <c>Warning</c> não avisam.
/// </summary>
public sealed class AlertingJobLog(IJobLog inner, IOpsAlerts alerts) : IJobLog
{
    public void Info(string job, string message) => inner.Info(job, message);

    public void Warning(string job, string message) => inner.Warning(job, message);

    public void Failure(string job, string message)
    {
        inner.Failure(job, message);
        alerts.Raise(job, message);
    }
}
