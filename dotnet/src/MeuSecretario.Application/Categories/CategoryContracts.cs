using MeuSecretario.Domain.Categories;

namespace MeuSecretario.Application.Categories;

public interface ICategoryRepository
{
    /// <summary>Subcategorias visíveis ao usuário (globais e próprias), já na ordem do contrato (spec 003 §3.5).</summary>
    Task<IReadOnlyList<CategoryRow>> ListVisibleAsync(int userId, CategoryTypeFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// Buscas por nome usadas pelas features 004 (transferência, pagamento de fatura) e 011 (bot). Sempre ordenam por
/// <c>id</c> e devolvem a primeira (desvio 030: <c>Outros</c> global duplicado). <c>null</c> = não existe; quem chama decide o erro.
/// </summary>
public interface ICategoryLookup
{
    /// <summary><c>Investimentos de Curto Prazo</c> no grupo <c>Meta Financeira</c> (global ou do usuário).</summary>
    Task<int?> FindShortTermInvestmentAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>Quitação de Empréstimos (Principal)</c> no grupo <c>Meta Financeira</c> (global ou do usuário).</summary>
    Task<int?> FindLoanPrincipalAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>Outros</c> global de <c>Receitas Gerais</c> (renda) ou de <c>Despesas Gerais</c>.</summary>
    Task<int?> FindFallbackAsync(bool income, CancellationToken cancellationToken);
}
