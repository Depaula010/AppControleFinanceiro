# Desvio 250 — daysBeforeDue em coluna própria

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 007, 013
- **Severidade:** correção

## Comportamento atual (Flask)
`PUT /api/user/notifications` grava `daysBeforeDue` em `notificationconfigs.alerta_potes_threshold` (limiar de percentual do pote). Em produção o valor está em 1, indicando o bug em ação; o campo não tem efeito de antecedência de vencimento.

## Comportamento proposto (.NET)
Coluna própria para a antecedência (`dias_antes_vencimento`, inteiro, default 3), criada por migration idempotente (faixa 0070-79). `alerta_potes_threshold` deixa de ser sobrescrito por `daysBeforeDue`. O GET devolve `daysBeforeDue` da coluna nova. Na migration, o valor atual de `alerta_potes_threshold` não é copiado (o 1 de hoje é lixo).

## Impacto nos clientes
Angular continua enviando e lendo `daysBeforeDue`; o limiar do pote deixa de ser alterado por essa tela. Rafael deve acertar `alerta_potes_threshold` direto no banco se quiser outro valor que não 1.

## Alternativas
Manter a paridade (rejeitado no P-007-1 original, reaberto com a evidência dos dados de produção).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"), após o resultado da consulta 10 do 06-conferencia-onda-2.sql.
