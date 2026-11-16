# Desvio 279 — Estorno abate o total da fatura

- **Estado:** implementado
- **Features afetadas:** 004, 006, 012, 013
- **Severidade:** correção (achado M2 da auditoria 2026-11-16)

## Comportamento atual (Flask / até o desvio 278)
Só a despesa em cartão era ligada à fatura (`fatura_id`); a receita no cartão (estorno) ficava sem fatura. Todos os totais somavam só os valores negativos (`SUM(ABS(valor)) WHERE valor < 0`). Caso: compras de 500 e estorno de +100 mostravam fatura de 500 e o check-in pagava 500 em vez de 400.

## Comportamento proposto (.NET)
- **Lançamento:** receita (`Renda`) em conta de cartão com os dois dias configurados passa a ligar à fatura do ciclo da data (`InvoiceLinking`, mesma regra da despesa, desvio 065). Vale para `POST /api/transactions`, bot, atalho e para o `PUT` que muda conta, data ou tipo (relink).
- **Total da fatura** (avisos da manhã/noite, check-in, vencimento hoje/atrasadas, "minha fatura" e rodapé do bot): líquido com sinal `GREATEST(-SUM(valor), 0)` sobre `Despesa` e `Renda` ligadas à fatura. O `Pagamento Fatura` (que grava as duas pernas com `fatura_id`) fica de fora; fatura com estorno maior que as compras vale 0, nunca negativo.
- **"Minha fatura" detalhada:** lista também o estorno, como linha de valor negativo.
- O relatório de fatura (`ReportsReader`) já separava compras e estornos (desvio 273) e passa a enxergá-los.

## Limitação conhecida
Receitas em cartão lançadas antes deste desvio continuam sem `fatura_id` (não há migration de backfill: o vínculo exato depende do ciclo de cada cartão). Só valem para o total as novas, ou as que o usuário editar.

## Impacto nos clientes
Nenhuma rota, campo ou envelope muda. `fatura_id`/`InvoiceId` de uma receita em cartão deixa de ser nulo. Valores de fatura com estorno passam a ser menores.

## Decisão do Rafael
"faça todos" na conversa de 16/11/2026.
