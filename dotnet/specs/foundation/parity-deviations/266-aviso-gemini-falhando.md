# Desvio 266 — Aviso ao dono quando o Gemini recusa as chamadas

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 012
- **Severidade:** operação

## Comportamento atual (Flask)
Quando o Gemini falha (cota, saldo, chave, modelo desativado), o webhook só devolve 429/500 e registra no log. O usuário vê "Estamos com instabilidade" e o dono só descobre quando percebe o bot mudo (26/10/2026: horas sem resposta por falta de saldo e depois por modelo indisponível).

## Comportamento proposto (.NET)
Toda falha do webhook entra no aviso operacional já existente (desvio 261, `OPS_ALERT_WHATSAPP`, no máximo um aviso por origem a cada 30 minutos), com a causa e o que fazer:
- `WHATSAPP-GEMINI-COTA`: cota ou saldo esgotados (lembra que "menu" e os comandos fixos seguem funcionando);
- `WHATSAPP-GEMINI-CHAVE`: permissão negada (confira `GEMINI_API_KEY` e `usar_chave_propria`);
- `WHATSAPP-GEMINI-FALHA`: outra falha do Gemini, com a mensagem segura (ex. HTTP 404) e o `UPDATE` que troca o modelo;
- `WHATSAPP-WEBHOOK`: qualquer outra exceção, só com o tipo.
O texto nunca leva mensagem do usuário, chave nem corpo de resposta.

## Impacto nos clientes
Nenhuma rota muda. O dono recebe um WhatsApp quando o Gemini quebra. Sem `OPS_ALERT_WHATSAPP` configurado nada muda.

## Alternativas
Só log (o que havia). Aviso por e-mail ou push (outro canal, mais infraestrutura).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado na conversa de 27/10/2026 ("como corrigimos isso para não acontecer mais?" / "pode").
