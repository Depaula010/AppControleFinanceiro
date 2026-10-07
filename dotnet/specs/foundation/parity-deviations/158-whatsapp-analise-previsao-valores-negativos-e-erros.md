# Desvio 158 — Análise e previsão: valores negativos, desempate aleatório e KeyError sem histórico

- **Estado:** proposto
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
(1) `analytics_service.py` soma `valor` de despesas (negativo) e escreve `R$ -1,234.50` no prompt do Gemini ("Total gasto este mês: R$ -...", variação, categorias, potes), o que confunde o modelo. (2) `get_monthly_comparison` ordena variações com empates decididos pela iteração de um `set` de texto (muda a cada processo por `PYTHONHASHSEED`). (3) `calculate_simple_forecast` devolve um dicionário sem as chaves `gasto_ate_hoje`, `taxa_diaria_atual`, `despesas_normais_pendentes` quando o usuário não tem gasto no histórico: o f-string do prompt dá `KeyError` ⇒ `❌ Ops!` (usuário novo nunca recebe previsão). (4) Os textos de moeda usam `{:,.2f}` (vírgula de milhar e ponto decimal) em vez de `R$ 1.234,50`.

## Comportamento proposto (.NET)
Usar valores absolutos nos prompts, desempate por nome, devolver zeros quando não há histórico e formatar com `formatar_moeda`.

## Impacto nos clientes
Análises e previsões mais coerentes; usuário novo passa a receber a previsão.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa (o desempate usa a ordem de aparição).

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
