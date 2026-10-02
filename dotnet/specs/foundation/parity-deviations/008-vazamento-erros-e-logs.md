# Desvio 008 — Vazamento de str(e) e de segredos em log

- **Estado:** aprovado
- **Features afetadas:** todas
- **Severidade:** segurança

## Comportamento atual (Flask)
500 devolve `str(e)` no GET de bills, PUT de profile, todo `/api-keys` e todo `/admin`. `whatsapp_handler.py:86-87` imprime a `x-api-key` recebida e 5+5 caracteres da `API_SECRET_KEY`; `transaction_handler.py:194` imprime o corpo bruto de `/api/transacao` (inclui `user_api_key`).

## Comportamento proposto (.NET)
Erros 500 devolvem a mensagem fixa de cada rota (envelope preservado) sem a exceção interna; detalhes vão ao log com correlation id. Filtro Serilog mascara `x-api-key`, `user_api_key`, `Authorization` e tokens.

## Impacto nos clientes
Clientes que mostravam a mensagem técnica passam a ver texto genérico.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
