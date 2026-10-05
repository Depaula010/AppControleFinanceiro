# Desvio 137 — Resumo matinal sem Google suprime os alertas financeiros

- **Estado:** proposto
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
`daily_briefing.py:118-132` com o resumo ativo chama `prepare_briefing_data`, que devolve `None` quando o Google Calendar não está conectado (`daily_briefing_service.py:233-237`); `daily_briefing.py:136` só monta o componente de alertas se o resumo **não** estiver ativo; `montar_mensagem_unificada` (`:50-57`) então devolve `None`. Com o padrão (resumo e alertas ativos), o usuário **sem Google** não recebe nada, nem a conta que vence hoje.

## Comportamento proposto (.NET)
Se o resumo falhar, montar os alertas financeiros mesmo assim e enviá-los.

## Impacto nos clientes
Usuários sem Google Calendar passam a receber os alertas financeiros no horário do resumo.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
