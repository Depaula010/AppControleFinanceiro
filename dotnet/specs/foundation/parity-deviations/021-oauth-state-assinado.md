# Desvio 021 — State do OAuth sem assinatura

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** segurança

## Comportamento atual (Flask)
`state` é o `usuario_id` em base64 url-safe, sem nonce nem assinatura (`google_calendar_oauth_service.py:63-69`): CSRF e troca de usuário.

## Comportamento proposto (.NET)
`state` = payload com `usuario_id` e expiração (10 min) assinado por HMAC.

## Impacto nos clientes
Links `connect-calendar` já enviados continuam funcionando (o state é gerado no clique).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
