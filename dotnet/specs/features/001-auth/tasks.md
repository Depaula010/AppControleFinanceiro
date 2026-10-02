# Tasks 001 — Auth

Regra: cada tarefa termina com testes verdes e um commit. Ordem importa. As tarefas T1–T6 formam a **001a** (PR único, entra em `main` antes da onda 1); T7–T11 formam a **001b**.

## 001a — blocos compartilhados

- [ ] **T1 — Fixtures do Python.** Escrever `tests/Fixtures/generate_auth_fixtures.py` e commitar a saída (hashes pbkdf2 e scrypt, token Fernet, JWTs válido/expirado/sem `user_id`/`alg=none`), com chaves fictícias.
- [ ] **T2 — `WerkzeugPasswordHasher`.** Testes vermelhos (AC-001-26, 27, 50) ⇒ implementação ⇒ verdes. Adicionar `BouncyCastle.Cryptography`.
- [ ] **T3 — `FernetEncryptionService`.** Vetores oficiais + token do Python (AC-001-51, 52).
- [ ] **T4 — `JwtTokenService`.** Emissão e validação (AC-001-28, 32, 34); `RequireExpirationTime`, `ClockSkew = 0`, só HS256.
- [ ] **T5 — `Envelopes.A.SuccessWithFields`** + teste; ajustar `api-contract.md` §1 se necessário.
- [ ] **T6 — `RequireUserFilter` + `ICurrentUser`** com testes de integração usando uma rota de teste (AC-001-40 a 44). `IDbConnectionFactory` e `AppSettings.PasswordHashIterations`. **Abrir PR da 001a.**

## 001b — rotas

- [ ] **T7 — Domain.** `PhoneNumber`, `CardDays`, `PasswordRules`, `DefaultAccounts` com testes (AC-001-02, 04, 06, 07).
- [ ] **T8 — Casos de uso** `RegisterUser`, `LoginUser`, `VerifyToken` com fakes (ordem das validações; AC-001-03 a 09, 20 a 25, 30 a 33).
- [ ] **T9 — `UserRepository` + migration 0001** com testes de integração em Testcontainers (AC-001-01, 08, 11, 12, 13).
- [ ] **T10 — Endpoints** `AuthModule` com leitura de corpo tolerante (desvio 024) e `AuthMessages` (AC-001-10 e demais por rota).
- [ ] **T11 — Teste de contrato** das respostas exatas (§4 da spec) e fechamento: desvios 017, 024 e 031 marcados como `implementado` (se aprovados), spec atualizada, PR aberto.

Definição de pronto: `constitution.md` §10.
