using System.Globalization;
using System.Text;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// <c>json.dumps</c> da lista de contas que vai nos prompts (<c>[{"nome": ..., "tipo": ...}]</c>; com <c>id</c> na extração de
/// transferência), com os separadores <c>, </c> e <c>: </c> e <c>ensure_ascii</c> do Python.
/// </summary>
public static class AccountsJson
{
    public static string Dump(IEnumerable<AccountRef> accounts, bool withId)
    {
        var sb = new StringBuilder("[");
        var first = true;
        foreach (var account in accounts)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append('{');
            if (withId)
            {
                sb.Append("\"id\": ").Append(account.Id.ToString(CultureInfo.InvariantCulture)).Append(", ");
            }

            sb.Append("\"nome\": ").Append(PythonJson.Quote(account.Name))
                .Append(", \"tipo\": ").Append(PythonJson.Quote(account.Type))
                .Append('}');
        }

        return sb.Append(']').ToString();
    }
}
