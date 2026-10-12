# Desvio 083 — x-api-key errada em /admin conta como tentativa suspeita

- **Estado:** implementado (feature 009a, 2026-10-04); aprovado na onda 2 em 2026-10-04
- **Features afetadas:** 009
- **Severidade:** segurança

## Comportamento atual (Flask)
Chave errada não conta.

## Comportamento proposto (.NET)
5 tentativas em 10 min bloqueiam o IP por 1 h.

## Impacto nos clientes
Aviso: desbloqueio de emergência com redis-cli DEL security:blocked:<ip>.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
