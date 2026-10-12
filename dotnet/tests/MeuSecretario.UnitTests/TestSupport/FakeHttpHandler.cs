using System.Net;
using System.Text;
using System.Text.Json;

namespace MeuSecretario.UnitTests.TestSupport;

/// <summary>Requisição como saiu do cliente; o corpo é lido na hora (o conteúdo é descartado depois do envio).</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string? BodyText, byte[]? BodyBytes);

/// <summary>
/// <see cref="HttpMessageHandler"/> de teste: enfileira respostas ou funções, registra cada requisição
/// (método, URI, headers, corpo) para o teste validar o payload enviado, e simula atraso e exceção.
/// Quando a fila acaba, repete a última resposta; sem nenhuma, devolve 200 vazio.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>> _responders = new();
    private Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>>? _last;
    private readonly object _gate = new();
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public FakeHttpHandler Enqueue(HttpStatusCode status, string? body = null, string mediaType = "application/json") =>
        Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = body is null ? new ByteArrayContent([]) : new StringContent(body, Encoding.UTF8, mediaType),
        });

    public FakeHttpHandler Enqueue(Func<RecordedRequest, HttpResponseMessage> responder) =>
        Enqueue((request, _) => Task.FromResult(responder(request)));

    public FakeHttpHandler Enqueue(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder)
    {
        lock (_gate)
        {
            _responders.Enqueue(responder);
        }

        return this;
    }

    /// <summary>Resposta que só chega depois de <paramref name="delay"/> (respeita o cancelamento do chamador).</summary>
    public FakeHttpHandler EnqueueDelayed(TimeSpan delay, HttpStatusCode status, string? body = null) =>
        Enqueue(async (_, ct) =>
        {
            await Task.Delay(delay, ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json") };
        });

    /// <summary>A próxima requisição lança <paramref name="exception"/> (falha de rede, por exemplo).</summary>
    public FakeHttpHandler EnqueueException(Exception exception) =>
        Enqueue((_, _) => throw exception);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recorded = await RecordAsync(request, cancellationToken);
        Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> responder;
        lock (_gate)
        {
            _requests.Add(recorded);
            if (_responders.Count > 0)
            {
                _last = _responders.Dequeue();
            }

            responder = _last ?? ((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }));
        }

        return await responder(recorded, cancellationToken);
    }

    private static async Task<RecordedRequest> RecordAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        string? text = null;
        byte[]? bytes = null;
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(",", header.Value);
            }

            bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            text = Encoding.UTF8.GetString(bytes);
        }

        return new RecordedRequest(request.Method, request.RequestUri!, headers, text, bytes);
    }

    /// <summary>Compara o corpo enviado com o JSON esperado (ignora espaços e ordem de formatação, não a ordem das propriedades dentro de arrays).</summary>
    public static void AssertJsonBody(RecordedRequest request, string expectedJson)
    {
        request.BodyText.ShouldNotBeNull();
        using var actual = JsonDocument.Parse(request.BodyText);
        using var expected = JsonDocument.Parse(expectedJson);
        Canonical(actual.RootElement).ShouldBe(Canonical(expected.RootElement));
    }

    private static string Canonical(JsonElement element) => JsonSerializer.Serialize(element);
}
