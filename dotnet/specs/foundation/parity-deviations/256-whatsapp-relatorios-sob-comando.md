# Desvio 256 — Relatórios sob comando no WhatsApp (fatura por categoria, gastos do mês por categoria, relatório do mês)

- **Estado:** implementado (PR #45)
- **Features afetadas:** 012
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
O Flask só gera o relatório mensal agendado; "qual valor da fatura?" responde o total e não há ranking por categoria no WhatsApp. O menu de ajuda não cita esses comandos.

## Comportamento proposto (.NET)
Passo determinístico antes do Gemini (o prompt de intenção do Flask não muda): `fatura do inter por categoria`, `gastos do mês por categoria` e `relatório do mês` (dispara o relatório mensal existente). Frases de configuração, consulta ou análise seguem o pipeline. O **menu de ajuda ganha 3 linhas**, o que altera o texto do Flask: 21 trechos em 5 fixtures de paridade atualizados.

## Impacto nos clientes
Texto do "Menu de Ajuda" muda (3 linhas novas). Comandos novos; nada existente muda de resposta.

## Alternativas
Mudar o prompt de intenção (quebraria a paridade com o Flask). Não listar no menu (os comandos não seriam descobertos).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido pelo Rafael na conversa de 23/10/2026 ("Fatura detalhada por categoria, Gastos do mês por categoria, Relatório mensal sob demanda"). Implementado no PR #45 (spec 012, AC-012-507).
