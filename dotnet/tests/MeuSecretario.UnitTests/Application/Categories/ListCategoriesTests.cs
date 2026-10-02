using MeuSecretario.Application.Categories;
using MeuSecretario.Domain.Categories;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.Categories;

public class ListCategoriesTests
{
    private readonly ICategoryRepository _repository = Substitute.For<ICategoryRepository>();
    private readonly ListCategories _sut;

    public ListCategoriesTests()
    {
        _sut = new ListCategories(_repository);
    }

    [Theory]
    [InlineData("Receita", CategoryTypeFilter.Receita)]
    [InlineData("Despesa", CategoryTypeFilter.Despesa)]
    [InlineData("receita", CategoryTypeFilter.None)]
    [InlineData(null, CategoryTypeFilter.None)]
    [Trait("AC", "003-05")]
    public async Task PassesTheUserIdAndTheParsedFilterToTheRepository(string? tipo, CategoryTypeFilter expected)
    {
        _repository.ListVisibleAsync(7, expected, Arg.Any<CancellationToken>()).Returns([]);

        await _sut.ExecuteAsync(7, tipo, CancellationToken.None);

        await _repository.Received(1).ListVisibleAsync(7, expected, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "003-06")]
    public async Task GroupsTheRowsIntoMacroCategories()
    {
        _repository.ListVisibleAsync(1, CategoryTypeFilter.None, Arg.Any<CancellationToken>()).Returns(
        [
            new CategoryRow("Renda", 1, "Renda Principal", 43, "Outros"),
            new CategoryRow("Renda", 1, "Renda Principal", 1, "Salário"),
        ]);

        var result = await _sut.ExecuteAsync(1, null, CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Subcategorias.Select(s => s.Id).ShouldBe([43, 1]);
    }

    [Fact]
    [Trait("AC", "003-09")]
    public async Task NoRows_ReturnsAnEmptyList()
    {
        _repository.ListVisibleAsync(1, CategoryTypeFilter.None, Arg.Any<CancellationToken>()).Returns([]);

        (await _sut.ExecuteAsync(1, null, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "003-30")]
    public async Task RepositoryFailure_Propagates()
    {
        _repository.ListVisibleAsync(1, CategoryTypeFilter.None, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        await Should.ThrowAsync<InvalidOperationException>(() => _sut.ExecuteAsync(1, null, CancellationToken.None));
    }
}
