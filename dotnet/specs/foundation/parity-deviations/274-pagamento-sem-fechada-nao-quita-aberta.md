# Desvio 274 — Pagamento sem fatura Fechada não marca a fatura aberta como Paga

- **Estado:** implementado
- **Features afetadas:** 004, 013
- **Severidade:** correção

## Comportamento atual
O pagamento de fatura (desvio 010) quita a fatura Fechada mais antiga; sem ela, usa a fatura do ciclo da data e a marca como `Paga` (R5, sem comparar valor). Num segundo pagamento parcial (600 em 18/11, 400 em 20/11) não há mais Fechada, o pagamento cai na fatura do ciclo (20/12, `Aberta`) e a marca como `Paga`. Ela some do fechamento, dos avisos de vencimento, do check-in e do "minha fatura".

## Comportamento proposto (.NET)
Só a fatura **Fechada** vira `Paga`. Sem Fechada, o pagamento é ligado à fatura do ciclo (criada se faltar) e o status dela não muda.

## Impacto nos clientes
Nenhuma rota muda. A resposta do pagamento continua trazendo `InvoiceId` e o vencimento.

## Decisão do Rafael
Pedido na conversa de 16/11/2026 ("pode corrigir todos os problemas encontrados"), achado A3 da auditoria.
