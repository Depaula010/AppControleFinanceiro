namespace MeuSecretario.Domain.Transactions;

/// <summary>Divisão do total de uma compra parcelada em centavos (desvio 276).</summary>
public static class InstallmentSplit
{
    /// <summary>
    /// <paramref name="perInstallmentExact"/> é o total dividido por <paramref name="count"/> sem arredondar. Devolve o valor das
    /// parcelas 2..N e o da 1ª (que leva o resíduo), de modo que <c>first + per * (count - 1) == total</c> em centavos.
    /// </summary>
    public static (decimal PerInstallment, decimal First) Split(decimal perInstallmentExact, int count)
    {
        var total = Math.Round(perInstallmentExact * count, 2, MidpointRounding.AwayFromZero);
        var per = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var first = total - (per * (count - 1));
        return (per, first);
    }
}
