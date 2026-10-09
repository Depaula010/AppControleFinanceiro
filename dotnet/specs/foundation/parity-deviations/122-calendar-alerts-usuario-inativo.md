# Desvio 122 — Alertas de calendário: usuário inativo ainda recebe alertas

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 014 (e 013)
- **Severidade:** correção

## Comportamento atual (Flask)
`calendar_alert_config_service.py:96-104`: a consulta dos usuários com alerta ativo não filtra `usuarios.ativo`; usuário desativado que deixou `alertas_tarefas_ativo = true` e o Google conectado continua recebendo WhatsApp.

## Comportamento proposto (.NET)
Acrescentar `AND u.ativo = true` à consulta.

## Impacto nos clientes
Usuário inativo deixa de receber alertas (esperado).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
