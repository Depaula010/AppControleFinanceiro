# Desvio 068 — PUT em Transferência ou Pagamento Fatura passa a dar 400

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
Editar desfaz a transferência pela metade.

## Comportamento proposto (.NET)
400 com a mensagem: Transferências e pagamentos de fatura não podem ser editados. Exclua e lance novamente.

## Impacto nos clientes
O Angular não edita esses tipos.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
