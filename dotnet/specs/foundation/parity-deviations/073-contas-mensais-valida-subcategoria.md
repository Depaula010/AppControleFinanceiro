# Desvio 073 — Contas mensais: subcategoria validada

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 005
- **Severidade:** segurança

## Comportamento atual (Flask)
Aceita subcategoria de outro usuário e vaza o nome dela; inexistente dá 500.

## Comportamento proposto (.NET)
subcategoria_id precisa pertencer ao usuário, senão 400.

## Impacto nos clientes
Nenhum cliente legítimo afetado.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 005.
