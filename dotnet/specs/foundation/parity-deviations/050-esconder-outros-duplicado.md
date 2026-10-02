# Desvio 050 — Categorias: esconder o Outros global duplicado na API

- **Estado:** rejeitado
- **Features afetadas:** 003
- **Severidade:** correção

## Comportamento atual (Flask)
Com a duplicata do desvio 030, `Despesas Gerais` mostra dois `Outros` com ids diferentes (confirmado em produção: macro 17, 2 linhas).

## Comportamento proposto (.NET)
A listagem esconderia o de maior `id` quando há duas subcategorias globais com o mesmo nome na mesma macro.

## Impacto nos clientes
O dropdown de despesa perderia uma opção repetida.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Rejeitar: é melhor resolver no dado depois (Rafael escolhe qual manter e reaponta as transações), e não esconder na API. Até lá o dropdown mostra dois `Outros`, como hoje.

## Decisão do Rafael
Rejeitado em 2026-10-02 ("aprovo as recomendações"): o .NET mantém o comportamento do Flask.
