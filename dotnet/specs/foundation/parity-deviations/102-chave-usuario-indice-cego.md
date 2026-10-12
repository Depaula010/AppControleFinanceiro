# Desvio 102 — Chave do atalho: índice cego e cabeçalho

- **Estado:** proposto
- **Features afetadas:** 011
- **Severidade:** segurança

## Comportamento atual (Flask)
`get_user_by_api_key` carrega **todos** os usuários com chave e decifra um a um (O(n) por requisição; `finance_service.py:251-288`), e a comparação é `==`. `GET /api/agendamentos/reserva` leva a chave na query string (vai para log de proxy e histórico do navegador).

## Comportamento proposto (.NET)
Coluna aditiva `api_key_automate_hash` (HMAC-SHA256 da chave com a `ENCRYPTION_KEY`) com UNIQUE, preenchida no cadastro e por migration para as existentes; busca direta pelo hash. Aceitar também o cabeçalho `X-User-Api-Key` no `GET` (mantendo `api_key` na query).

## Impacto nos clientes
Nenhum cliente muda; o atalho do usuário atual continua válido (a chave não é trocada).

## Alternativas
Manter como está (paridade estrita): hoje há 1 usuário e o custo é irrelevante; a comparação em tempo constante já entra na 011 sem mudar contrato.

## Recomendação
Decidir depois da virada.

## Decisão do Rafael
Adiado para depois da virada (2026-10-09); continua `proposto`, não implementado.
