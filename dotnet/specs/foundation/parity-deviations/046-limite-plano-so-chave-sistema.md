# Desvio 046 — API keys: limite do plano conta só chamadas com chave do sistema

- **Estado:** implementado (feature 008, 2026-10-02)
- **Features afetadas:** 008
- **Severidade:** correção

## Comportamento atual (Flask)
`_verificar_limite_plano` soma `propria` + `sistema` (`gerenciador_chaves_api.py:163-169`), mas a mensagem promete "sem limites" com chave própria.

## Comportamento proposto (.NET)
Somar só `tipo_chave = 'sistema'`.

## Impacto nos clientes
Usuários com plano passam a ter limite efetivo maior. Hoje nenhum usuário tem plano (`assinaturasusuario` vazia).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
