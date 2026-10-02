# Desvio 016 — Nome de conta duplicado devolve 500

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
A API checa unicidade só entre contas ativas; a constraint UNIQUE(usuario_id, nome_conta) vale para todas. Criar conta com o nome de uma inativa ⇒ erro do banco ⇒ 500.

## Comportamento proposto (.NET)
Checar todas as contas; se o nome existir em conta inativa, 400 com a mensagem de duplicidade (ou reativar — decidir na spec 002).

## Impacto nos clientes
Mensagem 400 em vez de 500.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar 400.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
