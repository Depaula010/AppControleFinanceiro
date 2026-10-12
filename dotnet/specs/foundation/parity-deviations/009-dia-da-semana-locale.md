# Desvio 009 — Dia da semana depende do lc_time do banco

- **Estado:** aprovado
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`gastos_dia_semana.dia` vem de `TO_CHAR(..., 'Day')` (`analytics_service.py:81`), no idioma de `lc_time` do servidor. **Confirmado em produção:** `lc_time = C` e `TimeZone = UTC`, então os nomes saem em inglês e com padding (`Monday   `). O Angular só declara o campo no model (`dashboard.model.ts:28`) e não o exibe.

## Comportamento proposto (.NET)
Agrupar por `EXTRACT(DOW ...)` e formatar o nome no código, em português fixo, reproduzindo a grafia atual (inglês, sem remover o padding) enquanto o Angular não usar o campo; ao usar, a spec 006 decide se passa para português.

## Impacto nos clientes
Nenhuma, se a grafia for igual à atual.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar: manter a grafia atual na saída e calcular no código (independe do banco).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
