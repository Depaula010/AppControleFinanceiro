# Desvio 017 — api_key_automate em texto puro e coluna pequena demais para Fernet

- **Estado:** implementado (feature 001, 2026-10-02)
- **Features afetadas:** 001, 011
- **Severidade:** segurança

## Comportamento atual (Flask)
O `/auth/register` tenta cifrar a `api_key_automate` com Fernet e, se falhar, grava em texto puro (`auth.py:147-152`). **Confirmado em produção:** o único usuário tem a chave em texto puro (40 caracteres, não começa com `gAAAAA`). A coluna é `varchar(100)`, e um token Fernet de uma chave de 43 caracteres tem cerca de 140: não cabe. Ou seja, a cifra nunca chegou a funcionar no banco. A busca por chave (`get_user_by_api_key`) carrega todos os usuários e decifra um a um.

## Comportamento proposto (.NET)
Migration aditiva alarga `api_key_automate` para `varchar(255)`; novos cadastros gravam a chave cifrada em Fernet; a leitura aceita texto puro legado e cifrado. A chave do usuário existente **não é alterada** (trocar quebraria o atalho do iPhone dele); ela pode ser cifrada num passo posterior com o mesmo valor. Se a cifra falhar no cadastro, o cadastro falha em vez de gravar em claro.

## Impacto nos clientes
Nenhum: a chave em texto puro do usuário atual continua válida.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar. Decidir depois da virada se vale cifrar a chave existente.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
