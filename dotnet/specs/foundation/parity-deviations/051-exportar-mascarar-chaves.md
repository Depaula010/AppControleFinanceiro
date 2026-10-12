# Desvio 051 — LGPD: mascarar chaves no exportar

- **Estado:** rejeitado
- **Features afetadas:** 008
- **Severidade:** segurança

## Comportamento atual (Flask)
`GET /api-keys/lgpd/exportar/<id>` devolve as chaves de API decifradas.

## Comportamento proposto (.NET)
Permitir `?mascarar=true` (padrão falso) e registrar a exportação.

## Impacto nos clientes
Nenhum cliente conhecido usa o export.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Rejeitar por ora: manter até haver cliente real; o risco real é o segredo único (decisão G-6).

## Decisão do Rafael
Rejeitado em 2026-10-02 ("aprovo as recomendações"): o .NET mantém o comportamento do Flask.
