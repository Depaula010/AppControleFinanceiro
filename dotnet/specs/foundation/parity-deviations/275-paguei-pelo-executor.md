# Desvio 275 — "Paguei X" e SMS de conta fixa ou variável passam pelo executor de agendamentos

- **Estado:** implementado
- **Features afetadas:** 011, 012, 013
- **Severidade:** correção

## Comportamento atual
Só o parcelado (desvio 270) passa pelo executor. Fixa e lembrete variável viravam despesa avulsa, sem `agendamento_id` nem `competencia`. Efeitos: conta semanal, quinzenal ou diária paga por "paguei" nunca saía do check-in (e, confirmada lá também, entrava duas vezes, porque o índice do desvio 258 só vale com agendamento e competência); conta paga com atraso "quitava" o vencimento errado.

## Comportamento proposto (.NET)
Toda baixa pelo "paguei" ou pelo SMS usa o executor do check-in: transação ligada ao agendamento, com competência. Mensal/anual: o vencimento do mês do pagamento. Cadência contínua (semanal, quinzenal, diária): o próprio dia. Se aquele vencimento já foi lançado, a baixa recusa ("conta já lançada neste vencimento") em vez de duplicar.

## Impacto nos clientes
Nenhuma rota muda. Pagar duas vezes a mesma conta no mesmo vencimento passa a ser recusado.

## Decisão do Rafael
Pedido na conversa de 16/11/2026 ("pode corrigir todos os problemas encontrados"), achado A4 da auditoria.
