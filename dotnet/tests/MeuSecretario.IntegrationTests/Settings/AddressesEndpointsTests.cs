using System.Net;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Settings;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Settings;

[Trait("Category", "Docker")]
public class AddressesEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Path = "/api/user/addresses";
    private readonly SettingsEnv _env = new(postgres);
    private int _user;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.InsertUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private const string InvalidLabel = "Label inválido. Use: casa, outro, trabalho";

    [Fact]
    [Trait("AC", "007-50")]
    public async Task Get_WithoutAddresses_ReturnsAnEmptyList()
    {
        (await _env.Get(_user, Path)).ShouldBe((HttpStatusCode.OK, SettingsEnv.Data("[]")));
    }

    [Fact]
    [Trait("AC", "007-50")]
    public async Task Get_OnlyTheOwnAddresses_OrderedByLabel_WithCoordinatesOnlyWhenBothExist()
    {
        var other = await _env.InsertUserAsync("5511900000009", "Beto");
        await _env.ExecuteAsync(
            """
            INSERT INTO enderecosfavoritos (usuario_id, label, endereco_completo, latitude, longitude) VALUES
              (@_user,'trabalho','Rua T',-23.56,-46.65),
              (@_user,'casa','Av Paulista 1000',NULL,NULL),
              (@_user,'outro','Rua O',10.5,NULL),
              (@other,'casa','Do Beto',1,2)
            """, new { _user, other });

        var (_, body) = await _env.Get(_user, Path);

        body.ShouldBe(SettingsEnv.Data(
            """[{"id":2,"label":"casa","address":"Av Paulista 1000"},{"id":3,"label":"outro","address":"Rua O"},{"id":1,"label":"trabalho","address":"Rua T","coordinates":{"lat":-23.56,"lng":-46.65}}]"""));
        body.ShouldNotContain("Beto");
    }

    [Fact]
    [Trait("AC", "007-51")]
    public async Task Post_NormalizesLabelAndAddress_AndUpsertsWithoutDuplicating()
    {
        var (status, body) = await _env.Post(_user, Path, """{"label":" CASA ","address":" Rua A, 10 "}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe(SettingsEnv.Success("Endereço salvo com sucesso"));
        (await _env.QueryAsync<(string Label, string Address)>("SELECT label, endereco_completo FROM enderecosfavoritos WHERE usuario_id = @_user", new { _user }))
            .Single().ShouldBe(("casa", "Rua A, 10"));

        (await _env.Post(_user, Path, """{"label":"casa","address":"Rua B, 20"}""")).Status.ShouldBe(HttpStatusCode.Created);
        (await _env.QueryAsync<string>("SELECT endereco_completo FROM enderecosfavoritos WHERE usuario_id = @_user", new { _user })).Single().ShouldBe("Rua B, 20");
    }

    [Theory]
    [Trait("AC", "007-52")]
    [InlineData("""{"label":"escritorio","address":"x"}""", InvalidLabel)]
    [InlineData("""{"address":"x"}""", InvalidLabel)]
    [InlineData("""{"label":"","address":"x"}""", InvalidLabel)]
    [InlineData("""{"label":null,"address":"x"}""", InvalidLabel)]
    [InlineData("""{"label":5,"address":"x"}""", InvalidLabel)]
    [InlineData("""{"label":"casa"}""", "Endereço é obrigatório")]
    [InlineData("""{"label":"casa","address":""}""", "Endereço é obrigatório")]
    [InlineData("""{"label":"casa","address":"  "}""", "Endereço é obrigatório")]
    [InlineData("""{"label":"casa","address":7}""", "Endereço é obrigatório")]
    [InlineData("""{"label":"zzz","address":""}""", InvalidLabel)]
    [InlineData("""{"label":"casa","address":"x","coordinates":"abc"}""", "Dados inválidos")]
    [InlineData("""{"label":"casa","address":"x","coordinates":{"lat":123456,"lng":0}}""", "Coordenadas inválidas")]
    [InlineData("""{"label":"casa","address":"x","coordinates":{"lat":"abc"}}""", "Coordenadas inválidas")]
    [InlineData("""{"label":"casa","address":"x","coordinates":{"lat":91,"lng":0}}""", "Coordenadas inválidas")]
    [InlineData("""{"label":"casa","address":"x","coordinates":{"lat":0,"lng":-180.5}}""", "Coordenadas inválidas")]
    public async Task Post_Invalid_Returns400_AndWritesNothing(string json, string message)
    {
        var (status, body) = await _env.Post(_user, Path, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(SettingsEnv.Error(message));
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM enderecosfavoritos", null)).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "007-53")]
    public async Task Post_WithCoordinates_StoresThem_AndGetReturnsTheSameNumbers()
    {
        await _env.Post(_user, Path, """{"label":"casa","address":"Rua A","coordinates":{"lat":-23.5,"lng":-46.6}}""");

        (await _env.Get(_user, Path)).Body.ShouldBe(SettingsEnv.Data(
            """[{"id":1,"label":"casa","address":"Rua A","coordinates":{"lat":-23.5,"lng":-46.6}}]"""));
    }

    [Fact]
    [Trait("AC", "007-53")]
    public async Task Post_OnlyLatitude_StoresOnlyIt_AndGetOmitsCoordinates()
    {
        await _env.Post(_user, Path, """{"label":"casa","address":"Rua A","coordinates":{"lat":-23.5}}""");

        (await _env.QueryAsync<(decimal? Lat, decimal? Lng)>("SELECT latitude, longitude FROM enderecosfavoritos", null)).Single().ShouldBe((-23.5m, null));
        (await _env.Get(_user, Path)).Body.ShouldBe(SettingsEnv.Data("""[{"id":1,"label":"casa","address":"Rua A"}]"""));
    }

    [Fact]
    [Trait("AC", "007-53")]
    public async Task Post_WithoutCoordinates_KeepsThemWhenTheTextIsTheSame_AndClearsWhenItChanged_Desvio086()
    {
        await _env.Post(_user, Path, """{"label":"casa","address":"Rua A","coordinates":{"lat":-23.5,"lng":-46.6}}""");

        await _env.Post(_user, Path, """{"label":"casa","address":"  Rua A  "}""");
        (await _env.QueryAsync<(decimal? Lat, decimal? Lng)>("SELECT latitude, longitude FROM enderecosfavoritos", null)).Single().ShouldBe((-23.5m, -46.6m));

        await _env.Post(_user, Path, """{"label":"casa","address":"Rua B","coordinates":{}}""");
        (await _env.QueryAsync<(decimal? Lat, decimal? Lng)>("SELECT latitude, longitude FROM enderecosfavoritos", null)).Single().ShouldBe((null, null));
    }

    [Fact]
    [Trait("AC", "007-53")]
    public async Task Post_WithCoordinatesReplacesTheOnesStored()
    {
        await _env.Post(_user, Path, """{"label":"casa","address":"Rua A","coordinates":{"lat":1,"lng":2}}""");
        await _env.Post(_user, Path, """{"label":"casa","address":"Rua A","coordinates":{"lat":"3.5","lng":4}}""");

        (await _env.QueryAsync<(decimal? Lat, decimal? Lng)>("SELECT latitude, longitude FROM enderecosfavoritos", null)).Single().ShouldBe((3.5m, 4m));
    }

    [Fact]
    [Trait("AC", "007-55")]
    public async Task Delete_RemovesOnlyTheOwnRow_TheSecondTimeIs404()
    {
        var other = await _env.InsertUserAsync("5511900000009", "Beto");
        await _env.Post(_user, Path, """{"label":"casa","address":"A"}""");
        await _env.Post(other, Path, """{"label":"casa","address":"B"}""");

        (await _env.Delete(_user, Path + "/Casa")).ShouldBe((HttpStatusCode.OK, SettingsEnv.Success("Endereço removido com sucesso")));
        (await _env.Delete(_user, Path + "/casa")).ShouldBe((HttpStatusCode.NotFound, SettingsEnv.Error("Endereço não encontrado")));
        (await _env.Delete(_user, Path + "/xyz")).ShouldBe((HttpStatusCode.BadRequest, SettingsEnv.Error(InvalidLabel)));

        (await _env.QueryAsync<int>("SELECT usuario_id FROM enderecosfavoritos", null)).Single().ShouldBe(other);
    }

    [Fact]
    [Trait("AC", "007-55")]
    public async Task OtherMethodsOnTheLabelRoute_Return405()
    {
        (await _env.Put(_user, Path + "/casa", "{}")).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await _env.Get(_user, Path + "/casa")).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    [Trait("AC", "007-82")]
    public async Task DeleteWithoutLabel_Returns404Html()
    {
        var (status, body) = await _env.Delete(_user, Path + "/");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("<title>404 Not Found</title>");
    }

    [Fact]
    [Trait("AC", "007-54")]
    public async Task Repository_RejectsLabelOutsideTheCheck_AndHandlesNumericOverflowAsException()
    {
        var repository = postgres.Api.Services.GetRequiredService<IFavoriteAddressRepository>();

        await Should.ThrowAsync<Exception>(() => repository.UpsertAsync(_user, "escritorio", "x", null, default));
        await Should.ThrowAsync<Exception>(() => repository.UpsertAsync(_user, "casa", "x", new AddressCoordinates(123456m, 0m), default));
        (await repository.ListAsync(_user, default)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Repository_Port_IsUsableByTheWhatsAppFeature()
    {
        var repository = postgres.Api.Services.GetRequiredService<IFavoriteAddressRepository>();

        await repository.UpsertAsync(_user, "trabalho", "Rua Z", new AddressCoordinates(1.5m, 2.5m), default);

        var row = (await repository.ListAsync(_user, default)).Single();
        (row.Label, row.Address, row.Latitude, row.Longitude).ShouldBe(("trabalho", "Rua Z", 1.5m, 2.5m));
        (await repository.DeleteAsync(_user, "trabalho", default)).ShouldBeTrue();
        (await repository.DeleteAsync(_user, "trabalho", default)).ShouldBeFalse();
    }
}
