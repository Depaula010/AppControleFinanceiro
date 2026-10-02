# Desvio 057 — Calendar: evento na virada do dia e atualização de data

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** correção

## Comportamento atual (Flask)
Sem `hora_fim`, o fim é `hora_inicio + 1 h` calculado sobre `date.today()` e reduzido à hora (`calendar_management_service.py:74-76`): início 23:30 ⇒ fim 00:30 no **mesmo dia**, que o Google rejeita (400). Em `update_event`, só mudar a data ou só o `hora_fim` é ignorado (o bloco só age com `hora_inicio`, `:202-226`) e a duração original não é mantida; a resposta diz que atualizou.

## Comportamento proposto (.NET)
O fim vai para o dia seguinte quando passa de 24:00; data e horas são atualizadas independentemente e a duração é preservada quando só o início muda.

## Impacto nos clientes
Eventos que hoje falham passam a ser criados; remarcações só de data passam a valer.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
