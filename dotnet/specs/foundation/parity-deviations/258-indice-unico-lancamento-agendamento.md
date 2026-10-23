# Desvio 258 — Índice único (agendamento_id, competencia): o banco recusa lançamento duplicado

- **Estado:** implementado (PR #47)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
A idempotência do lançamento de agendamentos dependia só do código (`FOR UPDATE` + `EXISTS`); nada no banco impedia dois lançamentos do mesmo vencimento.

## Comportamento proposto (.NET)
Migration 0090: índice único parcial `ux_transacoes_agendamento_competencia` em `(agendamento_id, competencia)` quando os dois existem. Se já houver duplicata, a migration avisa e **não derruba o deploy** (não cria o índice). O executor trata a violação como "já lançado".

## Impacto nos clientes
Nenhum visível. Lançamento avulso e linhas antigas (competência vazia) não são afetados.

## Alternativas
Confiar só no código (rejeitado: segunda trava barata).

## Recomendação
Aprovar.

## Decisão do Rafael
Escolhido pelo Rafael na conversa de 23/10/2026 ("vamos fazer 2, 3 e 5"). Implementado no PR #47.
