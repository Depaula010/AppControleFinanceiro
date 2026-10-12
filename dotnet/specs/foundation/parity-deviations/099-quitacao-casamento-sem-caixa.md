# Desvio 099 — Quitação por SMS: casar descrições sem diferenciar a caixa

- **Estado:** implementado (feature 011, 2026-10-09)
- **Features afetadas:** 011, 012
- **Severidade:** correção

## Comportamento atual (Flask)
`find_matching_bill` (`fixed_bills_service.py:80-145`) passa o texto em minúsculas, mas as descrições guardadas como estão. O `rapidfuzz` 3.14.3 (`requirements.txt:10`) não pré-processa e é sensível à caixa: `luz` × `Luz` = 66,7, `agua` × `Água` = 75 (corte 65), `net` × `Net` = 66,7. Descrições capitalizadas e curtas quase não casam e a despesa cai como avulsa.

## Comportamento proposto (.NET)
Pré-processar os dois lados (minúsculas e sem acentos) antes do `WRatio`.

## Impacto nos clientes
Mais SMS passam a quitar a conta fixa em vez de gerar uma despesa avulsa duplicada com o agendamento.

## Alternativas
Manter como está (paridade estrita), que é o que a feature 011 implementa até a decisão.

## Recomendação
Aprovar (a 012, que compartilha o casamento no "Quitar Conta Fixa", ganha o mesmo comportamento).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
