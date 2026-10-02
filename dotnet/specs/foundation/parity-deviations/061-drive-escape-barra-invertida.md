# Desvio 061 — Drive: escapar a barra invertida na busca de pasta

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** correção

## Comportamento atual (Flask)
`google_drive_service.py:120` escapa `'` mas não `\`; um nome com `\` quebra ou altera a consulta (afeta só o Drive do próprio usuário).

## Comportamento proposto (.NET)
Escapar `\` antes de `'`.

## Impacto nos clientes
Nomes de pasta com `\` passam a funcionar.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
