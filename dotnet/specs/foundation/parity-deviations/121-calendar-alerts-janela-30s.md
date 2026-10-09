# Desvio 121 — Alertas de calendário: a janela de ±30 s perde o aviso se o ciclo atrasa

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 014 (e 013, que agenda o job)
- **Severidade:** correção

## Comportamento atual (Flask)
`calendar_alert_service.py:121-129`: o job roda a cada minuto e cada ciclo procura só eventos que começam em `[agora + minutos_antes − 30 s, agora + minutos_antes + 30 s]`. Se um ciclo atrasar, falhar ou o Google demorar, o evento daquele minuto passa sem aviso, e nada tenta de novo.

## Comportamento proposto (.NET)
Janela `(agora + minutos_antes − 60 s, agora + minutos_antes + 30 s]`: o ciclo seguinte ainda enxerga o evento que o anterior perdeu. O claim de 2 h (`calendar_alert:{md5}`) evita o aviso repetido.

## Impacto nos clientes
Usuário recebe alertas que hoje se perdem. Com o Redis fora do ar (claim fail-open) um evento pode ser avisado duas vezes.

## Alternativas
Manter como está (paridade estrita). Ou janela maior só quando o Redis estiver disponível.

## Recomendação
Aprovar, junto com a 013 (o intervalo do job).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
