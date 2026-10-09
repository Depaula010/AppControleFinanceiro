# Desvio 120 — Alertas de calendário: tipos do corpo de /config e /ativar

- **Estado:** implementado (feature 014, 2026-10-09)
- **Features afetadas:** 014
- **Severidade:** correção

## Comportamento atual (Flask)
`calendar_alerts.py:51-52` e `calendar_alert_config_service.py:36-60`: `ativo` vira parâmetro SQL sem checar o tipo. `"true"`/`"false"` (texto) funcionam por conversão do PostgreSQL (e a mensagem diz "ativados" mesmo com `"false"`, porque o texto é verdadeiro no Python); `1`, `[]` ou texto qualquer dão 400 com `str(e)` do banco. `minutos_antes: true` passa no `isinstance(int)` e falha no banco; `5.0` cai na mensagem de faixa.

## Comportamento proposto (.NET)
`ativo` só aceita `true`/`false` JSON, com 400 `ativo deve ser true ou false` para o resto; `minutos_antes` só inteiro 1 a 60 (já é assim), booleano com a mensagem da faixa.

## Impacto nos clientes
Nenhum cliente conhecido chama as rotas. Enquanto não aprovado, o .NET responde 400 `Erro ao atualizar configuração` para `ativo` não booleano (divergência Q-014-3 da spec) e deixa de aceitar `"true"`/`"false"` como texto.

## Alternativas
Manter como está (paridade estrita): aceitar o texto `true`/`false` do PostgreSQL e dar 400 de texto fixo nos demais.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
