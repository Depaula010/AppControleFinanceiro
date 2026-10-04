# Desvio 078 — Validar e normalizar IP em blacklist e unblock

- **Estado:** implementado (feature 009a, 2026-10-04); aprovado na onda 2 em 2026-10-04
- **Features afetadas:** 009
- **Severidade:** segurança

## Comportamento atual (Flask)
Aceita texto arbitrário como IP.

## Comportamento proposto (.NET)
IPAddress.TryParse; 400 IP inválido; IP normalizado.

## Impacto nos clientes
Nenhum cliente legítimo afetado.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
