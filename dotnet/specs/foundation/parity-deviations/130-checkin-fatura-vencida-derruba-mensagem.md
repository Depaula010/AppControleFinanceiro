# Desvio 130 — Check-in: fatura vencida derruba a mensagem do usuário

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 013 (e 012, intenção "Contas atrasadas")
- **Severidade:** correção

## Comportamento atual (Flask)
`nightly_checkin_service.py:581-582` monta a seção FATURAS VENCIDAS lendo `fatura['nome_conta']`, mas a query `FaturasQueries.get_faturas_vencidas` (`faturas_queries.py:36`) devolve a coluna como `cartao`. Com qualquer fatura vencida o Python lança `KeyError: 'nome_conta'`; o laço do job (`nightly_checkin.py:155-160`) só registra o erro e o usuário **não recebe a mensagem** inteira. A sessão do Redis, porém, já foi criada antes (`nightly_checkin.py:118`) e fica ativa por 1 hora sem o usuário ter visto as contas.

## Comportamento proposto (.NET)
Ler `cartao`; e criar a sessão no Redis só depois de a mensagem ser montada com sucesso.

## Impacto nos clientes
Quem tem fatura `Aberta` vencida passa a receber o check-in (com a seção de faturas vencidas). Hoje o caso é raro porque quase toda fatura vencida já está `Fechada` (ver 131), mas vira frequente se o 131 for aprovado.

## Alternativas
Manter como está (paridade estrita): o .NET lança `InvalidOperationException` ao formatar fatura vencida, o laço registra e segue (implementado assim).

## Recomendação
Aprovar, junto com o 131.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
