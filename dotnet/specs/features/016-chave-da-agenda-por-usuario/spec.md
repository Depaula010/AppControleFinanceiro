# Feature 016 — Chave da agenda por usuário (para o Coach)

**Status:** aprovada pelo Rafael e implementada (2026-10-26); aguardando a decisão de merge/deploy. **Emenda a 015 (parte B).** Pedido do Rafael: "fazer de um jeito que não precise mexer no código futuramente" para outros usuários usarem a agenda no Coach.

## 1. Problema
A rota `GET /internal/coach/agenda` (015 B) aceita **uma chave só**, do ambiente (`COACH_AGENDA_API_KEY`), e lê a agenda de **um usuário fixo** (`COACH_AGENDA_USER_ID`). Outra pessoa só poderia usar com mudança de código e de secrets.

## 2. Solução
Cada pessoa tem a **própria chave da agenda**, guardada no banco só como **hash**. A rota descobre o dono pela chave recebida. Nenhum secret novo por usuário, nenhum deploy para incluir uma pessoa.

## 3. Regras
- **RN-016-01 Tabela `coach_agenda_chaves`** (migration aditiva): `id`, `usuario_id` (FK `usuarios`), `chave_hash` (SHA-256 em hex, único), `final` (4 últimos caracteres, para a pessoa reconhecer), `criada_em`, `ultimo_uso_em`, `revogada_em` (nula = ativa). Nunca a chave em texto.
- **RN-016-02 A chave** tem 32 bytes aleatórios (hex), **só de leitura da agenda**: não é a `api_key_automate` do atalho, que também lança transações.
- **RN-016-03 Gerar.** Rota autenticada pelo login normal do app (`ICurrentUser`): `POST /coach/agenda/chave` devolve a chave **uma única vez** (e o `final`). Gerar de novo **revoga a anterior** (uma ativa por pessoa). `DELETE /coach/agenda/chave` revoga. `GET /coach/agenda/chave` diz só se existe, o `final` e as datas, nunca a chave.
- **RN-016-04 A rota da agenda** valida a chave comparando o hash em tempo constante, resolve o `usuario_id` e lê a agenda **daquele usuário** (a conexão do Google e os níveis dele). Chave desconhecida ou revogada: `401`. Atualiza `ultimo_uso_em`.
- **RN-016-05 Sempre registrada.** A rota deixa de depender de `COACH_AGENDA_API_KEY`/`COACH_AGENDA_USER_ID`. Sem nenhuma chave ativa, qualquer chamada devolve `401` (não `404`).
- **RN-016-06 Transição.** Se `COACH_AGENDA_API_KEY` e `COACH_AGENDA_USER_ID` existirem, continuam valendo como uma chave extra daquele usuário (nada quebra no dia do deploy). Depois que o Rafael gerar a própria chave, os dois secrets podem ser apagados.
- **RN-016-07 Níveis por usuário.** Hoje os níveis (`coach_agenda.nivel_padrao`, `coach_agenda.nivel.<calendário>`, `coach_agenda.mascarar`) são globais. Nesta feature **continuam globais** (o padrão sem SQL é `completo`); níveis por pessoa ficam para quando houver segunda pessoa de verdade. Anotado, não implementado.
- **RN-016-08 Privacidade.** Nenhum log leva a chave, nem a recusada; só contagens e o motivo. Limite de tentativas inválidas por IP como nas outras rotas internas.

## 4. Como a pessoa pega a chave (a decidir)
O Rafael não lembra como usa o Meu Secretário, então a **tela/comando que chama `POST /coach/agenda/chave`** fica para depois. A rota existe e funciona com o login normal; até haver tela, quem chama é o próprio Rafael (passo a passo no `docs/` quando for implementar).

## 5. Critérios de aceite (todos com teste)
AC-016-01 o banco só tem o hash; AC-016-02 gerar devolve a chave uma vez e a listagem nunca; AC-016-03 gerar de novo revoga a anterior (a antiga passa a dar 401); AC-016-04 duas pessoas com chaves diferentes recebem cada uma a **sua** agenda; AC-016-05 chave revogada ou desconhecida: 401 e nada lido; AC-016-06 sem chaves ativas e sem env: 401; AC-016-07 com env antigo a rota segue funcionando; AC-016-08 nenhum log com a chave; AC-016-09 as rotas de gerar/revogar exigem login.

## 6. Do lado do Coach (amazfit-dados)
**Nada muda.** A tela Conexões e chaves já guarda a chave de cada pessoa cifrada e manda no cabeçalho `X-API-KEY`. O Coach ter vários usuários é outra spec (008).

## 7. Perguntas em aberto
- **Q-016-1** Onde a pessoa gera a chave (comando no bot ou tela web)? Depois.
- **Q-016-2** Níveis da agenda por pessoa: só quando existir a segunda pessoa.

## 8. Risco do deploy
Merge = deploy do Meu Secretário (~3 min fora do ar). A migration é aditiva. Fazer em horário calmo, com o merge só quando o Rafael decidir.
