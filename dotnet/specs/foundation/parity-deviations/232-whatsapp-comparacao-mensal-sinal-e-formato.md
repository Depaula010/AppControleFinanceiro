# Desvio 232 — Comparação Mensal: totais negativos, variação invertida e números em formato inglês

- **Estado:** implementado (feature 012, 2026-10-10)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`get_monthly_comparison` soma `valor` das despesas (negativo no banco) e responde `R$ -2,555.50`; como os totais são negativos, `📈`/`🔴` aparecem quando o gasto **diminui** e `📉`/`🟢` quando aumenta, e a variação sai com o sinal trocado (gastar mais mostra `-10.1%`). Os valores usam `{:,.2f}` (vírgula de milhar, ponto decimal) em vez de `R$ 2.555,50`. O desempate de categorias com a mesma variação depende da ordem de um `set` (muda a cada processo). Confirmado pelo harness (`comparacao_completa`).

## Comportamento proposto (.NET)
Usar o valor absoluto das despesas, `formatar_moeda` (`R$ 2.555,50`), variação positiva quando o gasto aumenta e desempate pelo nome da categoria.

## Impacto nos clientes
A mensagem passa a ser coerente com o que o usuário espera (gastou mais ⇒ variação positiva, vermelho).

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar junto com o 158 (que trata os mesmos defeitos nos prompts de análise e previsão).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
