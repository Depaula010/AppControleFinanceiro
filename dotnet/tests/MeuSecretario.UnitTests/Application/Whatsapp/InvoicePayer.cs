using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Application.Whatsapp;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Pagador de fatura do check-in com dependências falsas (os testes que não tocam em fatura usam <see cref="None"/>).</summary>
internal static class InvoicePayer
{
    public static CheckinInvoicePayer None(IClock clock) =>
        new(Substitute.For<IAutomationLookups>(), Substitute.For<ITransactionPosting>(), clock);
}
