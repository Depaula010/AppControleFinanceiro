using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.External.Weather;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Infrastructure.External;

public sealed class WeatherApiClientTests : IDisposable
{
    private const string Sunny = "{\"current\":{\"condition\":{\"text\":\"Sunny\"},\"temp_c\":24.9,\"feelslike_c\":-0.5,\"humidity\":65}}";

    private readonly FakeHttpHandler _handler = new();
    private readonly FakeKeySelector _keys = new();
    private readonly ListLogger<WeatherApiClient> _log = new();

    public void Dispose() => _handler.Dispose();

    private WeatherApiClient Client() => new(new StubHttpClientFactory(_handler), _keys, _log);

    [Fact]
    [Trait("AC", "010-110")]
    public async Task Current_SendsHttpsGetWithKeyQueryAndLangPt()
    {
        _handler.Enqueue(HttpStatusCode.OK, Sunny);

        await Client().GetWeatherAsync("São Paulo", "SP", null, default);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.Scheme.ShouldBe("https");
        request.Uri.Host.ShouldBe("api.weatherapi.com");
        request.Uri.AbsolutePath.ShouldBe("/v1/current.json");
        var query = Http.Query(request.Uri);
        query["key"].ShouldBe("chave-do-sistema");
        query["q"].ShouldBe("São Paulo,SP");
        query["lang"].ShouldBe("pt");
        _keys.Calls.ShouldBe([((int?)null, "weather")]);
    }

    [Theory]
    [InlineData("Campinas", null, "Campinas,Brazil")]
    [InlineData("Campinas", "", "Campinas,Brazil")]
    [InlineData("Foo, Bar", null, "Foo, Bar")]
    [InlineData("Foo, Bar", "SP", "Foo, Bar,SP")]
    public async Task Current_BuildsTheLocation(string city, string? state, string expected)
    {
        _handler.Enqueue(HttpStatusCode.OK, Sunny);

        await Client().GetWeatherAsync(city, state, null, default);

        Http.Query(_handler.Requests[0].Uri)["q"].ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "010-111")]
    public async Task Current_TruncatesNumbers_AndTranslatesTheCondition()
    {
        _handler.Enqueue(HttpStatusCode.OK, Sunny);

        var info = await Client().GetWeatherAsync("Santos", "SP", null, default);

        info.ShouldNotBeNull();
        info.Temperature.ShouldBe(24);
        info.FeelsLike.ShouldBe(0);
        info.Humidity.ShouldBe(65);
        info.Condition.ShouldBe("Ensolarado");
        info.Emoji.ShouldBe("☀️");
        info.RainChance.ShouldBeNull();
        info.FullDescription.ShouldBe("24°C, Ensolarado ☀️");
    }

    [Fact]
    [Trait("AC", "010-111")]
    public async Task Current_PortugueseText_GetsTheEmoji_Deviation054()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{\"current\":{\"condition\":{\"text\":\"Chuva leve\",\"code\":1183},\"temp_c\":18,\"feelslike_c\":18,\"humidity\":90}}");

        var info = await Client().GetWeatherAsync("Santos", "SP", null, default);

        info!.Condition.ShouldBe("Chuva leve");
        info.Emoji.ShouldBe("\U0001F327️");
    }

    [Fact]
    [Trait("AC", "010-111")]
    public async Task Current_UnknownTextWithKnownCode_UsesTheCode()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{\"current\":{\"condition\":{\"text\":\"Aguaceiro leve\",\"code\":1240},\"temp_c\":18,\"feelslike_c\":18,\"humidity\":90}}");

        var info = await Client().GetWeatherAsync("Santos", "SP", null, default);

        info!.Condition.ShouldBe("Chuva leve");
        info.Emoji.ShouldBe("\U0001F327️");
    }

    [Theory]
    [InlineData("{\"current\":{\"temp_c\":20}}", "Desconhecido")]
    [InlineData("{\"current\":{\"condition\":{\"text\":\"Coisa nova\"},\"temp_c\":20}}", "Coisa nova")]
    [InlineData("{}", "Desconhecido")]
    public async Task Current_UnknownCondition_ThermometerAndDefaults(string body, string condition)
    {
        _handler.Enqueue(HttpStatusCode.OK, body);

        var info = await Client().GetWeatherAsync("Santos", "SP", null, default);

        info!.Condition.ShouldBe(condition);
        info.Emoji.ShouldBe("\U0001F321️");
        info.Humidity.ShouldBe(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Created)]
    public async Task Current_StatusOtherThan200_ReturnsNull(HttpStatusCode status)
    {
        _handler.Enqueue(status, Sunny);

        (await Client().GetWeatherAsync("Santos", "SP", null, default)).ShouldBeNull();
        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-112")]
    public async Task Current_InvalidJsonNetworkErrorAndTimeout_ReturnNullWithoutThrowing()
    {
        _handler.Enqueue(HttpStatusCode.OK, "isto não é json");
        _handler.EnqueueException(new HttpRequestException("falha https://api.weatherapi.com/v1/current.json?key=chave-do-sistema"));
        _handler.EnqueueException(new TaskCanceledException("timeout"));
        var client = Client();

        (await client.GetWeatherAsync("Santos", "SP", null, default)).ShouldBeNull();
        (await client.GetWeatherAsync("Santos", "SP", null, default)).ShouldBeNull();
        (await client.GetWeatherAsync("Santos", "SP", null, default)).ShouldBeNull();

        _log.All.ShouldNotContain("chave-do-sistema");
        _log.All.ShouldNotContain("weatherapi.com");
        _log.All.ShouldNotContain("Santos");
    }

    [Fact]
    [Trait("AC", "010-112")]
    public async Task Current_WithoutKey_ReturnsNullWithoutRequest()
    {
        _keys.SystemKey = null;

        (await Client().GetWeatherAsync("Santos", "SP", null, default)).ShouldBeNull();

        _handler.Requests.ShouldBeEmpty();
        _log.All.ShouldContain("WEATHER_API_KEY");
    }

    [Fact]
    public async Task Current_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _handler.Enqueue(async (_, ct) =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return Http.Json(HttpStatusCode.OK, Sunny);
        });

        await Should.ThrowAsync<OperationCanceledException>(() => Client().GetWeatherAsync("Santos", "SP", null, cts.Token));
    }

    [Fact]
    [Trait("AC", "010-115")]
    public async Task UserKey_Resolved_IsUsedInTheQuery_AndFailedMeansNullWithoutRequest()
    {
        _keys.Users[7] = ProviderKeyResult.WithKey("chave-do-usuario");
        _keys.Users[8] = FakeKeySelector.Failure("Você não configurou sua chave");
        _handler.Enqueue(HttpStatusCode.OK, Sunny);
        var client = Client();

        (await client.GetWeatherAsync("Santos", "SP", 7, default)).ShouldNotBeNull();
        Http.Query(_handler.Requests[0].Uri)["key"].ShouldBe("chave-do-usuario");

        (await client.GetWeatherAsync("Santos", "SP", 8, default)).ShouldBeNull();
        _handler.Requests.Count.ShouldBe(1);
        _log.All.ShouldNotContain("chave-do-usuario");
    }

    [Fact]
    [Trait("AC", "010-113")]
    public async Task Forecast_CapsDaysAtThree_UsesBrazilAndReadsTheFirstDay()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{\"forecast\":{\"forecastday\":[{\"day\":{\"daily_chance_of_rain\":65.7,\"maxtemp_c\":31.9,\"mintemp_c\":-1.2}}]}}");

        var forecast = await Client().GetForecastAsync("Campinas", null, 5, null, default);

        forecast.ShouldBe(new ForecastInfo(65, 31, -1));
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe("/v1/forecast.json");
        var query = Http.Query(request.Uri);
        query["days"].ShouldBe("3");
        query["q"].ShouldBe("Campinas,Brazil");
        query["lang"].ShouldBe("pt");
        query["key"].ShouldBe("chave-do-sistema");
    }

    [Theory]
    [InlineData("{\"forecast\":{\"forecastday\":[]}}")]
    [InlineData("{}")]
    [InlineData("nada")]
    public async Task Forecast_EmptyOrInvalid_ReturnsNull(string body)
    {
        _handler.Enqueue(HttpStatusCode.OK, body);

        (await Client().GetForecastAsync("Campinas", "SP", 1, null, default)).ShouldBeNull();
    }

    [Fact]
    public async Task Forecast_Status500_UserKeyFailed_AndNoKey_ReturnNull()
    {
        _handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        _keys.Users[8] = FakeKeySelector.Failure("sem chave");
        var client = Client();

        (await client.GetForecastAsync("Campinas", "SP", 2, null, default)).ShouldBeNull();
        (await client.GetForecastAsync("Campinas", "SP", 2, 8, default)).ShouldBeNull();
        _keys.SystemKey = null;
        (await client.GetForecastAsync("Campinas", "SP", 2, null, default)).ShouldBeNull();

        _handler.Requests.Count.ShouldBe(1);
    }
}
