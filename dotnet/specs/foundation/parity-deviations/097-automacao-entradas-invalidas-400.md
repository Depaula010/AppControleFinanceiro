# Desvio 097 — Rotas de automação: entradas inválidas viram 400 com mensagem

- **Estado:** proposto
- **Features afetadas:** 011
- **Severidade:** correção

## Comportamento atual (Flask)
Entradas absurdas viram 500 com `str(e)` (ou gravam lixo):
- `/api/transacao`: campos de texto que não são texto; `valor` `nan` (aceito e gravado como `NaN`).
- `/webhook-sms-payment`: `data_pagamento` inválida (500); `valor_pago` negativo (grava despesa **positiva**).
- `/webhook-automate`: `tipo_fluxo` fora de `Renda`/`Despesa`, `valor_decimal` nulo (500) ou zero (cria pendência de R$ 0,00) e usuário sem contas (`IndexError`).
- `PATCH /api/agendamento/<id>/reserva`: `incluir` que não é booleano (o resultado depende do driver).
- `GET /api/agendamentos/reserva`: `limit`/`offset` que o `int()` não converte ou negativos (500).

O .NET reproduz a falha com **500 e mensagem fixa** (desvio 008), sem gravar nada, exceto `nan`, que responde o 400 de "valor deve ser positivo" porque `decimal` não representa `NaN`.

## Comportamento proposto (.NET)
400 com mensagem em português por caso (texto a definir na aprovação), sem gravar nada.

## Impacto nos clientes
Só quem hoje já recebe 500 (ou grava lixo). Nenhum cliente conhecido depende desses comportamentos.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
