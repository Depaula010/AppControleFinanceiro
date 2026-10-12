# Desvio 076 — Receita fora da reserva de emergência

- **Estado:** implementado (feature 005, 2026-10-04)
- **Features afetadas:** 005
- **Severidade:** correção

## Comportamento atual (Flask)
A API aceita receita com incluir_na_reserva.

## Comportamento proposto (.NET)
Receita é ignorada na reserva.

## Impacto nos clientes
Nenhum efeito para o Angular, que já impede.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 005.
