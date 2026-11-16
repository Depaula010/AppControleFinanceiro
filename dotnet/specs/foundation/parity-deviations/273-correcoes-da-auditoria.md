# Desvio 273 — Correções da auditoria de 2026-11-16 (bugs sem mudança de regra de negócio)

- **Estado:** implementado
- **Features afetadas:** 005, 006, 011, 012, 013
- **Severidade:** correção

Auditoria feita por três agentes (QA, Produto, Financeiro) a pedido do Rafael em 2026-11-16, que mandou corrigir tudo o que foi achado.

## Correções
1. **Relatório de fatura no WhatsApp (`ReportsReader`)**: as quatro consultas deixam de contar o "Pagamento Fatura" (que grava as duas pernas com `fatura_id`) como compra, estorno, categoria "Quitação de Empréstimos" ou maior compra.
2. **Relatório mensal (`MonthlyReportRepository`)**: o JOIN dos potes passa a filtrar `t.usuario_id = @uid` (subcategorias globais somavam gasto de outros usuários).
3. **"Ajustar saldo inicial"**: o nome da conta é retirado da mensagem antes de procurar o número ("C6 1500" lia 6), o número usa o leitor do check-in ("5000.50" = 5.000,50; "12.50" = 12,50; antes virava 1.250) e aceita sinal de menos. No Flask "12.50" valia 1.250 (cenário `ajuste_valor_decimal_ponto` registrado como divergência conhecida).
4. **Migration `0130`**: a 0090 terminava sem erro quando havia duplicata e ficava registrada sem criar o índice único do desvio 258. A 0130 repete a tentativa, idempotente, sem derrubar o deploy.
5. **Resposta à pendência no WhatsApp (`TransactionReplyStep`)**: trava atômica no Redis (`SET NX`, 30 s) por número e pendência. Dois "OK" simultâneos não lançam duas vezes; o segundo recebe "Já estou processando sua confirmação".

## Impacto nos clientes
Nenhuma rota muda. Mensagens novas: apenas a do item 5.

## Decisão do Rafael
Pedido na conversa de 16/11/2026 ("pode corrigir todos os problemas encontrados").
