# Desvio 043 — API keys e LGPD: usuário inexistente responde 404

- **Estado:** aprovado
- **Features afetadas:** 008
- **Severidade:** correção

## Comportamento atual (Flask)
Violação de FK (`23503`) em POST de chave, preferência e consentimento com `usuario_id` inexistente dá 500.

## Comportamento proposto (.NET)
404 `{"erro":"Usuário não encontrado","mensagem":"Nenhum usuário com ID <id>"}`.

## Impacto nos clientes
Só a mensagem e o status mudam.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
