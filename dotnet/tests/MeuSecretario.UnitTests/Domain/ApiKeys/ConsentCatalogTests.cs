using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.UnitTests.Domain.ApiKeys;

public class ConsentCatalogTests
{
    [Fact]
    [Trait("AC", "008-45")]
    public void Catalog_HasTheFiveTypesInFlaskOrder_OnlyTheFirstTwoRequired()
    {
        ConsentCatalog.All.Select(c => c.Type).ShouldBe(
            ["uso_dados_pessoais", "armazenamento_chaves_api", "rastreamento_uso", "comunicacao_whatsapp", "compartilhamento_terceiros"]);
        ConsentCatalog.All.Select(c => c.Required).ShouldBe([true, true, false, false, false]);
        ConsentCatalog.CurrentVersion.ShouldBe("1.0");
    }

    [Fact]
    public void Descriptions_AreTheLiteralFlaskTexts()
    {
        ConsentCatalog.Description("uso_dados_pessoais").ShouldBe("Uso de dados pessoais para funcionamento do sistema");
        ConsentCatalog.Description("armazenamento_chaves_api").ShouldBe("Armazenamento de chaves de API criptografadas");
        ConsentCatalog.Description("rastreamento_uso").ShouldBe("Rastreamento de uso para billing e analytics");
        ConsentCatalog.Description("comunicacao_whatsapp").ShouldBe("Envio de notificações via WhatsApp");
        ConsentCatalog.Description("compartilhamento_terceiros").ShouldBe("Compartilhamento com APIs de terceiros (Gemini, Weather, etc)");
        ConsentCatalog.Description("outro").ShouldBe("Descrição não disponível");
    }

    [Theory]
    [InlineData("uso_dados_pessoais", true)]
    [InlineData("USO_DADOS_PESSOAIS", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("marketing", false)]
    public void IsValid_IsExact(string? type, bool expected) => ConsentCatalog.IsValid(type).ShouldBe(expected);

    [Fact]
    [Trait("AC", "008-42")]
    public void InvalidTypeMessage_ListsTheTypesInPythonReprFormat()
    {
        ConsentCatalog.InvalidTypeMessage("marketing").ShouldBe(
            "Tipo de consentimento inválido: marketing. Válidos: " +
            "['uso_dados_pessoais', 'armazenamento_chaves_api', 'rastreamento_uso', 'comunicacao_whatsapp', 'compartilhamento_terceiros']");
    }
}
