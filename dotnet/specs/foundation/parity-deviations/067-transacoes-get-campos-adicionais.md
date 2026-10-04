# Desvio 067 — GET /api/transactions devolve campos que o Angular lê

- **Estado:** implementado (feature 004, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
A lista não traz conta_id, subcategoria_id, observacoes, data, conta_bancaria nem conta_bancaria_id; a tela mostra data e conta vazias.

## Comportamento proposto (.NET)
Os campos são acrescentados à resposta (só acréscimo).

## Impacto nos clientes
Nenhum cliente quebra.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
