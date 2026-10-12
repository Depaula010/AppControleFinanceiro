# Desvio 073 — Contas mensais: subcategoria validada

- **Estado:** implementado (feature 005, 2026-10-04)
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

## Implementação
A spec 005 (§9, P-005-2) define a resposta como **404** `"Subcategoria não encontrada"` (mesma regra e texto da 004), e não 400 como resume o texto acima; foi o que se implementou. Valem: POST depois da checagem da conta; PUT entre `valor_previsto` e `conta_id`; `subcategoria_id` não inteiro ou `null` no PUT vira 0 e responde 404.
