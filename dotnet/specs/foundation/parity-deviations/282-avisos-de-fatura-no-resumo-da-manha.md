# Desvio 282 — Avisos de fatura saem no resumo da manhã e respeitam os alertas financeiros

- **Estado:** implementado
- **Features afetadas:** 013
- **Severidade:** correção (achado M13 da auditoria 2026-11-16)

## Comportamento atual (Flask / até o desvio 281)
O processador de faturas das 00:01 fechava as faturas vencidas **e** mandava na hora "FATURA FECHADA" e, para faturas fechadas que vencem em 3 dias, "VENCIMENTO PRÓXIMO". Os dois avisos ignoravam `alertas_financeiros_ativos`, saíam de madrugada e se repetiam no resumo da manhã (bloco de faturas de hoje) e no check-in da noite. Um aviso de fechamento que falhava no envio nunca era reenviado (só o fechamento das 00:01 o tentava).

## Comportamento proposto (.NET)
- O job das 00:01 continua **fechando** as faturas, garantindo a fatura corrente e populando as abertas, mas **não manda mensagem**.
- O resumo da manhã (`DailyBriefingJob`, na hora configurada do usuário) envia os dois avisos **só para quem tem `alertas_financeiros_ativos` ligado**, depois da mensagem principal:
  - **FATURA FECHADA:** faturas `Fechada` com fechamento nos últimos 7 dias ainda não avisadas. Se o envio falhar, a manhã seguinte tenta de novo (dentro dos 7 dias), o que corrige o "não reenvia".
  - **VENCIMENTO PRÓXIMO:** faturas fechadas que vencem em 3 dias.
- **Sem duplicar:** fatura que vence hoje já aparece no bloco de alertas do resumo, então não ganha o aviso de fechamento (a chave de "já avisada" é gravada mesmo assim).
- A dedupe é a mesma de antes (`CacheKeys.InvoiceClosed` e `InvoiceDueWarning3d`, 7 dias, gravadas só quando o envio dá certo).
- Texto das mensagens inalterado (o formato do dinheiro é tratado no desvio 285).

## Impacto nos clientes
Nenhuma rota muda. Quem tem os alertas desligados deixa de receber os avisos de fatura; quem tem ligados passa a recebê-los de manhã em vez de 00:01. O aviso de fechamento pode chegar até 1 dia depois do fechamento.

## Decisão do Rafael
"faça todos" na conversa de 16/11/2026.
