# Desvio 157 — Pagamento Fatura é inalcançável com "paguei..."

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
O passo "pagamento" (`whatsapp_handler.py:446`) intercepta qualquer texto com `paguei`, `quitei`, `liquidei`, `saldei` ou `zerei` antes de consultar o Gemini. O exemplo do próprio menu de ajuda, "paguei 500 da fatura do nubank", cai nesse fluxo e registra uma despesa avulsa "fatura do nubank" (ou quita uma conta fixa parecida) em vez de pagar a fatura. A intenção `Pagamento Fatura` só roda para frases sem essas palavras.

## Comportamento proposto (.NET)
Decidir (produto): detectar `fatura` no texto de pagamento e seguir para `Pagamento Fatura`, ou trocar o exemplo do menu.

## Impacto nos clientes
Pagar fatura pelo WhatsApp passa a se comportar como o menu promete.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Decidir com o Rafael (mudança de produto).

## Decisão do Rafael
Adiado para depois da virada (2026-10-09); continua `proposto`, não implementado.
