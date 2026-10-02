# Desvio 060 — OAuth: escapar o nome do usuário na página de sucesso

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** segurança

## Comportamento atual (Flask)
`{nome_usuario}` entra cru no HTML (`calendar_handler.py:268`); o nome é digitado pelo próprio usuário no cadastro.

## Comportamento proposto (.NET)
Codificar para HTML (`&`, `<`, `>`, `"`).

## Impacto nos clientes
Nomes com esses caracteres mudam no HTML; o resto é idêntico.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
