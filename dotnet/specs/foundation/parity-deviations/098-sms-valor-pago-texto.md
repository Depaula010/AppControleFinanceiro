# Desvio 098 — SMS: valor_pago como texto numérico deve funcionar

- **Estado:** implementado (feature 011, 2026-10-09)
- **Features afetadas:** 011
- **Severidade:** correção

## Comportamento atual (Flask)
`float(valor_pago)` aceita `"150.50"` e a transação é gravada (`commit`), mas `formatar_moeda(valor_pago)` recebe o texto e falha (`ValueError` no fallback manual), o que devolve 500 sem avisar o WhatsApp, com o pagamento já gravado. O usuário vê erro e tenta de novo (duplica). O .NET reproduz (500 fixo depois de gravar, sem aviso).

## Comportamento proposto (.NET)
Converter o valor uma vez e usar o número em todo o fluxo: 200 e aviso normais.

## Impacto nos clientes
O atalho que envia o valor como texto passa a receber 200.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar (confirmar antes, Q-011-2, se o atalho envia texto).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
