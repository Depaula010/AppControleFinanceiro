# Desvio 089 — Dashboard: tipo Renda x Receita nas transações recentes

- **Estado:** proposto
- **Features afetadas:** 006
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
`GET /api/dashboard/recent` (e `/api/transactions/recent`, `/api/transacoes/recentes`) devolve o `tipo_transacao` cru do banco (`api.py:981`), ou seja, `Renda`. O desvio 001 (aprovado) manda a saída da API usar `Receita`. O Angular compara `tipo === 'Renda'` para o ícone e a cor (`dashboard.component.ts:289-302`, `dashboard.model.ts:49`).

## Comportamento proposto (.NET)
Decidir: (a) manter `Renda` nas recentes e excluir esta rota do desvio 001, ou (b) devolver `Receita` e ajustar o Angular na mesma virada. A 006 implementa o comportamento do Flask (`Renda`).

## Impacto nos clientes
Com (b) sem ajustar o front, toda receita recente perde ícone e cor (cai no ramo neutro).

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
(a): manter `Renda` nas recentes até o front mudar.

## Decisão do Rafael
(pendente)
