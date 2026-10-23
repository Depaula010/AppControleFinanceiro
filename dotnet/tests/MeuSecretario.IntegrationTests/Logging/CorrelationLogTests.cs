using MeuSecretario.Api;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Display;

namespace MeuSecretario.IntegrationTests.Logging;

/// <summary>
/// Desvio 261: com o template real do log, as linhas fora do webhook não mudam e as de dentro de uma mensagem levam o mesmo
/// <c>[id]</c>. Usa o Serilog de verdade (a mesma ponte <c>ILogger</c> do host), sem banco nem Docker.
/// </summary>
public class CorrelationLogTests
{
    private sealed class CapturingSink : ILogEventSink
    {
        private readonly MessageTemplateTextFormatter _formatter = new(LogTemplates.Output, System.Globalization.CultureInfo.InvariantCulture);

        public List<string> Lines { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            using var writer = new StringWriter();
            _formatter.Format(logEvent, writer);
            Lines.Add(writer.ToString().TrimEnd('\r', '\n'));
        }
    }

    private static (Microsoft.Extensions.Logging.ILogger Logger, CapturingSink Sink) Create()
    {
        var sink = new CapturingSink();
        var serilog = new LoggerConfiguration()
            .Enrich.WithProperty(LogTemplates.CorrelationProperty, string.Empty)
            .WriteTo.Sink(sink)
            .CreateLogger();
        return (new SerilogLoggerProvider(serilog).CreateLogger("teste"), sink);
    }

    private static void Write(Microsoft.Extensions.Logging.ILogger logger, LogLevel level, string text) =>
        logger.Log(level, default, text, null, static (state, _) => state);

    private static string WithoutTime(string line) => line[(line.IndexOf(']', StringComparison.Ordinal) + 2)..];

    [Fact]
    public void WithoutAScope_TheLineKeepsTheOldFormat()
    {
        var (logger, sink) = Create();

        Write(logger, LogLevel.Information, "[2026-10-23 20:00:00] [CHECKIN-NOTURNO] INFO: INICIANDO PROCESSAMENTO");

        sink.Lines.Single().ShouldMatch(@"^\[\d\d:\d\d:\d\d INF\] \[2026-10-23 20:00:00\] \[CHECKIN-NOTURNO\] INFO: INICIANDO PROCESSAMENTO$");
    }

    [Fact]
    public void InsideTheWebhookScope_EveryLineCarriesTheSameId_AndItEndsWithTheScope()
    {
        var (logger, sink) = Create();
        var id = LogTemplates.NewCorrelationId();

        using (logger.BeginScope(new Dictionary<string, object> { [LogTemplates.CorrelationProperty] = LogTemplates.Prefix(id) }))
        {
            Write(logger, LogLevel.Information, "Webhook recebido");
            Write(logger, LogLevel.Warning, "Falha na intenção");
        }

        Write(logger, LogLevel.Information, "depois");

        id.ShouldMatch("^[0-9a-f]{8}$");
        sink.Lines.Select(WithoutTime).ShouldBe([$"[{id}] Webhook recebido", $"[{id}] Falha na intenção", "depois"]);
    }

    [Fact]
    public void TwoMessagesGetDifferentIds() =>
        LogTemplates.NewCorrelationId().ShouldNotBe(LogTemplates.NewCorrelationId());
}
