# Tasks 008 — API keys e LGPD

Regra: cada tarefa termina com testes verdes e commit. Ordem importa. Teste primeiro, código depois.

## Divisão proposta em entregas

| Entrega | Tarefas | Observação |
|---|---|---|
| **008-0** | T1–T4 | PR **separado e primeiro** (arquivos compartilhados em `Api/Http/` e porta usada pela 010/009/012) |
| **008a** | T5–T13 | chaves, preferências, uso, logs, health e implementação da porta |
| **008b** | T14–T19 | LGPD |
| Fechamento | T20 | desvios, spec, PR |

A 010 só precisa da T3 (interfaces e tipos) para começar; a 009 e a 012 só precisam da T1–T2.

## 008-0 — blocos compartilhados

- [x] **T1 — `PythonDateFormat`** (`Api/Http`): testes vermelhos (fração só com microssegundos, `+00:00`, `null`, local sem fuso) ⇒ implementação ⇒ verdes.
- [x] **T2 — `IApiKeyValidator` + `ApiSecretKeyValidator` + `RequireApiKeyFilter`** com `ApiKeyUnauthorized.EnvelopeC` (AC-008-01, 02): testes unitários do validador (tempo constante via hash, tamanhos diferentes) e do filtro (`next` não chamado, 401 em envelopes C, B e E). Registrar o validador em `AddInfrastructure` só se a 009/012 ainda não o tiverem feito; senão o `ApiKeysModule` o registra com `TryAdd`.
- [x] **T3 — Porta `IApiKeyProvider`** em `Application/Abstractions`: interfaces, `ApiKeyResolution`, `ApiKeyFailure`, `ApiKeyMessages` (textos literais) e `ToString()` sem a chave. Teste unitário das mensagens (AC-008-60 a 62, 64, 68). Avisar a 010.
- [x] **T4 — Fixtures do Python:** dispensada; reutiliza `AuthFixtures` (tokens Fernet do Python, token adulterado) e texto puro nos próprios testes.

## 008a — chaves, preferências, uso, logs

- [x] **T5 — Domain** (`ApiProviders`, `PreferenceKind`, `UsageSummary`, `PlanLimit`, `UsageMonth`): testes vermelhos ⇒ verdes (AC-008-12, 30, 66).
- [x] **T6 — Casos de uso** `RegisterApiKey`, `ListApiKeys`, `UpdateApiKey`, `DeactivateApiKey` com fakes: ordem das validações, `usuario_id` do dono (AC-008-10 a 18 no nível unitário).
- [x] **T7 — Casos de uso** `SetPreference`, `ListPreferences`, `DeletePreference`, `GetUsage`, `GetAccessLogs` com fakes (AC-008-20 a 22, 30, 31; `limit` estilo `int()` com 500 para inválido).
- [x] **T8 — Repositórios Dapper** (`ApiKeyRepository`, `PreferenceRepository`, `UsageRepository`, `AccessLogRepository`) com testes de integração em Testcontainers: UNIQUE, FK, filtro de dono, upsert, agregação, `criado_em` (AC-008-10, 13, 14, 17, 20, 30, 31).
- [x] **T9 — Endpoints das rotas 1 a 9 e 16** (`ApiKeysModule`, `ApiKeysMessages`, `ApiKeysLog`, `GetBool`): corpo tolerante (024), 500 de texto fixo sem vazamento, 404 de id inválido (AC-008-01 a 04, 10 a 22, 30, 31, 50).
- [x] **T10 — `IdorTests`** com dois usuários semeados (AC-008-17).
- [x] **T11 — `PlanRepository` + `ApiKeyProvider`**: testes unitários da porta (AC-008-60 a 68, ordem das decisões, fail-open, mensagens literais, `FakeTimeProvider`) e de integração (plano ativo/expirado, upsert de uso, `ultimo_uso_em`, Fernet e texto puro; AC-008-63 a 66).
- [x] **T12 — DI da feature:** `AddApiKeysInfrastructure`, `ISystemApiKeys` (de `AppSettings`) e registro da porta; teste de arquitetura verde.
- [x] **T13 — Contrato 008a:** comparar corpo exato (como texto) de cada resposta da spec §4.1–4.9 e §4.16, incluindo ordem das chaves e formato dos timestamps.

## 008b — LGPD

- [x] **T14 — Domain** `ConsentCatalog` (descrições literais, `obrigatorio`, mensagem com a lista `repr`) e `AccountDeletion` com testes (AC-008-42, 45, 48).
- [x] **T15 — Casos de uso** `RegisterConsent`, `ListConsents`, `GetInitialConsents`, `CheckConsent` (fail-closed no erro de banco), `ExportUserData`, `DeleteAccountData` com fakes (AC-008-40 a 48).
- [x] **T16 — `ConsentRepository` + `AccountDataRepository`** em integração: colunas reais (`versao_consentimento`, `timestamp_acesso`, `nome_plano`, `limite_*`), duplicidade 23505, FK 23503, `deletar-conta` transacional com falha injetada (AC-008-43, 47, 49).
- [x] **T17 — Endpoints das rotas 10 a 15** com `Cache-Control: no-store` no export e confirmação exata no delete (AC-008-40 a 49, 50).
- [x] **T18 — Teste de `exportar`** com Fernet do Python, texto puro, token corrompido (500 sem vazar), assinatura nos três estados, `preco_mensal` `0` inteiro (AC-008-47).
- [x] **T19 — Contrato 008b:** corpos exatos da spec §4.10–4.15.

## Fechamento

- [ ] **T20 — Desvios e PR:** marcar 005, 006 (só checagem de dono), 008, 024 e 028 como `implementado` nos trechos desta feature; registrar decisões sobre P-008-1 a P-008-9 (cada aprovado vira arquivo em `parity-deviations/`, criado por quem aprovar); atualizar `api-contract.md` (§7: rotas `/api-keys/*` agora alcançáveis) e esta spec se algo mudou; abrir PR.

## Tarefas condicionais (APROVADAS em 2026-10-02: C1 a C6 entram nas tarefas acima; 051 rejeitado; flag 052 em T9)

- [x] **C1 — P-008-1:** upsert de consentimento com `revogado_em` + testes (reativar, revogar, versão nova).
- [x] **C2 — P-008-2 e P-008-3:** 409 de chave duplicada e 404 de usuário inexistente + testes.
- [x] **C3 — P-008-4:** `DeleteAccountCompletely` com a ordem explícita (`transacoes`, `agendamentos`, `contas`, `potesdegastos`, `subcategoria`, `macrocategoria`, `usuarios`), testes com dados reais de um usuário completo, `RESTRICT` e dados de outro usuário preservados.
- [x] **C4 — P-008-5:** `usuario_id` obrigatório em `PUT/DELETE /chave/<id>`.
- [x] **C5 — P-008-7 e P-008-8:** limite só com `tipo_chave = 'sistema'`; escrita em `logacessochaveapi` pela porta.
- [x] **C6 — P-008-9:** 400 para tipos JSON inesperados.

Definição de pronto: `constitution.md` §10.

## Estado em 2026-10-02

Implementadas T1 a T19 e C1 a C6 (T4 dispensada: os testes reutilizam os tokens Fernet do Python em `AuthFixtures`).
Pendente: T20 (marcar os desvios 005, 006, 008, 024, 028, 041 a 048 e 052 como `implementado` em `parity-deviations/` e atualizar
`api-contract.md` §7; arquivos de `foundation/`, fora do escopo deste agente) e abrir o PR.
