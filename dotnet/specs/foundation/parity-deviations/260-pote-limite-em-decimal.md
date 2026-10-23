# Desvio 260 — Limite do pote decidido em decimal

- **Estado:** implementado (PR #49)
- **Features afetadas:** 011
- **Severidade:** correção

## Comportamento atual (Flask)
`validate_budget` compara em `float`: gasto + valor que fecha exatamente o limite (R$ 0,14 + R$ 0,10 contra R$ 0,24) dá "Esta despesa ultrapassará seu limite!" em cerca de 10% desses casos, com "Excesso: R$ 0,00".

## Comportamento proposto (.NET)
A decisão (`Exceeds`) é em `decimal` (`MoneyDecimal.TryFromDouble`); os valores exibidos seguem em `double` (paridade de formatação). Valor fora de `numeric(15,2)` mantém a comparação antiga.

## Impacto nos clientes
Só o caso de fechar exatamente o limite deixa de avisar. Nenhum cenário do Flask mudou.

## Alternativas
Migrar todos os ~130 `double` de dinheiro para `decimal` (rejeitado: estão em formatação e JSON que reproduzem o Flask; sem ganho de exatidão).

## Recomendação
Aprovar.

## Decisão do Rafael
Item 5 pedido pelo Rafael na conversa de 23/10/2026; a auditoria restringiu o escopo ao ponto de decisão. Implementado no PR #49 (spec 011 §7.6; regra 10 do CLAUDE.md).
