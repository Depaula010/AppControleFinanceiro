# Desvio 047 — API keys: gravar o log de acesso

- **Estado:** implementado (feature 008, 2026-10-02)
- **Features afetadas:** 008
- **Severidade:** correção

## Comportamento atual (Flask)
`_registrar_log_acesso` (`gerenciador_chaves_api.py:263-286`) não tem chamador: `logacessochaveapi` fica sempre vazia, `/logs` e a exportação também.

## Comportamento proposto (.NET)
A porta de chaves grava em `logacessochaveapi` (operação, sucesso, erro sem segredo, `endereco_ip` nulo) a cada resolução de chave.

## Impacto nos clientes
Auditoria LGPD passa a ter dados.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
