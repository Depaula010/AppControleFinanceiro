# Desvio 268 — Fim do aviso avulso "LEMBRETE DE CONTA VARIÁVEL"; alerta da manhã cobre contas semanais

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
- O motor de agendamentos (00:05) manda, `notificar_antes_dias` antes do vencimento, uma mensagem avulsa "🔔 LEMBRETE DE CONTA VARIÁVEL 🔔 … me diga o valor exato deste mês". É um terceiro aviso, além do resumo da manhã e do check-in noturno, e repete o que eles já mostram.
- O alerta da manhã (`ALERTAS FINANCEIROS`) acha as contas pelo `dia_execucao`. Conta de cadência contínua (semanal, quinzenal, diária) vence pelo próximo vencimento, não pelo dia do mês, então **não aparecia** de manhã.

## Comportamento proposto (.NET)
- O motor **não envia mais** o lembrete avulso de `LEMBRETE_VARIAVEL` (e não o lança, como já era). A conta continua em dois lugares: o alerta da manhã (hoje e amanhã) e o check-in noturno (do vencimento em diante, com a pergunta do valor real, desvio 252).
- O alerta da manhã passa a incluir as contas de cadência contínua cujo **próximo vencimento** é hoje ou amanhã.

## Impacto nos clientes
Nenhuma rota muda. Some a mensagem "LEMBRETE DE CONTA VARIÁVEL". Contas semanais/quinzenais passam a sair no resumo da manhã.

## Alternativas
Manter o aviso avulso, com opção de desligar por usuário. Mais código para um aviso que o usuário não quer.

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 01/11/2026 ("eu não quero esse lembrete"; ele deve aparecer só nos check-ins matinal e noturno).
