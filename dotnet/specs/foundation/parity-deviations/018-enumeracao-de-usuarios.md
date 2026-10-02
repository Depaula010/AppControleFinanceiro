# Desvio 018 — Login distingue usuário inexistente de senha errada

- **Estado:** aprovado
- **Features afetadas:** 001
- **Severidade:** segurança

## Comportamento atual (Flask)
`/auth/login` devolve 404 "WhatsApp não cadastrado" ou 401 "Senha incorreta".

## Comportamento proposto (.NET)
Manter (o Angular pode usar a mensagem).

## Impacto nos clientes
—

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Rejeitar por ora (paridade); reavaliar após a virada.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Nota:** a proposta é manter o comportamento atual (paridade); não há mudança.
