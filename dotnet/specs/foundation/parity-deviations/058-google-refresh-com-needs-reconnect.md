# Desvio 058 — Google: não renovar token quando needs_reconnect está ligado

- **Estado:** aprovado
- **Features afetadas:** 010, 014
- **Severidade:** correção

## Comportamento atual (Flask)
`get_credentials` não consulta `needs_reconnect` (`google_calendar_oauth_service.py:260-412`): todo uso de um token já revogado chama o Google de novo, falha e re-marca (a notificação é limitada a 1 por semana, a chamada não).

## Comportamento proposto (.NET)
Com `needs_reconnect = TRUE` e `token_expiry` vencido, devolver "precisa reconectar" sem chamar o Google; o flag volta a `FALSE` na reconexão.

## Impacto nos clientes
Menos chamadas ao Google; um `invalid_grant` transitório só se recupera reconectando.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
