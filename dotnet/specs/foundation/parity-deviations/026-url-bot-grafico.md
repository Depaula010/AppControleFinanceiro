# Desvio 026 — URL do bot no intent de gráfico

- **Estado:** aprovado
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`analytics_intents.py:185` usa `WHATSAPP_BOT_URL` com padrão `http://localhost:3003` (variável inexistente em produção); o envio de gráfico por esse intent falha.

## Comportamento proposto (.NET)
Usar o `IBotClient` comum (`BOT_WHATSAPP_URL`/sessão).

## Impacto nos clientes
Gráficos pedidos pelo WhatsApp passam a ser enviados.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
