# Plan 007 — Configurações do usuário

- **Spec:** `spec.md` (em revisão)
- **Migrations:** nenhuma (faixa 0070–0079 reservada, não usada).
- **Arquivos compartilhados:** não edita `Program.cs`, `DependencyInjection.cs`, `AppSettings` nem arquivos da 008. Registra-se no `AddServices` do `SettingsModule`.

## 1. Domain (`MeuSecretario.Domain/Settings`)

Funções puras, sem I/O, com teste unitário. Reaproveita `RawValue`, `RawBody`, `PyNumber` (002) e `ApiProviders` (008).

| Tipo | Assinatura | Papel |
|---|---|---|
| `ProfileRules` | `static ProfileUpdateResult Validate(RawBody body)` ⇒ `Ok(ProfileChanges)` \| `Error(message)` | ordem da spec §3.2: nada a atualizar, conversão de `meses` (`PyNumber.TryInt`), nome, estado, faixa de meses; `""` ⇒ `null` para `email`/`cidade`/`estado`; **não** faz `trim` do nome gravado (P-007-3) |
| `ProfileChanges` | `record` com `Change<string?>?` por coluna (ausente × valor × `null`) | só as colunas enviadas entram no `UPDATE` |
| `NotificationRules` | `static NotificationUpdateResult Validate(NotificationInput input)` ⇒ formato `H:M` do check-in (`split(":")` com 2 partes, cada uma por `PyNumber.TryInt`), faixa de hora 18–23 | RN-007-09 |
| `NotificationInput` | `record(bool MorningEnabled, string MorningTime, bool EveningEnabled, string EveningTime, bool FinancialEnabled, int DaysBeforeDue)` com os padrões da spec §3.7 aplicados na leitura | PUT total |
| `NotificationDefaults` | `const` dos padrões (`07:00`, `19:00`, 3) e `DaysBeforeDue.Read(int threshold)` (`0 ⇒ 3`) e `TimeText.First5(string)` | RN-007-07, 08 |
| `AddressLabels` | `IReadOnlyList<string> All` (`casa`, `trabalho`, `outro`); `bool TryNormalize(string? raw, out string label)` (`trim` + `ToLowerInvariant`); `const string ValidListText = "casa, outro, trabalho"` | RN-007-10 |
| `PyTruth` | `static bool IsTruthy(RawValue)` (`true`, número ≠ 0, texto não vazio, lista/objeto não vazio; `false`, `0`, `""`, `null`, ausente falsos) | `useOwnKey` (sem P-007-2) e `type or provedor` |
| `ApiKeyInput` | `static ApiKeyInputResult Read(RawBody body)` ⇒ `provider` (`type` se truthy, senão `provedor`), `useOwn`, `rawKey` (`Trim`), `KeyWasNotText` | spec §3.4 e §3.5 |
| `SettingsMessages` | constantes (títulos de 400/404/500 literais da spec) | RN-007-13 |

`Domain` não referencia `System.Text.Json` (a `RawValue` existe para isso).

## 2. Application (`MeuSecretario.Application/Settings`)

**Portas** (`Application/Settings`):
- `IProfileRepository`: `GetAsync(userId)` ⇒ `ProfileRow?`; `UpdateAsync(userId, ProfileChanges)` ⇒ `ProfileUpdateOutcome { Updated(ProfileRow), NotFound, Failed }` (a violação de UNIQUE/tamanho/NOT NULL é `Failed`, tratada como 500 de texto fixo; com P-007-3 aprovada, `EmailTaken` e `TooLong` viram resultados próprios).
- `IApiKeySettingsRepository`: `ListPreferencesAsync(userId)` ⇒ `IReadOnlyList<ApiKeyPreferenceRow(provider, useOwnKey, hasKey)>`; `SaveOwnKeyAsync(userId, provider, useOwn, encryptedKey?)` ⇒ `bool` (uma transação: upsert da preferência; se `encryptedKey` não nulo, upsert da chave com `ativo = true`; se `useOwn` falso, desativa a chave).
- `IUserNotificationSettingsRepository`: `GetAsync(userId)` ⇒ `NotificationRow?`; `UpsertAsync(userId, NotificationInput)` ⇒ `bool`.
- `IFavoriteAddressRepository` (**compartilhada com a 012**): `ListAsync(userId)` ⇒ `IReadOnlyList<AddressRow(id, label, address, lat?, lng?)>` (ordenada por `label`); `UpsertAsync(userId, label, address, lat?, lng?)`; `DeleteAsync(userId, label)` ⇒ `bool`.
- `IProviderKeyValidator`, `IEncryptionService`, `ICurrentUser` já existem.

**Casos de uso** (um por classe, `ExecuteAsync`, resultado tipado, sem HTTP): `GetProfile`, `UpdateProfile`, `GetApiKeySettings`, `SaveOwnApiKey`, `ValidateProviderKey`, `GetNotificationSettings`, `SaveNotificationSettings`, `ListAddresses`, `SaveAddress`, `DeleteAddress`, `GetUserSettings` (compõe `GetProfile`-reader + os outros três readers, na mesma conexão). A validação na ordem da spec fica no `Domain`/caso de uso; o texto em português só em `SettingsMessages` (Api), como `AuthMessages`.
- `SaveOwnApiKey`: valida provedor (`ApiProviders.IsValid`), chave obrigatória, cifra com `IEncryptionService.Encrypt(rawKey)` e chama o repositório; **não** loga a chave; falha de cifra/banco ⇒ resultado `Failed`.
- `ValidateProviderKey`: ordem da spec §3.5 (campos ⇒ provedor ⇒ `IProviderKeyValidator`); `ProviderUnavailableException` ⇒ `Unavailable`.
- Leitores compartilhados entre os `GET` e o `settings`: `ProfileReader`, `ApiKeySettingsReader`, `NotificationSettingsReader`, `AddressReader` (Application), montam o modelo de resposta a partir das linhas; os endpoints e `GetUserSettings` os reutilizam (RN-007-12).

## 3. Infrastructure (`Infrastructure/Settings`, Dapper; sem `AppDbContext`)

`IDbConnectionFactory`, SQL parametrizado, tabelas minúsculas sem aspas. `PostgresException`: `23505`/`22001`/`23502`/`23503`/`22P02`/`22007`/`22008`/`22003`/`23514` ⇒ `Failed` (500 de texto fixo na rota; log só `SafeError.Describe`).

```sql
-- ProfileRepository
SELECT id, nome, email, numero_whatsapp, cidade, estado, fuso_horario, meses_reserva_emergencia
FROM usuarios WHERE id = @uid AND ativo = true;

-- UPDATE montado só com fragmentos fixos (uma coluna = @parâmetro por campo enviado)
UPDATE usuarios SET nome = @nome, email = @email /* ... só as enviadas */
WHERE id = @uid AND ativo = true
RETURNING id, nome, email, numero_whatsapp, cidade, estado, fuso_horario, meses_reserva_emergencia;

-- ApiKeySettingsRepository
SELECT p.provedor, p.usar_chave_propria, CASE WHEN c.id IS NOT NULL THEN true ELSE false END AS has_key
FROM preferenciaschaveapi p
LEFT JOIN chavesapiusuario c ON c.usuario_id = p.usuario_id AND c.provedor = p.provedor AND c.ativo = true
WHERE p.usuario_id = @uid;
-- (com P-007-6: FROM (VALUES ('gemini'),('weather'),('openroute')) v(provedor) LEFT JOIN preferenciaschaveapi p ... LEFT JOIN chavesapiusuario c ...)

-- SaveOwnKey: uma NpgsqlTransaction (ReadCommitted), três comandos
INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria)
VALUES (@uid, @prov, @uso)
ON CONFLICT (usuario_id, provedor) DO UPDATE SET usar_chave_propria = EXCLUDED.usar_chave_propria, atualizado_em = now();

INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo)
VALUES (@uid, @prov, @chave, true)
ON CONFLICT (usuario_id, provedor) DO UPDATE SET chave_api_criptografada = EXCLUDED.chave_api_criptografada, ativo = true, atualizado_em = now();

UPDATE chavesapiusuario SET ativo = false, atualizado_em = now() WHERE usuario_id = @uid AND provedor = @prov;

-- NotificationSettingsRepository
SELECT resumo_matinal_ativo, resumo_matinal_hora::text AS resumo_matinal_hora,
       checkin_noturno_ativo, checkin_noturno_hora::text AS checkin_noturno_hora,
       alertas_financeiros_ativos, dias_antes_vencimento
FROM notificationconfigs WHERE usuario_id = @uid;

INSERT INTO notificationconfigs
       (usuario_id, resumo_matinal_ativo, resumo_matinal_hora, checkin_noturno_ativo, checkin_noturno_hora,
        alertas_financeiros_ativos, dias_antes_vencimento)
VALUES (@uid, @matAtivo, CAST(@matHora AS time), @notAtivo, CAST(@notHora AS time), @finAtivo, @threshold)
ON CONFLICT (usuario_id) DO UPDATE SET
    resumo_matinal_ativo = EXCLUDED.resumo_matinal_ativo, resumo_matinal_hora = EXCLUDED.resumo_matinal_hora,
    checkin_noturno_ativo = EXCLUDED.checkin_noturno_ativo, checkin_noturno_hora = EXCLUDED.checkin_noturno_hora,
    alertas_financeiros_ativos = EXCLUDED.alertas_financeiros_ativos,
    dias_antes_vencimento = EXCLUDED.dias_antes_vencimento, updated_at = now();

-- FavoriteAddressRepository
SELECT id, label, endereco_completo AS address, latitude, longitude
FROM enderecosfavoritos WHERE usuario_id = @uid ORDER BY label;

INSERT INTO enderecosfavoritos (usuario_id, label, endereco_completo, latitude, longitude)
VALUES (@uid, @label, @addr, @lat, @lng)
ON CONFLICT (usuario_id, label) DO UPDATE SET
    endereco_completo = EXCLUDED.endereco_completo, latitude = EXCLUDED.latitude,
    longitude = EXCLUDED.longitude, updated_at = now();
-- (com P-007-4 e sem 'coordinates' na entrada: latitude = COALESCE(CASE WHEN enderecosfavoritos.endereco_completo = EXCLUDED.endereco_completo THEN enderecosfavoritos.latitude END, NULL) e idem longitude)

DELETE FROM enderecosfavoritos WHERE usuario_id = @uid AND label = @label;   -- linhas afetadas = 0 ⇒ 404
```

Detalhes:
- `CAST(@matHora AS time)`: a hora vai como **texto** e o PostgreSQL decide o que é válido (`7:30`, `07:00:15`, `7:00 pm`), como o Flask (o driver Python também mandava texto). Os parâmetros de hora usam `NpgsqlDbType.Text`; `@threshold` usa `Integer` (valor `int` convertido por `PyNumber.TryInt`; estouro de `int4` falha no .NET antes de chegar ao banco ⇒ resultado `Failed`, o mesmo 500).
- Latitude/longitude: `NpgsqlDbType.Numeric` com `decimal` (string numérica convertida por `decimal.TryParse` invariável; falha ⇒ `Failed`).
- O `UPDATE` de perfil devolve `meses_reserva_emergencia` anulável; `null` explícito grava `NULL` (parâmetro `DBNull`), ausente não entra no `SET`.
- `SaveOwnKey` abre uma conexão e uma transação; o `INSERT` de chave só roda com `encryptedKey` não nulo; o `UPDATE` de desativação só com `useOwn = false`; `Commit` no fim, `Rollback` em qualquer exceção (AC-007-27).

**DI:** `AddSettingsInfrastructure(IServiceCollection)` chamada pelo `SettingsModule.AddServices` (Api referencia Infrastructure); não se edita `DependencyInjection.cs`.

## 4. Api (`MeuSecretario.Api/Settings`)

- `SettingsModule : IFeatureModule`: registra serviços e mapeia as 11 rotas em `app.MapGroup("/api/user")`, todas com `.RequireUser()`; `DELETE /addresses/{label}` com `label` como `string`.
- Leitor de corpo: `SettingsBodyReader` (como `AccountsBodyReader`): `JsonBody.ReadObjectAsync` ⇒ `RawBody` com `RawValue` por tipo JSON (corpo ausente/inválido/não-objeto ⇒ `RawBody` vazio).
- Respostas: `Envelopes.A.Success(data, message, status)` / `Envelopes.A.Error(message, status)`; **`data` com as chaves na ordem da spec** (dicionários ordenados; `daysBeforeDue`, `coordinates` condicionais). `SettingsResponses` monta cada bloco uma vez e é usada pelos endpoints e pelo `settings`.
- `Cache-Control: no-store` nas respostas de `/api/user/*` (perfil e preferências de chave; barato e inofensivo). Não altera o contrato.
- Logs: `SettingsLog` (`[LoggerMessage]`, EventIds 7001+, `ILogger<SettingsModule>`): só `Error` (`SafeError.Describe`) e `userId`; nunca corpo, chave, e-mail, endereço, hora.
- Mensagens: `SettingsMessages` (a mesma classe do Domain ou espelho com `const`), exatamente os textos da spec.
- `GET /api/user/settings`: um caso de uso, **uma** conexão, quatro leituras em sequência; perfil sem linha encerra com 404.

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário | `Domain/Settings/ProfileRulesTests` | ordem das validações, conversão estilo `int()` (`"12"`, `" 12 "`, `12.9`, `true`, `"5.5"`, `[]`), `""` ⇒ `null`, nome sem `trim` gravado, estado vazio, borda 1/36/0/37 (AC-007-10 a 16) |
| Unitário | `Domain/Settings/NotificationRulesTests` | `H:M` (`"19"`, `"19:00:00"`, `null`, `1900`, `"7:00"`, `"24:00"`, `"23:30"`, `"18:75"`), padrões do PUT total, `0 ⇒ 3`, `First5` (AC-007-41 a 46) |
| Unitário | `Domain/Settings/AddressLabelsTests`, `PyTruthTests`, `ApiKeyInputTests` | `" CASA "`, `type or provedor`, truthiness, `key` não-texto (AC-007-22 a 26, 51, 52) |
| Unitário | `Application/Settings/*UseCaseTests` (NSubstitute) | ordem das validações por rota, provedor antes de chave, validador `Valid/Invalid/NotSupported/Unavailable`, a chave nunca em `ToString()`/log (AC-007-24, 30 a 33) |
| Integração (Docker) | `Settings/ProfileEndpointsTests` (`PostgresFixture`) | AC-007-02, 03, 10 a 17, 82 (corpo exato como texto; UNIQUE de e-mail; 500 sem vazar) |
| Integração | `Settings/ApiKeysEndpointsTests` | AC-007-20 a 28, 33; chave decifrada com `IEncryptionService` do `PostgresFixture`; rollback com falha injetada (trigger de teste que lança no `INSERT` de `chavesapiusuario`) |
| Integração | `Settings/ValidateKeyEndpointTests` | AC-007-30 a 33 com `IProviderKeyValidator` substituído por fake (nenhuma chamada externa) |
| Integração | `Settings/NotificationsEndpointsTests` | AC-007-40 a 47 (colunas reais, `time` com e sem segundos, `NULL` em `alertas_financeiros_ativos`, UNIQUE, hora aceita pelo PostgreSQL) |
| Integração | `Settings/AddressesEndpointsTests` | AC-007-50 a 55, CHECK de rótulo, ordem, `coordinates` condicional, `numeric` estourado |
| Integração | `Settings/SettingsAggregateTests` | AC-007-60, 61, 70 (JSON do `settings` idêntico aos individuais) |
| Integração | `Settings/SettingsAuthAndIsolationTests` | AC-007-01, 81 (dois usuários, as escritas de cada rota), AC-007-80 (500 sem sentinela no corpo e no log) |
| Arquitetura | NetArchTest existente | Application sem Npgsql; Api sem Infrastructure.Dapper; `Domain/Settings` sem `System.Text.Json` |
| Contrato | corpos exatos nos testes de integração (como 001/002/008) | spec §3 |

`PostgresFixture.ResetAsync` precisa limpar `notificationconfigs`, `enderecosfavoritos`, `preferenciaschaveapi`, `chavesapiusuario` (via `TRUNCATE usuarios CASCADE` já cobre pela FK); o arquivo de teste da feature semeia os usuários. A fixture do Fernet usa a `ENCRYPTION_KEY` fictícia das `AuthFixtures`.

## 6. Fakes e fixtures

- Fake de `IProviderKeyValidator` (retorno programável) e de `IApiKeySettingsRepository` nos testes de caso de uso.
- Trigger de teste (`CREATE FUNCTION ... RAISE EXCEPTION`) para forçar falha no segundo `INSERT` e provar o rollback (AC-007-27), instalada e removida pelo próprio teste.
- Chaves de teste sempre fictícias; nenhum teste imprime a chave; verificação de log por `TestLogger` que falha se contiver a sentinela.

## 7. Riscos e decisões

- **Hora do resumo matinal sem validação.** O contrato reproduz o Flask (aceita o que o PostgreSQL aceita; erro vira 500). A confiança de "mesma aceitação do Postgres" vem do `CAST(@x AS time)`; um teste de tabela com ~15 formatos (`7:30`, `07:00:15`, `7pm`, `25:00`, `abc`, `""`, `24:00`) fixa o comportamento.
- **`daysBeforeDue` (desvio 250, implementado).** Coluna própria `dias_antes_vencimento` (migration `0070`); `alerta_potes_threshold` deixou de ser sobrescrito e ficou só para o limiar de pote da 012/013.
- **PUT total de notificações.** Um cliente que mande só um bloco zera os outros para os padrões; o Angular manda tudo. Reproduzido; qualquer cliente novo precisa ler antes de escrever.
- **Coordenadas apagadas pelo site (P-007-4).** Enquanto não aprovada, editar um endereço pelo site apaga lat/lng do WhatsApp; a 012 deve tratar `lat/lng` nulos (geocodificar sob demanda).
- **Transação da chave.** O `PUT` usa uma conexão/transação própria (não reutiliza os repositórios da 008); se a 008 mudar a regra de preferência (ex.: exigir chave para `usar_chave_propria = true`), a 007 não herda. Ver Q-007-1.
- **Duplicidade de lógica `trim` da chave.** A 008 cifra "sem `trim`" e a 007 cifra a chave **já `trim`ada**; o mesmo usuário pode ter chaves tratadas de forma diferente conforme a rota usada. Reproduzido do Flask.
- **Sem consentimento LGPD.** A rota grava a chave do usuário sem consultar `consentimentousuario`; igual ao Flask (a 008 tampouco exige). Registrado para a revisão de LGPD.
- **Oráculo de validação de chaves.** Qualquer usuário autenticado faz o servidor chamar provedores externos com chaves arbitrárias (5 s de timeout). O rate limit global da 009 (`RATE_LIMIT_ENABLED`) é a mitigação; sem ele, só o nginx.
