using System.Net;
using System.Text.RegularExpressions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.ApiKeys;

/// <summary>Spec 008b (rotas 10 a 15) ponta a ponta contra PostgreSQL 15, com as colunas reais do banco (desvio 028).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public partial class LgpdEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        await ApiKeysTestSupport.SeedPlansAsync(postgres);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client(bool withKey = true) => ApiKeysTestSupport.EnabledClient(postgres, withKey);

    private static Task<(HttpStatusCode Status, string Body)> Send(HttpClient client, HttpMethod method, string path, string? json = null) =>
        ApiKeysEndpointsTests.Send(client, method, path, json);

    private static string Error(string erro, string mensagem) => ApiKeysEndpointsTests.Error(erro, mensagem);

    private static string Norm(string body) => ApiKeysEndpointsTests.NormalizeTimestamps(body);

    // ============================== autenticação ==============================

    [Theory]
    [Trait("AC", "008-01")]
    [InlineData("POST", "/api-keys/lgpd/consentimento", """{"usuario_id":1,"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":true}""")]
    [InlineData("GET", "/api-keys/lgpd/consentimentos/1", null)]
    [InlineData("GET", "/api-keys/lgpd/consentimentos-iniciais/1", null)]
    [InlineData("GET", "/api-keys/lgpd/exportar/1", null)]
    [InlineData("DELETE", "/api-keys/lgpd/deletar-conta/1", """{"confirmacao":"CONFIRMO_DELECAO"}""")]
    [InlineData("GET", "/api-keys/lgpd/verificar-consentimento/1/uso_dados_pessoais", null)]
    public async Task WithoutTheApiKey_Returns401_AndNothingChanges(string method, string path, string? json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "segredo-do-usuario");

        var (status, body) = await Send(Client(withKey: false), new HttpMethod(method), path, json);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe("""{"erro":"Não autorizado","mensagem":"API key inválida ou ausente"}""");
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM usuarios")).ShouldBe(1);
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM chavesapiusuario")).ShouldBe(1);
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM consentimentousuario")).ShouldBe(0);
    }

    // ============================== POST /lgpd/consentimento ==============================

    [Fact]
    [Trait("AC", "008-40")]
    public async Task RegisterConsent_Returns201_WithDefaultVersion_AndNullIp()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento",
            $$"""{"usuario_id":{{userId}},"tipo_consentimento":"armazenamento_chaves_api","consentimento_dado":true}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe("""{"status":"sucesso","consentimento_id":1,"mensagem":"Consentimento registrado com sucesso","tipo":"armazenamento_chaves_api","consentimento_dado":true}""");
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT versao_consentimento FROM consentimentousuario WHERE id = 1")).ShouldBe("1.0");
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT ip_consentimento IS NULL AND revogado_em IS NULL FROM consentimentousuario WHERE id = 1")).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "008-40")]
    public async Task RegisterConsent_FalseIsAccepted_AndTheGivenVersionIsStored()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento",
            $$"""{"usuario_id":{{userId}},"tipo_consentimento":"rastreamento_uso","consentimento_dado":false,"versao_termos":"2.5"}""");

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"consentimento_dado\":false");
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT versao_consentimento FROM consentimentousuario WHERE id = 1")).ShouldBe("2.5");
    }

    [Theory]
    [Trait("AC", "008-41")]
    [InlineData("""{"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":true}""")]
    [InlineData("""{"usuario_id":0,"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":true}""")]
    [InlineData("""{"usuario_id":1,"consentimento_dado":true}""")]
    [InlineData("""{"usuario_id":1,"tipo_consentimento":"","consentimento_dado":true}""")]
    [InlineData("""{"usuario_id":1,"tipo_consentimento":"uso_dados_pessoais"}""")]
    [InlineData("""{"usuario_id":1,"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":null}""")]
    [InlineData("""{"usuario_id":"1","tipo_consentimento":"uso_dados_pessoais","consentimento_dado":true}""")]
    [InlineData("""{"usuario_id":1,"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":"true"}""")]
    [InlineData("""{"usuario_id":1,"tipo_consentimento":5,"consentimento_dado":true}""")]
    [InlineData("não é json")]
    [InlineData("")]
    public async Task RegisterConsent_MissingFieldsOrWrongTypes_Return400(string json)
    {
        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Dados incompletos", "usuario_id, tipo_consentimento e consentimento_dado são obrigatórios"));
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM consentimentousuario")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "008-42")]
    public async Task RegisterConsent_UnknownType_Returns400WithThePythonList()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento",
            $$"""{"usuario_id":{{userId}},"tipo_consentimento":"marketing","consentimento_dado":true}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error(
            "Validação",
            "Tipo de consentimento inválido: marketing. Válidos: ['uso_dados_pessoais', 'armazenamento_chaves_api', 'rastreamento_uso', 'comunicacao_whatsapp', 'compartilhamento_terceiros']"));
    }

    [Fact]
    [Trait("AC", "008-43")]
    public async Task RegisterConsent_RepeatingUpdatesTheSameRow_RevokesAndRegrants()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        string Json(bool given, string version = "1.0") =>
            $$"""{"usuario_id":{{userId}},"tipo_consentimento":"comunicacao_whatsapp","consentimento_dado":{{given.ToString().ToLowerInvariant()}},"versao_termos":"{{version}}"}""";

        var (s1, b1) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento", Json(true));
        var firstDate = await ApiKeysTestSupport.Scalar<DateTime>(postgres, "SELECT data_consentimento FROM consentimentousuario WHERE id = 1");

        var (s2, b2) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento", Json(false));
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT revogado_em IS NOT NULL AND NOT consentimento_dado FROM consentimentousuario WHERE id = 1")).ShouldBeTrue();
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT data_consentimento > @firstDate FROM consentimentousuario WHERE id = 1", new { firstDate })).ShouldBeTrue();

        var (s3, _) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento", Json(true));
        (await ApiKeysTestSupport.Scalar<bool>(postgres, "SELECT revogado_em IS NULL AND consentimento_dado FROM consentimentousuario WHERE id = 1")).ShouldBeTrue();

        var (s4, b4) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento", Json(true, "2.0"));

        new[] { s1, s2, s3, s4 }.ShouldAllBe(s => s == HttpStatusCode.Created);
        b1.ShouldContain("\"consentimento_id\":1");
        b2.ShouldContain("\"consentimento_id\":1");
        b4.ShouldNotContain("\"consentimento_id\":1,"); // linha nova (a sequência pula valores nos upserts que conflitam)
        (await ApiKeysTestSupport.Scalar<string>(postgres, "SELECT versao_consentimento FROM consentimentousuario ORDER BY id DESC LIMIT 1")).ShouldBe("2.0");
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM consentimentousuario")).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "008-43")]
    public async Task RegisterConsent_UnknownUser_Returns404()
    {
        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento",
            """{"usuario_id":999,"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":true}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(Error("Usuário não encontrado", "Nenhum usuário com ID 999"));
    }

    [Fact]
    [Trait("AC", "008-43")]
    public async Task RegisterConsent_VersionLongerThan20Characters_Returns500WithFixedText()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        var (status, body) = await Send(Client(), HttpMethod.Post, "/api-keys/lgpd/consentimento",
            $$"""{"usuario_id":{{userId}},"tipo_consentimento":"uso_dados_pessoais","consentimento_dado":true,"versao_termos":"{{new string('9', 21)}}"}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Error("Erro ao registrar consentimento", "Erro ao registrar consentimento. Tente novamente."));
    }

    // ============================== GET consentimentos / iniciais / verificar ==============================

    [Fact]
    [Trait("AC", "008-44")]
    public async Task ListConsents_ReturnsDescriptionsAndVersion_MostRecentFirst()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO consentimentousuario (usuario_id, tipo_consentimento, versao_consentimento, consentimento_dado, data_consentimento) VALUES
              (@userId, 'uso_dados_pessoais', '1.0', true, '2026-10-01 10:00:00+00'),
              (@userId, 'tipo_antigo', '1.0', false, '2026-10-03 10:00:00+00')
            """, new { userId });

        var (status, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/lgpd/consentimentos/{userId}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"usuario_id":1,"total":2,"consentimentos":[""" +
            """{"id":2,"tipo_consentimento":"tipo_antigo","descricao":"Descrição não disponível","consentimento_dado":false,"versao_termos":"1.0","data_consentimento":"2026-10-03T10:00:00+00:00"},""" +
            """{"id":1,"tipo_consentimento":"uso_dados_pessoais","descricao":"Uso de dados pessoais para funcionamento do sistema","consentimento_dado":true,"versao_termos":"1.0","data_consentimento":"2026-10-01T10:00:00+00:00"}]}""");
    }

    [Fact]
    [Trait("AC", "008-45")]
    public async Task InitialConsents_ListsTheFiveTypesInOrder()
    {
        var (status, body) = await Send(Client(), HttpMethod.Get, "/api-keys/lgpd/consentimentos-iniciais/7");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"usuario_id":7,"consentimentos_necessarios":[""" +
            """{"tipo":"uso_dados_pessoais","descricao":"Uso de dados pessoais para funcionamento do sistema","obrigatorio":true,"versao_termos":"1.0"},""" +
            """{"tipo":"armazenamento_chaves_api","descricao":"Armazenamento de chaves de API criptografadas","obrigatorio":true,"versao_termos":"1.0"},""" +
            """{"tipo":"rastreamento_uso","descricao":"Rastreamento de uso para billing e analytics","obrigatorio":false,"versao_termos":"1.0"},""" +
            """{"tipo":"comunicacao_whatsapp","descricao":"Envio de notificações via WhatsApp","obrigatorio":false,"versao_termos":"1.0"},""" +
            """{"tipo":"compartilhamento_terceiros","descricao":"Compartilhamento com APIs de terceiros (Gemini, Weather, etc)","obrigatorio":false,"versao_termos":"1.0"}],"mensagem":"Para continuar, você precisa consentir com os termos abaixo"}""");
    }

    [Fact]
    [Trait("AC", "008-46")]
    public async Task CheckConsent_FollowsTheMostRecentRecord()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);

        async Task<string> Check(string type) =>
            (await Send(Client(), HttpMethod.Get, $"/api-keys/lgpd/verificar-consentimento/{userId}/{type}")).Body;

        (await Check("uso_dados_pessoais")).ShouldBe("""{"usuario_id":1,"tipo_consentimento":"uso_dados_pessoais","consentimento_ativo":false}""");

        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO consentimentousuario (usuario_id, tipo_consentimento, versao_consentimento, consentimento_dado, data_consentimento) VALUES
              (@userId, 'uso_dados_pessoais', '1.0', true, '2026-10-01 10:00:00+00'),
              (@userId, 'rastreamento_uso', '1.0', true, '2026-10-01 10:00:00+00'),
              (@userId, 'rastreamento_uso', '2.0', false, '2026-10-02 10:00:00+00')
            """, new { userId });

        (await Check("uso_dados_pessoais")).ShouldBe("""{"usuario_id":1,"tipo_consentimento":"uso_dados_pessoais","consentimento_ativo":true}""");
        (await Check("rastreamento_uso")).ShouldBe("""{"usuario_id":1,"tipo_consentimento":"rastreamento_uso","consentimento_ativo":false}""");
        (await Check("tipo-desconhecido")).ShouldBe("""{"usuario_id":1,"tipo_consentimento":"tipo-desconhecido","consentimento_ativo":false}""");
    }

    // ============================== GET /lgpd/exportar/<id> ==============================

    [GeneratedRegex("\"data_exportacao\":\"\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(\\.\\d{6})?\"")]
    private static partial Regex ExportDateRegex();

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_WithDataInEveryTable_ReturnsTheSixSections_WithDecryptedKeysAndNoStore()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        var python = AuthFixtures.FernetCases[0]; // "abc", token gerado pelo Python
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", python.Token);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "weather", "texto-puro-legado", active: false);
        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria) VALUES (@userId, 'gemini', true);
            INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano) VALUES (@userId, 'gemini', 'propria', 4, '2026-10');
            INSERT INTO logacessochaveapi (usuario_id, provedor, tipo_chave, operacao, sucesso, mensagem_erro, timestamp_acesso)
              VALUES (@userId, 'gemini', 'propria', 'obter_chave', false, 'OwnKeyMissing', '2026-10-02 09:30:00.25+00');
            INSERT INTO consentimentousuario (usuario_id, tipo_consentimento, versao_consentimento, consentimento_dado, data_consentimento)
              VALUES (@userId, 'uso_dados_pessoais', '1.0', true, '2026-10-01 10:00:00+00');
            INSERT INTO assinaturasusuario (usuario_id, plano_id, status) SELECT @userId, id, 'ativo' FROM planos WHERE nome_plano = 'Prata';
            """, new { userId });

        using var client = Client();
        using var response = await client.GetAsync(new Uri($"/api-keys/lgpd/exportar/{userId}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        ExportDateRegex().IsMatch(body).ShouldBeTrue();

        body.ShouldContain("\"data_acesso\":\"2026-10-02T09:30:00.250000+00:00\"");
        body.ShouldContain("\"data_consentimento\":\"2026-10-01T10:00:00+00:00\"");

        var normalized = Norm(ExportDateRegex().Replace(body, "\"data_exportacao\":\"<now>\""));
        normalized.ShouldBe(
            """{"usuario_id":1,"data_exportacao":"<now>","dados":{"chaves_api":[""" +
            """{"id":1,"provedor":"gemini","chave_api":"abc","ativo":true,"ultimo_uso_em":null,"criado_em":"<ts>","atualizado_em":"<ts>"},""" +
            """{"id":2,"provedor":"weather","chave_api":"texto-puro-legado","ativo":false,"ultimo_uso_em":null,"criado_em":"<ts>","atualizado_em":"<ts>"}],"preferencias":[{"id":1,"provedor":"gemini","usar_chave_propria":true,"atualizado_em":"<ts>"}],"uso_mensal":[{"provedor":"gemini","tipo_chave":"propria","quantidade_chamadas":4,"mes_ano":"2026-10"}],"logs_acesso":[{"provedor":"gemini","tipo_chave":"propria","sucesso":false,"mensagem_erro":"OwnKeyMissing","data_acesso":"<ts>"}],"consentimentos":[{"id":1,"tipo_consentimento":"uso_dados_pessoais","descricao":"Uso de dados pessoais para funcionamento do sistema","consentimento_dado":true,"versao_termos":"1.0","data_consentimento":"<ts>"}],"assinatura":{"id":1,"plano":"Prata","preco_mensal":29.9,"limites":{"gemini":500,"weather":200,"openroute":50},"data_inicio":"<ts>","data_fim":null,"ativo":true}}}""");
    }

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_UnknownUser_ReturnsEmptySectionsAndNullSubscription()
    {
        var (status, body) = await Send(Client(), HttpMethod.Get, "/api-keys/lgpd/exportar/404");

        status.ShouldBe(HttpStatusCode.OK);
        ExportDateRegex().Replace(body, "\"data_exportacao\":\"<now>\"").ShouldBe(
            """{"usuario_id":404,"data_exportacao":"<now>","dados":{"chaves_api":[],"preferencias":[],"uso_mensal":[],"logs_acesso":[],"consentimentos":[],"assinatura":null}}""");
    }

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_Subscription_ZeroPriceIsAnInteger_UnlimitedLimitsAreNull_AndExpiredIsInactive()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO assinaturasusuario (usuario_id, plano_id, status, data_fim) SELECT @userId, id, 'ativo', now() - interval '1 day' FROM planos WHERE nome_plano = 'Ouro'",
            new { userId });

        var (_, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/lgpd/exportar/{userId}");

        Norm(body).ShouldContain(
            "\"assinatura\":" +
            """{"id":1,"plano":"Ouro","preco_mensal":0,"limites":{"gemini":null,"weather":null,"openroute":null},"data_inicio":"<ts>","data_fim":"<ts>","ativo":false}""");
    }

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_CancelledSubscription_IsInactive()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.Execute(postgres,
            "INSERT INTO assinaturasusuario (usuario_id, plano_id, status) SELECT @userId, id, 'cancelado' FROM planos WHERE nome_plano = 'Bronze'", new { userId });

        var (_, body) = await Send(Client(), HttpMethod.Get, $"/api-keys/lgpd/exportar/{userId}");

        body.ShouldContain("\"ativo\":false}}}");
        body.ShouldContain("\"plano\":\"Bronze\"");
    }

    [Fact]
    [Trait("AC", "008-47")]
    public async Task Export_CorruptedFernetToken_Returns500_WithoutLeakingTheTokenOrOtherKeys()
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", AuthFixtures.FernetTamperedToken);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "weather", "texto-puro-legado");

        using var client = Client();
        using var response = await client.GetAsync(new Uri($"/api-keys/lgpd/exportar/{userId}", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Error("Erro ao exportar dados", "Erro ao exportar dados. Tente novamente."));
        body.ShouldNotContain(AuthFixtures.FernetTamperedToken);
        body.ShouldNotContain("texto-puro-legado");
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    // ============================== DELETE /lgpd/deletar-conta/<id> ==============================

    [Theory]
    [Trait("AC", "008-48")]
    [InlineData("""{"confirmacao":"confirmo_delecao"}""")]
    [InlineData("""{"confirmacao":"CONFIRMO"}""")]
    [InlineData("""{"confirmacao":true}""")]
    [InlineData("""{"confirmacao":null}""")]
    [InlineData("""{}""")]
    [InlineData("não é json")]
    [InlineData("")]
    public async Task DeleteAccount_WithoutTheExactConfirmation_Returns400_AndDeletesNothing(string json)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres);
        await ApiKeysTestSupport.SeedKeyAsync(postgres, userId, "gemini", "k");

        var (status, body) = await Send(Client(), HttpMethod.Delete, $"/api-keys/lgpd/deletar-conta/{userId}", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(Error("Confirmação inválida", "Confirmação inválida. Para deletar a conta, você deve passar confirmacao='CONFIRMO_DELECAO' explicitamente."));
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM usuarios")).ShouldBe(1);
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM chavesapiusuario")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "008-49")]
    public async Task DeleteAccount_UnknownUser_Returns404()
    {
        var (status, body) = await Send(Client(), HttpMethod.Delete, "/api-keys/lgpd/deletar-conta/321", """{"confirmacao":"CONFIRMO_DELECAO"}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(Error("Usuário não encontrado", "Nenhum usuário com ID 321"));
    }

    /// <summary>Um usuário com dados em todas as tabelas, inclusive as que têm FK RESTRICT. Devolve (usuário, conta, subcategoria).</summary>
    private async Task<int> SeedFullUserAsync(string whatsapp, string suffix)
    {
        var userId = await ApiKeysTestSupport.SeedUserAsync(postgres, whatsapp, $"Usuário {suffix}");
        await ApiKeysTestSupport.Execute(postgres,
            """
            INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro) SELECT id, @userId, @macro FROM grupocategoria ORDER BY id LIMIT 1;
            INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) SELECT id, @userId, @sub FROM macrocategoria WHERE usuario_id = @userId;
            INSERT INTO contas (usuario_id, nome_conta, tipo_conta, dia_vencimento, dia_fechamento) VALUES (@userId, @card, 'Cartão de Crédito', 10, 5);
            INSERT INTO contas (usuario_id, nome_conta, tipo_conta) VALUES (@userId, @wallet, 'Dinheiro');
            INSERT INTO faturas (conta_id, data_vencimento, data_fechamento) SELECT id, '2026-11-10', '2026-11-05' FROM contas WHERE usuario_id = @userId AND nome_conta = @card;
            INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, tipo_transacao)
              SELECT @userId, c.id, s.id, 'Compra', 10.00, 'Despesa' FROM contas c, subcategoria s WHERE c.usuario_id = @userId AND s.usuario_id = @userId;
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, tipo_agendamento, periodicidade, data_inicio, dia_execucao)
              SELECT @userId, c.id, s.id, 'Assinatura', 'FIXO', 'MENSAL', '2026-10-01', 1 FROM contas c, subcategoria s
              WHERE c.usuario_id = @userId AND c.nome_conta = @wallet AND s.usuario_id = @userId;
            INSERT INTO potesdegastos (usuario_id, nome_pote, valor_limite) VALUES (@userId, 'Pote', 100);
            INSERT INTO potesubcategorias (pote_id, subcategoria_id) SELECT p.id, s.id FROM potesdegastos p, subcategoria s WHERE p.usuario_id = @userId AND s.usuario_id = @userId;
            INSERT INTO googlecalendartokens (usuario_id, access_token) VALUES (@userId, 'token-google');
            INSERT INTO notificationconfigs (usuario_id) VALUES (@userId);
            INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada) VALUES (@userId, 'gemini', 'k1'), (@userId, 'weather', 'k2');
            INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria) VALUES (@userId, 'gemini', true);
            INSERT INTO logacessochaveapi (usuario_id, provedor, tipo_chave, operacao, sucesso) VALUES (@userId, 'gemini', 'propria', 'obter_chave', true);
            INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano) VALUES (@userId, 'gemini', 'propria', 1, '2026-10');
            INSERT INTO consentimentousuario (usuario_id, tipo_consentimento, versao_consentimento, consentimento_dado) VALUES (@userId, 'uso_dados_pessoais', '1.0', true);
            INSERT INTO assinaturasusuario (usuario_id, plano_id) SELECT @userId, id FROM planos WHERE nome_plano = 'Bronze';
            UPDATE usuarios SET conta_padrao_id = (SELECT id FROM contas WHERE usuario_id = @userId AND nome_conta = @wallet) WHERE id = @userId;
            """,
            new { userId, macro = $"Macro {suffix}", sub = $"Sub {suffix}", card = $"Cartão {suffix}", wallet = $"Carteira {suffix}" });
        return userId;
    }

    private static readonly string[] UserTables =
    [
        "chavesapiusuario", "preferenciaschaveapi", "logacessochaveapi", "rastreamentousoapi", "consentimentousuario", "assinaturasusuario",
        "transacoes", "agendamentos", "contas", "potesdegastos", "subcategoria", "macrocategoria", "googlecalendartokens", "notificationconfigs",
    ];

    private async Task<Dictionary<string, int>> CountsForAsync(int userId)
    {
        var counts = new Dictionary<string, int>();
        foreach (var table in UserTables)
        {
            counts[table] = await ApiKeysTestSupport.Scalar<int>(postgres, $"SELECT count(*) FROM {table} WHERE usuario_id = @userId", new { userId });
        }

        counts["usuarios"] = await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM usuarios WHERE id = @userId", new { userId });
        counts["faturas"] = await ApiKeysTestSupport.Scalar<int>(
            postgres, "SELECT count(*) FROM faturas f JOIN contas c ON c.id = f.conta_id WHERE c.usuario_id = @userId", new { userId });
        counts["potesubcategorias"] = await ApiKeysTestSupport.Scalar<int>(
            postgres, "SELECT count(*) FROM potesubcategorias ps JOIN potesdegastos p ON p.id = ps.pote_id WHERE p.usuario_id = @userId", new { userId });
        return counts;
    }

    private sealed class RecordingCacheCleaner(Func<int, Task<bool>> userStillExists) : IUserCacheCleaner
    {
        public List<(int UserId, bool UserStillExisted)> Calls { get; } = [];

        public async Task ClearAsync(int userId, CancellationToken cancellationToken) =>
            Calls.Add((userId, await userStillExists(userId)));
    }

    [Fact]
    [Trait("AC", "008-49")]
    public async Task DeleteAccount_RemovesTheWholeAccount_InOrder_KeepsOtherUsersAndGlobalData_AndClearsTheCache()
    {
        var target = await SeedFullUserAsync("5511999990001", "A");
        var other = await SeedFullUserAsync("5511999990002", "B");
        var globalSubcategories = await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL");
        var globalMacros = await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL");
        var before = await CountsForAsync(target);
        before.Values.ShouldAllBe(v => v >= 1); // o usuário tem dados em todas as tabelas
        var otherBefore = await CountsForAsync(other);

        var cleaner = new RecordingCacheCleaner(async id =>
            await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM usuarios WHERE id = @id", new { id }) > 0);
        using var factory = ApiKeysTestSupport.EnabledApi(postgres).WithWebHostBuilder(b =>
            b.ConfigureServices(services => services.AddSingleton<IUserCacheCleaner>(cleaner)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-KEY", AuthFixtures.JwtSecret);

        var (status, body) = await Send(client, HttpMethod.Delete, $"/api-keys/lgpd/deletar-conta/{target}", """{"confirmacao":"CONFIRMO_DELECAO"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","usuario_id":1,"registros_deletados":{"chaves_api":2,"preferencias":1,"logs":1,"uso":1,"consentimentos":1,"assinaturas":1,"transacoes":2,"agendamentos":1,"contas":2,"potes":1,"subcategorias":1,"macrocategorias":1,"usuarios":1},"total_registros":16,"mensagem":"Conta e todos os dados do usuário 1 foram deletados com sucesso"}""");

        (await CountsForAsync(target)).Values.ShouldAllBe(v => v == 0);
        (await CountsForAsync(other)).ShouldBe(otherBefore);
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL")).ShouldBe(globalSubcategories);
        (await ApiKeysTestSupport.Scalar<int>(postgres, "SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL")).ShouldBe(globalMacros);

        // O limpador de cache roda uma vez, com o id certo, depois do commit (o usuário já não existe).
        cleaner.Calls.ShouldBe([(target, false)]);
    }

    [Fact]
    [Trait("AC", "008-49")]
    public async Task DeleteAccount_FailureInTheLastStep_RollsEverythingBack()
    {
        var target = await SeedFullUserAsync("5511999990001", "A");
        var before = await CountsForAsync(target);

        // Falha injetada: a deleção de usuarios (último passo) lança, então os 12 DELETE anteriores precisam ser desfeitos.
        await ApiKeysTestSupport.Execute(postgres,
            """
            CREATE FUNCTION bloquear_delete_usuarios() RETURNS trigger AS $f$ BEGIN RAISE EXCEPTION 'falha injetada com segredo-do-banco'; END; $f$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_bloquear_delete BEFORE DELETE ON usuarios FOR EACH ROW EXECUTE FUNCTION bloquear_delete_usuarios();
            """);
        try
        {
            var (status, body) = await Send(Client(), HttpMethod.Delete, $"/api-keys/lgpd/deletar-conta/{target}", """{"confirmacao":"CONFIRMO_DELECAO"}""");

            status.ShouldBe(HttpStatusCode.InternalServerError);
            body.ShouldBe(Error("Erro ao deletar conta", "Erro ao deletar conta. Tente novamente."));
            body.ShouldNotContain("segredo-do-banco");
            (await CountsForAsync(target)).ShouldBe(before);
        }
        finally
        {
            await ApiKeysTestSupport.Execute(postgres, "DROP TRIGGER trg_bloquear_delete ON usuarios; DROP FUNCTION bloquear_delete_usuarios();");
        }
    }
}
