# Tasks 007 — Configurações do usuário

Regra: cada tarefa termina com testes verdes e commit. Ordem importa. Teste primeiro (vermelho), código depois. Pré-requisito: spec aprovada e respostas às Q-007-1 e P-007-1 a P-007-6. A 001a, a 002 (`PyNumber`, `RawValue`) e a 008 (`ApiProviders`) já estão na base; a 010e (`IProviderKeyValidator`) também. **Não edita arquivos compartilhados nem da 008.**

## Divisão em entregas

| Entrega | Tarefas | Observação |
|---|---|---|
| **007a** | T1–T8 | perfil, notificações e `settings` (sem chave de API nem endereço) |
| **007b** | T9–T15 | chaves de API (PUT/GET/validate) e endereços; `IFavoriteAddressRepository` já liberada para a 012 na T12 |
| Fechamento | T16 | desvios, spec, PR |

As duas entregas podem sair no mesmo PR; a ordem interna é a das tarefas. O `GET /api/user/settings` só fica completo na T15.

## 007a — perfil e notificações

- [ ] **T1 — Domain: perfil.** `ProfileRules`, `ProfileChanges`: testes vermelhos ⇒ verdes (ordem 1 a 5, conversão `int()`, `""` ⇒ `null`, nome sem `trim`, estado, faixa) (AC-007-10 a 16).
- [ ] **T2 — Domain: notificações.** `NotificationRules`, `NotificationInput`, `NotificationDefaults` (`0 ⇒ 3`, `First5`): tabela de horários (`"19"`, `"19:00:00"`, `null`, `1900`, `"7:00"`, `"24:00"`, `"23:30"`, `"18:75"`) (AC-007-41 a 46).
- [ ] **T3 — Repositórios de perfil e notificações** (`ProfileRepository`, `NotificationSettingsRepository`, SQL do plan §3; `AddSettingsInfrastructure`): integração em Testcontainers: UNIQUE de e-mail, NOT NULL de `fuso_horario`, `varchar` estourado, `NULL` em `meses`, upsert de notificações sem duplicar, `CAST(@x AS time)` com a tabela de ~15 formatos, `time` com e sem segundos no `GET` (AC-007-17, 41, 42, 45).
- [ ] **T4 — Casos de uso** `GetProfile`, `UpdateProfile`, `GetNotificationSettings`, `SaveNotificationSettings` com fakes: ordem das validações, 404 do perfil, resultados tipados (AC-007-02, 03, 10 a 16, 40 a 47).
- [ ] **T5 — Api (esqueleto):** `SettingsModule`, `SettingsBodyReader`, `SettingsMessages`, `SettingsResponses`, `SettingsLog`, `Cache-Control: no-store`. Teste de integração de `RequireUser` com 401 nas 4 mensagens para as rotas mapeadas (AC-007-01).
- [ ] **T6 — Endpoints `GET/PUT /profile`:** corpo exato como texto, 500 sem vazamento, e-mail repetido sem o e-mail alheio, corpo completo do Angular (AC-007-02, 03, 10 a 17, 82).
- [ ] **T7 — Endpoints `GET/PUT /notifications`:** padrões sem linha (sem criar), linha completa, `PUT` total, 400/500 do §3.7, `daysBeforeDue` 0/negativo/45 (AC-007-40 a 47).
- [ ] **T8 — `GetUserSettings` parcial + `GET /settings`** com perfil e notificações (apiKeys `[]` e addresses `[]` provisórios só até T15; **não** liberar a rota antes da T15 em produção) (AC-007-60, 61 parcial).

## 007b — chaves e endereços

- [ ] **T9 — Domain: chaves e rótulos.** `PyTruth`, `ApiKeyInput`, `AddressLabels`: truthiness, `type or provedor`, `key` não-texto, `" CASA "` (AC-007-22 a 26, 51, 52).
- [ ] **T10 — `ApiKeySettingsRepository`** com a transação do plan §3 (preferência + chave + desativação) e a leitura com `has_key`: integração: upsert sem 409, reativação, desativação sem apagar, `ativo = NULL` como inativa, rollback com a trigger de teste (AC-007-20 a 23, 27).
- [ ] **T11 — Casos de uso** `SaveOwnApiKey`, `GetApiKeySettings`, `ValidateProviderKey`: provedor antes de chave, `trim`, cifra Fernet (chave decifra igual), a chave nunca em `ToString()`/log; validador fake `Valid/Invalid/NotSupported/Unavailable` (AC-007-22 a 26, 30 a 33).
- [ ] **T12 — `FavoriteAddressRepository` + `IFavoriteAddressRepository`** (porta liberada para a 012 ao fim desta tarefa): integração: CHECK de rótulo, upsert, ordem por `label`, `numeric` estourado, `DELETE` 0 linhas (AC-007-50 a 55).
- [ ] **T13 — Casos de uso** `ListAddresses`, `SaveAddress`, `DeleteAddress`: rótulo antes de endereço, `coordinates` condicional, sem `coordinates` apaga (P-007-4 ainda não aprovada) (AC-007-51 a 55).
- [ ] **T14 — Endpoints** `GET/PUT /api-keys`, `POST /api-keys/validate`, `GET/POST /addresses`, `DELETE /addresses/{label}`: corpo exato como texto, 503 sem a mensagem da exceção, chave ausente de corpo/log/banco, 405 em `PUT /addresses/casa`, 404 HTML com barra final (AC-007-20 a 33, 50 a 55, 82).
- [ ] **T15 — `GET /settings` completo** com os quatro leitores compartilhados e o teste de paridade interna (JSON de `settings` idêntico ao das quatro rotas) (AC-007-60, 61, 70).

## Fechamento

- [ ] **T16 — Contrato e desvios.** Revisar cada resposta contra o código Python lido (corpo exato dos testes de integração): isolamento de tenant com dois usuários nas 11 rotas (AC-007-81), 500 sem sentinela no corpo nem no log (AC-007-80), desvios 008, 019, 024 e 028 marcados como `implementado` neste escopo; criar os arquivos `parity-deviations/` dos `P-007-N` aprovados; registrar no `parity-deviations/` o achado "`daysBeforeDue` grava em `alerta_potes_threshold`" (P-007-1, mesmo que rejeitado, para a 013); avisar a 012 sobre `IFavoriteAddressRepository` e sobre coordenadas nulas; spec atualizada para `aprovada`/`implementada`; PR aberto.

Definição de pronto: `constitution.md` §10.
