using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.CoachAgenda;

/// <summary>Quanto de cada evento a agenda para o Coach mostra (spec 015, RN-015-03). Do mais privado para o mais completo.</summary>
public enum NivelDeDetalhe
{
    /// <summary>Só início, fim e dia inteiro.</summary>
    Ocupado,

    /// <summary>Ocupado mais o título.</summary>
    Titulos,

    /// <summary>Títulos mais descrição, local e nome do calendário.</summary>
    Completo,
}

public static class NivelDeDetalheTexto
{
    /// <summary>
    /// <c>ocupado</c>, <c>titulos</c> (ou <c>títulos</c>) e <c>completo</c>, sem diferenciar maiúsculas. <b>Falha segura:</b> qualquer outro valor vale
    /// <see cref="NivelDeDetalhe.Ocupado"/>, o mais privado (AC-015-06).
    /// </summary>
    public static NivelDeDetalhe Ler(string? valor) => valor?.Trim().ToLowerInvariant() switch
    {
        "completo" => NivelDeDetalhe.Completo,
        "titulos" or "títulos" => NivelDeDetalhe.Titulos,
        _ => NivelDeDetalhe.Ocupado,
    };
}

/// <summary>
/// Spec 015, RN-015-04: troca por marcas o que não deve ir a uma IA externa. Ordem: código depois de uma palavra-chave, links, e-mails, telefones e, por
/// último, sequências de 6 ou mais dígitos soltas.
/// </summary>
public static partial class MascaradorDeTexto
{
    public const string Link = "[link]";
    public const string Email = "[e-mail]";
    public const string Telefone = "[telefone]";
    public const string Codigo = "[código]";

    // "senha: abc123", "PIN=4455", "código de acesso: 998877": mascara só o valor, mantém a palavra
    [GeneratedRegex(@"(\b(?:senha|c[oó]digo|pin|token|acesso)\b(?:\s+de\s+acesso)?\s*[:=]\s*)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodigoComPalavra();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+|\b(?:meet\.google\.com|zoom\.us|teams\.microsoft\.com|teams\.live\.com)\S*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Links();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex Emails();

    // telefone brasileiro: +55, DDD (com ou sem parênteses), 8 ou 9 dígitos com ou sem hífen ou espaço
    [GeneratedRegex(@"(?<![\d])(?:\+?55[\s-]?)?(?:\(?\d{2}\)?[\s-]?)?(?:9\d{4}|\d{4})[\s-]?\d{4}(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex Telefones();

    [GeneratedRegex(@"(?<![\d])\d{6,}(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex DigitosLongos();

    public static string Mascarar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var resultado = CodigoComPalavra().Replace(texto, "$1" + Codigo);
        resultado = Links().Replace(resultado, Link);
        resultado = Emails().Replace(resultado, Email);
        resultado = Telefones().Replace(resultado, Telefone);
        return DigitosLongos().Replace(resultado, Codigo);
    }
}

/// <summary>Um evento como o Google devolve (o texto de <c>Start</c>/<c>End</c> é <c>dateTime</c> ou <c>date</c>). O id nunca entra aqui.</summary>
public sealed record EventoBruto(string Titulo, string Inicio, string Fim, string Local, string Descricao, bool DiaInteiro, string? NomeDoCalendario, string? IdDoCalendario);

/// <summary>O evento que sai para o Coach: o que o nível não inclui fica <c>null</c> (e nunca é escrito na resposta).</summary>
public sealed record EventoParaOCoach(
    string Inicio, string Fim, bool DiaInteiro, NivelDeDetalhe Nivel, string? Titulo, string? Descricao, string? Local, string? Calendario);

public static class AgendaParaOCoach
{
    public const int LimiteDoTitulo = 120;
    public const int LimiteDoLocal = 120;
    public const int LimiteDaDescricao = 600;

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <param name="nivelDe">o nível do calendário (recebe o id do calendário).</param>
    /// <param name="mascarar">RN-015-04.</param>
    /// <returns>ordenado por início (RN-015-02).</returns>
    public static IReadOnlyList<EventoParaOCoach> Montar(IEnumerable<EventoBruto> eventos, Func<string?, NivelDeDetalhe> nivelDe, bool mascarar)
    {
        var saida = new List<(DateTimeOffset Quando, EventoParaOCoach Evento)>();
        foreach (var e in eventos)
        {
            var nivel = nivelDe(e.IdDoCalendario);
            var (inicio, quando) = Horario(e.Inicio, e.DiaInteiro);
            var (fim, _) = Horario(e.Fim, e.DiaInteiro);
            saida.Add((quando, new EventoParaOCoach(
                inicio, fim, e.DiaInteiro, nivel,
                nivel >= NivelDeDetalhe.Titulos ? Texto(e.Titulo, LimiteDoTitulo, mascarar) : null,
                nivel >= NivelDeDetalhe.Completo ? Texto(e.Descricao, LimiteDaDescricao, mascarar) : null,
                nivel >= NivelDeDetalhe.Completo ? Texto(e.Local, LimiteDoLocal, mascarar) : null,
                nivel >= NivelDeDetalhe.Completo ? e.NomeDoCalendario : null)));
        }

        return [.. saida.OrderBy(x => x.Quando).Select(x => x.Evento)];
    }

    // o corte vale DEPOIS do mascaramento (AC-015-08): uma marca nunca é cortada pela metade
    private static string Texto(string? texto, int limite, bool mascarar)
    {
        var t = mascarar ? MascaradorDeTexto.Mascarar(texto) : texto ?? string.Empty;
        t = t.Trim();
        return t.Length <= limite ? t : t[..limite].TrimEnd() + "…";
    }

    // dia inteiro: só a data (o fim é exclusivo, como no Google); com hora: ISO com o deslocamento de São Paulo
    private static (string Texto, DateTimeOffset Quando) Horario(string bruto, bool diaInteiro)
    {
        if (diaInteiro)
        {
            var data = bruto.Length >= 10 ? bruto[..10] : bruto;
            var ok = DateTimeOffset.TryParse(data, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d);
            return (data, ok ? d : DateTimeOffset.MaxValue);
        }

        if (!DateTimeOffset.TryParse(bruto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instante))
        {
            return (bruto, DateTimeOffset.MaxValue);
        }

        var local = TimeZoneInfo.ConvertTime(instante, SaoPaulo);
        return (local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), instante);
    }
}
