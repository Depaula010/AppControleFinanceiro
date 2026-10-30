# Desvio 267 — Contas fixas do cartão aparecem nos avisos, só como informação, mesmo depois de lançadas

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
- **Alertas da manhã** (`ALERTAS FINANCEIROS`): a conta fixa do cartão sai junto das demais em "VENCE HOJE/AMANHÃ (Despesas)", como se fosse para pagar. E só sai se ainda **não** foi lançada no mês.
- **Check-in noturno:** a do cartão sai como "DÉBITO CARTÃO DE CRÉDITO … Será debitado hoje" (informativa), mas só se ainda estiver pendente.
- O motor de agendamentos (00:05) lança a conta do cartão na fatura no dia do vencimento. Daí em diante ela sai das duas consultas: **no próprio dia da cobrança o aviso some**, justamente quando deveria informar.

## Comportamento proposto (.NET)
- **Manhã:** a conta fixa do cartão vai para um bloco próprio, **"💳 NA FATURA DO CARTÃO (só informativo)"** (`• Hoje: …`, `• Amanhã: …`), fora das seções de "vence", e **aparece mesmo se o motor já a lançou na fatura**. As contas de outras contas continuam saindo só enquanto não lançadas.
- **Noite:** as contas fixas do cartão (FIXO e LEMBRETE_VARIAVEL, mensais ou anuais no mês, despesas) cobradas **hoje** saem no bloco "DÉBITO CARTÃO DE CRÉDITO … Será debitado hoje", lançadas ou não. Nunca recebem número nem entram na pergunta do "ok": o cartão cobra sozinho.
- Um check-in só com contas do cartão é enviado (informativo, sem "COMO RESPONDER").
- O lembrete de véspera (`BILLS-NOTIF`) já listava o cartão como informativo e não muda.

## Impacto nos clientes
Nenhuma rota muda. Mudam os textos da manhã (a conta do cartão deixa de parecer "a pagar") e da noite (a cobrança do cartão aparece no dia, em vez de sumir depois do lançamento automático).

## Alternativas
Manter como está (o aviso some no dia da cobrança). Mostrar também as do cartão dos próximos dias no check-in noturno.

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 30/10/2026 ("as contas fixas mensais … cadastradas no cartão … devem aparecer nesses dois check-ins, só de forma informativa").
