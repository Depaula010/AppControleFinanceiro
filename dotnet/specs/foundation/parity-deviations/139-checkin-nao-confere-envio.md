# Desvio 139 — Check-in não confere o envio e deixa a sessão ativa

- **Estado:** proposto
- **Features afetadas:** 013, 012
- **Severidade:** correção

## Comportamento atual (Flask)
`nightly_checkin.py:118-146`: cria a sessão no Redis (`nightly_checkin_active:{numero}`, 1 h) e envia a mensagem; o retorno de `enviar_notificacao_whatsapp` é ignorado e o log diz "Check-in consolidado enviado" mesmo se o bot falhou. A próxima mensagem do usuário pode ser lida como resposta a um check-in que ele nunca recebeu. O resumo matinal (`daily_briefing.py:166-174`) também não confere o envio.

## Comportamento proposto (.NET)
Se o envio falhar, apagar a sessão e registrar a falha (no resumo, registrar o erro em vez de "enviada").

## Impacto nos clientes
Nenhum campo; evita resposta a uma sessão fantasma.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
