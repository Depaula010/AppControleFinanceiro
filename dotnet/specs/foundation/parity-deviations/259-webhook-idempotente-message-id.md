# Desvio 259 — Webhook do WhatsApp ignora a mesma mensagem reenviada (message_id)

- **Estado:** implementado (PR #48); depende do bot enviar message_id
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
O webhook não deduplica: se o bot reenvia a mesma mensagem (timeout, nova tentativa), "gastei 50" pode virar duas pendências e o check-in responder duas vezes.

## Comportamento proposto (.NET)
O corpo aceita `message_id` (opcional, até 128 caracteres). A mesma mensagem do mesmo número em até 10 min responde `sucesso` com `resposta` vazia, sem Gemini nem pipeline. Sem `message_id` nada é descartado; Redis fora processa; falha no processamento libera o id.

## Impacto nos clientes
**O bot (Node) precisa enviar `message_id`** (no Baileys, `message.key.id`); enquanto não enviar, nada muda.

## Alternativas
Deduplicar por texto (rejeitado: engoliria "sim"/"ok" repetidos de propósito).

## Recomendação
Aprovar; combinar a mudança no bot.

## Decisão do Rafael
Escolhido pelo Rafael na conversa de 23/10/2026 ("vamos fazer 2, 3 e 5"). Implementado no PR #48 (spec 012, AC-012-018).
