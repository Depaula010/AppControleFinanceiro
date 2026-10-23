using System.Globalization;
using System.Threading.Channels;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Ops;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Ops;

public sealed record OpsAlert(string Source, string Message);

/// <summary>
/// Fila em memória dos avisos operacionais (desvio 261). Sem número de destino (<c>OPS_ALERT_WHATSAPP</c>) é um "nada a fazer"; com a fila
/// cheia descarta o aviso novo (um alerta perdido é melhor que travar um job).
/// </summary>
public sealed class OpsAlertChannel : IOpsAlerts
{
    private readonly Channel<OpsAlert> _channel = Channel.CreateBounded<OpsAlert>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public OpsAlertChannel(bool enabled) => Enabled = enabled;

    public bool Enabled { get; }

    public ChannelReader<OpsAlert> Reader => _channel.Reader;

    public void Raise(string source, string message)
    {
        if (Enabled)
        {
            _channel.Writer.TryWrite(new OpsAlert(source, message));
        }
    }
}

/// <summary>Entrega os avisos operacionais pelo bot, no máximo um por origem a cada 30 minutos.</summary>
public sealed partial class OpsAlertWorker(
    OpsAlertChannel channel, IBotClient bot, string destination, TimeProvider time, ILogger<OpsAlertWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    private readonly AlertThrottle _throttle = new(time, Window);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!channel.Enabled)
        {
            return;
        }

        try
        {
            await foreach (var alert in channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessAsync(alert, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // desligando.
        }
    }

    /// <summary>Envia o aviso, se a origem não estourou a frequência. Nunca lança.</summary>
    public async Task ProcessAsync(OpsAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (!_throttle.TryAcquire(alert.Source, out var suppressed))
        {
            return;
        }

        try
        {
            if (!await bot.SendTextAsync(destination, Format(alert, suppressed, time.GetUtcNow()), cancellationToken))
            {
                SendFailed(logger, alert.Source);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SendFailed(logger, alert.Source);
        }
    }

    /// <summary>O texto não leva dados pessoais: as mensagens dos jobs já vêm só com ids e o tipo da exceção.</summary>
    public static string Format(OpsAlert alert, int suppressed, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        var text = string.Create(CultureInfo.InvariantCulture, $"🚨 *ALERTA - {alert.Source}*\n{alert.Message}\n_{local:dd/MM HH:mm}_");
        return suppressed > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{text}\n(+{suppressed} falha(s) desta origem desde o último aviso)")
            : text;
    }

    [LoggerMessage(EventId = 13900, Level = LogLevel.Warning, Message = "Não consegui enviar o aviso operacional de {Source}")]
    private static partial void SendFailed(ILogger logger, string source);
}
