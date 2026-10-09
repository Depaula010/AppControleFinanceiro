# Desvio 136 — Horário com minutos nunca dispara (resumo matinal e check-in)

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 013, 007
- **Severidade:** correção

## Comportamento atual (Flask)
`daily_briefing.py:90` e `nightly_checkin.py:56` zeram só segundos e rodam no minuto 0 (`@hourly` e `0 0 18..23`); `notification_config_service.py:326-329` e `:476-479` comparam `TIME` por igualdade, apesar do docstring "Compara apenas a HORA". Quem configurou `07:30` (resumo) ou `19:15` (check-in) nunca recebe: o job só enxerga `HH:00`.

## Comportamento proposto (.NET)
Rodar os dois jobs a cada minuto, mantendo a igualdade: cada usuário recebe no minuto escolhido.

## Impacto nos clientes
Quem configurou horário com minutos passa a receber. Custo: uma consulta indexada por minuto.

## Alternativas
Comparar só a hora (docstring): quem escolhe `07:30` receberia às `07:00`. Ou manter como está (implementado assim).

## Recomendação
Aprovar o cron por minuto.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
