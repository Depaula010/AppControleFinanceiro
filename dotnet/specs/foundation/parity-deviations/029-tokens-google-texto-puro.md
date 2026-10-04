# Desvio 029 — Tokens do Google guardados em texto puro

- **Estado:** implementado (feature 010, 2026-10-02)
- **Features afetadas:** 010, 014
- **Severidade:** segurança

## Comportamento atual (Flask)
**Confirmado em produção:** `googlecalendartokens.access_token` e `refresh_token` não têm formato Fernet. O serviço não cifra (`google_calendar_oauth_service.py:122`), mas `clear_credentials.py` e o modelo ORM sugerem criptografia que nunca existiu.

## Comportamento proposto (.NET)
Paridade: ler e gravar em texto puro. O refresh token dá acesso ao Calendar e ao Drive do usuário. Cifrar com Fernet exige migrar o registro existente; a leitura aceitaria os dois formatos (mesma regra do desvio 017).

## Impacto nos clientes
Nenhum, se mantido. Cifrar depois da virada não exige reconexão do usuário.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Manter em texto puro na virada e cifrar numa tarefa posterior, para não misturar risco com a troca de stack.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
