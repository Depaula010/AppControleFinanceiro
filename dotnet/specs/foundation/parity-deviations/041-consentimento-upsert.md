# Desvio 041 — LGPD: consentimento pode ser atualizado e revogado

- **Estado:** aprovado
- **Features afetadas:** 008
- **Severidade:** correção

## Comportamento atual (Flask)
`INSERT` simples (`servico_consentimento_lgpd.py:71-90`) com UNIQUE `(usuario, tipo, versão)`: o 2º registro do mesmo tipo e versão dá 500, apesar do docstring "registra ou atualiza"; `revogado_em` nunca é gravado.

## Comportamento proposto (.NET)
`ON CONFLICT (usuario_id, tipo_consentimento, versao_consentimento) DO UPDATE SET consentimento_dado = EXCLUDED.consentimento_dado, data_consentimento = now(), revogado_em = CASE WHEN EXCLUDED.consentimento_dado THEN NULL ELSE now() END`, mantendo 201.

## Impacto nos clientes
Revogação passa a funcionar; o histórico é uma linha por versão.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
