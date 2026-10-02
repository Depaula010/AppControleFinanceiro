# Desvio 053 — Google: link de conexão assinado (/connect-calendar)

- **Estado:** aprovado
- **Features afetadas:** 010, 012
- **Severidade:** segurança

## Comportamento atual (Flask)
`/connect-calendar/<id>` não tem autenticação nem assinatura (`calendar_handler.py:25-102`): qualquer pessoa abre o link de outro usuário, conclui o consentimento com a **própria** conta Google e o callback grava os tokens dela na linha da vítima (alertas e Drive da vítima passam a usar a conta do atacante). O desvio 021 (state assinado) não resolve, porque o atacante obtém um `state` válido chamando a própria rota. Também enumera ids (404 × 302).

## Comportamento proposto (.NET)
O link enviado por WhatsApp passa a levar `?t=<token>`, HMAC de `{usuario_id, expiração}` (7 dias), gerado por `GoogleLinks.ConnectUrl`; `/connect-calendar` sem token válido responde 403 (HTML simples) e o `state` herda o `usuario_id` do token. Opcional e **desligado por padrão**: `GOOGLE_CONNECT_LEGACY_UNTIL` (data) aceita links sem `t` até aquela data, como válvula na virada.

## Impacto nos clientes
Links já enviados (sem `t`) deixam de funcionar; o usuário pede outro pelo WhatsApp (o fluxo normal gera link novo).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** o `oauth_proxy` (Render) descarta a query string no `/connect-calendar`; ele precisa ser atualizado para repassá-la (`oauth_proxy/app.py`, já corrigido neste repositório) e **reimplantado antes da virada**, senão todo link assinado chegaria sem `t` e responderia 403.
