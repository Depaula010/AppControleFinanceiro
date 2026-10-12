# Desvio 035 — Contas: validar cor_hex e icone

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
`cor_hex` e `icone` aceitam qualquer valor; mais de 7 / 50 caracteres dão erro do banco (500); não há checagem de formato `#RRGGBB` (`api.py:382-383, 564-571`).

## Comportamento proposto (.NET)
`cor_hex` `null` ou `^#[0-9A-Fa-f]{6}$`; `icone` `null` ou até 50 caracteres; senão 400 (`"Cor inválida. Use o formato #RRGGBB"`, `"Ícone inválido"`).

## Impacto nos clientes
400 no lugar de 500 no tamanho; recusa cores fora do padrão que hoje seriam gravadas (o modal do Angular só envia 10 cores fixas válidas).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar (comprimento e formato).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
