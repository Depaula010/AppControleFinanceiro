# Desvio 277 — Populador lança a conta do cartão na data do ciclo da fatura

- **Estado:** implementado
- **Features afetadas:** 011, 013
- **Severidade:** correção

## Comportamento atual (Flask)
`process_invoice_schedules` datava o lançamento com `date(ano, mês, dia_execucao)` do mês do **vencimento** da fatura. No Inter (fecha 13, vence 20) uma conta fixa de dia 15 entrava na fatura de 20/11 com data 15/11, que pela regra do desvio 065 é da fatura de 20/12: a fatura saía com a cobrança a mais e o lançamento ficava datado depois do fechamento (mês errado nos relatórios e no dashboard). Num parcelado a última parcela caía uma fatura antes do banco.

## Comportamento proposto (.NET)
A data é calculada pelo ciclo (`InvoiceCycle`): se a data do mês do vencimento já pertence a outra fatura, usa a do mês anterior (dia 15 na fatura de 20/11 vira 15/10; dia 10 continua 10/11). Dia que não cai em nenhum dos dois meses mantém o comportamento antigo. Cartão sem dia de fechamento ou vencimento segue como antes.

## Limitação conhecida
A consulta dos agendamentos ainda não respeita `data_inicio` futura (parcelado com início depois da fatura aberta): fica para outro desvio.

## Impacto nos clientes
Nenhuma rota muda. Faturas passam a mostrar contas fixas com a data do ciclo certo.

## Decisão do Rafael
Pedido na conversa de 16/11/2026 ("pode corrigir todos os problemas encontrados"), achado A2 da auditoria.
