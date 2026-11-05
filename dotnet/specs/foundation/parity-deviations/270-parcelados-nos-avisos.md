# Desvio 270 — Contas parceladas entram nos avisos e no "paguei"

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 011, 012, 013
- **Severidade:** correção

## Comportamento atual (Flask)
Vários comandos e avisos filtram `tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')`, então um parcelado nunca aparece neles:
- contas fixas pendentes e vence hoje/amanhã/essa semana;
- alertas do resumo da manhã e lembrete de contas;
- previsão de gastos (contas que faltam no mês);
- bloco do cartão no check-in.

Também não dá para quitar um parcelado: "paguei a parcela do X" e a baixa por SMS só casam com fixas e variáveis (AC-011-061). Além disso, a quitação lança sem `agendamento_id` e não avança o contador.

## Comportamento proposto (.NET)
- Os filtros passam a incluir `PARCELADO` **com parcela restante** (`SchedulePaymentSql.ListedKind`). As listas que conferem "já pago no mês" pela descrição passam a aceitar também a transação ligada ao parcelado, cujo nome termina em `(n/N)`.
- No cartão, o parcelado entra no bloco informativo, como as fixas do cartão (desvio 267).
- "Paguei" e baixa por SMS de um parcelado vão pelo **mesmo executor do check-in** (`BillSettlement`). Assim a transação fica ligada ao agendamento, com competência e nome `X (n/N)`, o contador avança e o agendamento é desativado na última parcela. A competência já paga no mês não é lançada de novo.
- O detalhe da reserva de emergência continua sem parcelados (exclusão de propósito).

## Impacto nos clientes
Nenhuma rota muda. O AC-011-061 deixa de valer para `PARCELADO` com parcela restante, e a resposta do SMS traz o nome com a parcela.

## Decisão do Rafael
Pedido na conversa de 05/11/2026: "todo comando via bot que exibir os pagamentos parcelados devem mostrar dessa forma". Ele escolheu incluir os parcelados nos comandos que os escondiam.
