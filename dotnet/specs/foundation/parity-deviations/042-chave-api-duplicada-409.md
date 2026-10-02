# Desvio 042 — API keys: chave duplicada responde 409

- **Estado:** aprovado
- **Features afetadas:** 008
- **Severidade:** correção

## Comportamento atual (Flask)
`POST /api-keys/usuario/<id>` com chave já existente (ativa ou desativada) dá 500 pela UNIQUE (`api_keys.py:73-89`).

## Comportamento proposto (.NET)
409 `{"erro":"Chave já cadastrada","mensagem":"Já existe uma chave para este provedor. Use PUT /api-keys/chave/<id>."}`.

## Impacto nos clientes
Só a mensagem muda.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
