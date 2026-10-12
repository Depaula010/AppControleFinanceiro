# Desvio 023 — Rate limit de 100/hora por IP do proxy

- **Estado:** aprovado
- **Features afetadas:** 009
- **Severidade:** operação

## Comportamento atual (Flask)
Sem `ProxyFix`, `remote_addr` é o IP do container nginx; o limite de 100/hora vira um balde compartilhado por todos os clientes. Não há `@limiter.limit` em rota alguma (configuração morta). O 429 sai em HTML.

## Comportamento proposto (.NET)
`ForwardedHeaders` configurado para a rede do nginx; rate limit por IP real; resposta 429 em JSON (envelope D). O nginx já aplica 20/50/5 r/s.

## Impacto nos clientes
Clientes legítimos deixam de compartilhar o balde.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar, confirmando os limites com o Rafael.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** limites confirmados com o Rafael na spec 009.
