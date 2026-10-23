# Desvio 254 — Diálogo do check-in com estado explícito (sem mudança de comportamento)

- **Estado:** implementado (PRs #41 e #46)
- **Features afetadas:** 012
- **Severidade:** operação

## Comportamento atual (Flask)
O responder do check-in manipulava três chaves soltas no Redis (sessão do job, flag ativa, fila de valores) com TTLs e limpezas espalhados.

## Comportamento proposto (.NET)
`CheckinDialogState` (`Expired`, `AwaitingConfirmation`, `AwaitingValues`) e `CheckinDialogStore` como único dono das chaves, injetado no responder. Formato das chaves e comportamento idênticos.

## Impacto nos clientes
Nenhum. Registrado por rastreabilidade, já que o número foi citado em código e specs.

## Alternativas
Manter as chaves soltas (cada passo novo aumenta o risco de esquecer uma limpeza).

## Recomendação
Aprovar (refatoração).

## Decisão do Rafael
Escolhido pelo Rafael na conversa de 20/10/2026 ("Estado explícito do check-in"). Implementado nos PRs #41 e #46.
