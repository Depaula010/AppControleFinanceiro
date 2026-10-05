# Spec 014 — Alertas de calendário (`/calendar-alerts/*` e `CalendarAlertService`)

- **Estado:** implementada em 2026-10-05 (fluxo aprovado por delegação do Rafael; só paridade com o Flask, mais os desvios já aprovados 003, 004, 005, 008 e 024; novos desvios 120 a 122 ficam `proposto` e **não** são implementados)
- **Onda:** 3
- **Origem (Python):** `app/routes/calendar_alerts.py:1-184` (4 rotas); `app/services/calendar_alert_config_service.py:1-132` (config e usuários com alerta ativo); `app/services/calendar_alert_service.py:1-387` (busca de eventos, claim anti-duplicação, mensagem, processamento por usuário); `app/jobs/task_alerts.py` (só o laço, da 013); `app/middleware/security.py:84-212` (whitelist); DDL real em `dotnet/migrations/0000_baseline.sql` (`calendaralertconfigs`)
- **Depende de:** 001a (`RequireUser`, `ICurrentUser`, `Envelopes`, `SafeError`), 010 (`IGoogleAccountService`, `ICalendarClient`, `IBotClient`, `ICache`, `CacheKeys.CalendarAlert`), 009 (whitelist derivada das rotas, desvio 005)
- **Consumida por:** 013 (job `TaskAlertsJob`, a cada minuto: `GetUsersWithActiveAlerts` + `CalendarAlertService.ProcessAlertsForUser`); o Angular **não** chama estas rotas (busca em `meusecretariofront` sem ocorrência) e o bot Baileys também não (busca em `bot-appfinanceiro-whatsapp`, `baileys-bot-group` e `bot-group` sem ocorrência); só `docs/MANUAL_ALERTAS_TAREFAS.md` as cita
- **Desvios aplicáveis (aprovados):** 003 (JWT + id do caminho igual ao do token), 004 (`get_or_create` existe), 005 (rotas alcançáveis: já feito pela 009-0), 008 (texto fixo nos 500), 024 (corpo tolerante). Propostas novas: §9 (120 a 122).

## Decisões aplicadas (já aprovadas)

| Desvio | Efeito nesta spec |
|---|---|
| 003 | As 4 rotas passam a exigir JWT (`RequireUser`, as 4 mensagens 401 da spec 001) e o `<id>` do caminho deve ser o do token, senão **403**. Condição de mapear chamadores: feito, nenhum cliente conhecido chama as rotas (Angular, bot e scripts do repositório). O `<id>` continua no caminho (contrato imutável), mas deixa de ser a fonte do usuário. Atalhos de iPhone/Android não são verificáveis a partir do código (Q-014-8). |
| 004 | `CalendarAlertConfigService.get_or_create_config` não existe no Flask (500 em tudo). Implementado: `INSERT` com o padrão do banco (`alertas_tarefas_ativo = false`, `minutos_antes = 1`) e leitura. |
| 005 | A whitelist é derivada das rotas registradas; `/calendar-alerts/ativar/*` e `/desativar/*` (404 externo no Flask) ficam alcançáveis. Nada a fazer na 014 além de registrar as rotas. |
| 008 | Todo 500 devolve texto fixo (§3); só o log leva `SafeError.Describe`. O `Erro ao atualizar configuração: <str(e)>` do serviço vira `Erro ao atualizar configuração`. |
| 024 | Corpo ausente, vazio, inválido ou não-objeto = objeto sem campos (o Flask dava 415/500). |

## 1. Objetivo

O usuário liga/desliga o aviso por WhatsApp de compromissos do Google Calendar e escolhe quantos minutos (1 a 60) antes quer ser avisado. O job da 013 chama a cada minuto o `CalendarAlertService` desta feature, que procura eventos que começam daqui a `minutos_antes` e manda a mensagem uma única vez por evento.

## 2. Rotas

| # | Método | Caminho | Auth | Envelope | Seção |
|---|---|---|---|---|---|
| 1 | GET | `/calendar-alerts/config/<int:usuario_id>` | JWT | B | 3.1 |
| 2 | POST | `/calendar-alerts/config/<int:usuario_id>` | JWT | B | 3.2 |
| 3 | POST | `/calendar-alerts/ativar/<int:usuario_id>` | JWT | B | 3.3 |
| 4 | POST | `/calendar-alerts/desativar/<int:usuario_id>` | JWT | B | 3.4 |

Origem das 4: `calendar_alerts.py:13, 37, 109, 156`; blueprint com `url_prefix='/calendar-alerts'` (`app/__init__.py:172`). **Só estas 4** rotas do `api-contract.md` pertencem à 014 (§"Alertas de calendário").

Convenções:
- `<int:usuario_id>`: só dígitos (outro texto, vazio ou barra final: 404 do Flask, já feito pelo roteamento); acima de `int.MaxValue` também 404, como na 010d.
- Envelope B: sucesso `{"status":"sucesso","mensagem":"...","config":{...}}` (GET sem `mensagem`); erro `{"status":"erro","mensagem":"..."}`. **Exceção (desvio 003):** o 401 de `RequireUser` continua no envelope A (`{"status":"error","message":"..."}`), com as 4 mensagens exatas da spec 001 §4.4; o Flask não tinha 401 aqui (Q-014-1).
- Ordem: 404 (rota) → 401 (JWT) → **403 (id ≠ token)** → regras da rota. O 403 é `{"status":"erro","mensagem":"Acesso negado"}` (texto novo; Q-014-2).
- `config` é sempre `{"alertas_tarefas_ativo": <bool>, "minutos_antes": <int>}`, nesta ordem.
- Corpo (024): lido como objeto; ausente, vazio, inválido ou não-objeto = `{}`; não exige `Content-Type`; chaves desconhecidas ignoradas; chave repetida vale a última.
- 500 (desvio 008): texto fixo por rota; log com `SafeError.Describe`, nunca corpo nem número de WhatsApp.

## 3. Contrato por rota

### 3.1 `GET /calendar-alerts/config/<id>`
`get_or_create` (RN-014-01) e devolve **200** `{"status":"sucesso","config":{"alertas_tarefas_ativo":false,"minutos_antes":1}}`. Sem linha, a linha é criada com o padrão (o GET escreve; é o que o desvio 004 manda implementar). 500: `Erro ao obter configuração de alertas`. *Origem: `calendar_alerts.py:13-34`.*

### 3.2 `POST /calendar-alerts/config/<id>`
Entrada: `ativo` e/ou `minutos_antes` (`null` equivale a ausente). Passos, **nesta ordem** (o primeiro que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | `ativo` e `minutos_antes` ausentes ou nulos | 400 `Nenhum parâmetro fornecido. Use 'ativo' e/ou 'minutos_antes'` |
| 2 | `ativo` **verdadeiro no sentido do Python** (`if ativo:`: `true`, número ≠ 0, texto não vazio, lista/objeto não vazio) e Google Calendar não conectado (`IGoogleAccountService.IsConnectedAsync`) | 400 `Você precisa conectar o Google Calendar primeiro para ativar alertas de tarefas` |
| 3 | `get_or_create` (cria a linha padrão se faltar; acontece **antes** da validação seguinte, como no Flask) | — |
| 4 | `minutos_antes` presente, não nulo, e que não seja inteiro JSON entre 1 e 60 (decimal, `5.0`, texto, booleano, lista, fora da faixa) | 400 `minutos_antes deve ser um número entre 1 e 60` |
| 5 | `ativo` presente, não nulo e que não seja `true`/`false` JSON | 400 `Erro ao atualizar configuração` (**divergência** do Flask, ver §9 e Q-014-3) |
| 6 | `UPDATE` ok | 200 (abaixo) |

Efeito do passo 6: `UPDATE calendaralertconfigs SET [alertas_tarefas_ativo = @ativo,] [minutos_antes = @minutos,] updated_at = CURRENT_TIMESTAMP WHERE usuario_id = @uid RETURNING alertas_tarefas_ativo, minutos_antes` (só as colunas enviadas; não há trigger de `updated_at` na tabela). Erro do banco nesse `UPDATE` ⇒ **400** `Erro ao atualizar configuração` (o Flask devolvia `Erro ao atualizar configuração: <str(e)>`; 008). Erro em qualquer outro ponto (consulta ao Google, `get_or_create`) ⇒ **500** `Erro ao atualizar configuração de alertas`.
Sucesso **200** `{"status":"sucesso","mensagem":"<msg>","config":{...}}` com `<msg>` montada assim (RN-014-02): `ativo = true` ⇒ `Alertas de tarefas ativados`; `ativo = false` ⇒ `Alertas de tarefas desativados`; `minutos_antes` informado ⇒ `alerta configurado para N minuto(s) antes`; as partes presentes unidas por ` e `. Exemplos: `Alertas de tarefas ativados e alerta configurado para 5 minuto(s) antes`; só minutos: `alerta configurado para 10 minuto(s) antes` (minúscula, como no Flask). *Origem: `calendar_alerts.py:37-106`, `calendar_alert_config_service.py:16-84`.*

### 3.3 `POST /calendar-alerts/ativar/<id>`
Corpo opcional `{"minutos_antes": 1-60}`. Passos: (1) Google não conectado ⇒ 400 com a mesma mensagem do passo 2 de 3.2 (**antes** de olhar o corpo); (2) `get_or_create`; (3) `minutos_antes` informado e inválido ⇒ 400 `minutos_antes deve ser um número entre 1 e 60`; (4) `UPDATE` com `ativo = true` (+ minutos). **200** `{"status":"sucesso","mensagem":"Alertas de tarefas ativados[ e alerta configurado para N minuto(s) antes]","config":{...}}`. Sem `minutos_antes` mantém o valor atual. Erro do `UPDATE` ⇒ 400 `Erro ao atualizar configuração`; demais falhas ⇒ 500 `Erro ao ativar alertas de tarefas`. *Origem: `calendar_alerts.py:109-153`.*

### 3.4 `POST /calendar-alerts/desativar/<id>`
Sem corpo e **sem** consulta ao Google. `get_or_create`, `UPDATE ... ativo = false`. **200** `{"status":"sucesso","mensagem":"Alertas de tarefas desativados","config":{...}}` (`minutos_antes` preservado). Erro do `UPDATE` ⇒ 400 `Erro ao atualizar configuração`; demais ⇒ 500 `Erro ao desativar alertas de tarefas`. *Origem: `calendar_alerts.py:156-184`.*

## 4. Serviço de alertas (sem rota; consumido pela 013)

`CalendarAlertService` reproduz `calendar_alert_service.py` e `calendar_alert_config_service.py:86-132`.

- **RN-014-10 Usuários com alerta ativo** (`get_users_with_alerts_active`): `calendaralertconfigs` com `usuarios` e `googlecalendartokens`, `alertas_tarefas_ativo = true` e `(needs_reconnect = false OR needs_reconnect IS NULL)`; devolve `(usuario_id, numero_whatsapp, minutos_antes)`; usuário sem linha de token fica de fora (junção interna); `ORDER BY usuario_id` é novo, só para ser determinístico (o Flask não ordena). Não filtra `usuarios.ativo` (Q-014-4; desvio 122).
- **RN-014-11 Janela** (`get_upcoming_events`): `agora` = relógio em São Paulo; `alvo = agora + minutos_antes min`; janela `[alvo − 30 s, alvo + 30 s]` enviada ao Google como `timeMin`/`timeMax` (`singleEvents`, `orderBy = startTime`) em **cada calendário selecionado** (`ICalendarClient.ListSelectedCalendarsAsync`). Falha ao listar calendários ⇒ lista vazia; calendário com erro é pulado; usuário sem Google conectado ou token inválido ⇒ lista vazia; qualquer outra exceção ⇒ lista vazia (log). Nunca lança (cancelamento da requisição propaga).
- **RN-014-12 Filtro:** só evento com horário (`dateTime`; dia inteiro é ignorado) e **futuro** (`start > agora`). Cada evento vira `{id, summary (padrão "Sem título"), start (texto do Google), location, description, calendar_name}`; a ordem é a do Google dentro de cada calendário, na ordem dos calendários.
- **RN-014-13 Claim anti-duplicação:** chave `CacheKeys.CalendarAlert(usuario, event_id, start)` (`calendar_alert:` + MD5 de `"{uid}:{event_id}:{start}"`), `SET NX EX 7200` com valor `processing` (`ICache.SetIfNotExistsAsync`). Já existe ⇒ pula o evento. **Redis indisponível (`IsAvailableAsync` falso) ⇒ prossegue sem lock (fail-open)**. Envio falhou (retorno falso ou exceção) ⇒ apaga a chave (retry no ciclo seguinte); sucesso ⇒ mantém por 2 h. Liberar sem Redis não faz nada.
- **RN-014-14 Mensagem** (`send_event_alert`, literal, `\n` entre linhas): `⚠️ Faltam {minutos_antes} min: {summary}` + `\n🕒 {HH:mm}` + (se `location` não vazio) `\n📍 {location}`. `HH:mm` é a hora **escrita no texto** `start` (sem converter de fuso; `2024-01-15T14:30:00-03:00` ⇒ `14:30`); se o texto não for ISO, usa o texto cru. Enviada por `IBotClient.SendTextAsync(numero_whatsapp, mensagem)`; exceção ⇒ falso.
- **RN-014-15 Processamento por usuário** (`process_alerts_for_user`): para cada evento, na ordem: claim, envio, conta se enviou; exceção em um evento não para os outros; devolve o número de alertas enviados; qualquer exceção inesperada ⇒ 0.
- **RN-014-16 Não portado:** `_notify_calendar_disconnected` (aviso semanal de Google desconectado, chave `calendar_disconnected_alert:{id}`) **nunca é chamado** no Flask (código morto; o aviso de token revogado, com outro texto e outra chave, já é enviado pela 010d). Q-014-5.

O laço do job (ler os usuários, chamar o serviço, somar) e o agendamento são da **013**.

## 5. Critérios de aceite

Cada `AC-014-NN` vira ao menos um teste com `[Trait("AC","014-NN")]`.

| ID | Dado / Quando / Então |
|---|---|
| AC-014-01 | Dado qualquer das 4 rotas sem token, com formato inválido ou token inválido, então 401 envelope A com a mensagem exata da spec 001 e nada é gravado |
| AC-014-02 | Dado token do usuário A e `<id>` do usuário B nas 4 rotas, então 403 `Acesso negado` e a config de B não muda |
| AC-014-03 | Dado `<id>` não numérico ou maior que `int.MaxValue`, então 404 (HTML do Flask); método errado (ex. GET em `/ativar`) dá 405 |
| AC-014-10 | Dado usuário sem linha, `GET config`, então 200 com `false`/`1` e a linha existe no banco com o padrão |
| AC-014-11 | Dado linha `true`/15, `GET config`, então 200 com `true`/15 e nenhuma outra linha criada |
| AC-014-12 | Dado falha do repositório, `GET config`, então 500 `Erro ao obter configuração de alertas` sem o texto da exceção |
| AC-014-20 | Dado corpo ausente, `{}`, `[]`, texto inválido, `{"ativo":null,"minutos_antes":null}` ou só chaves desconhecidas, `POST config`, então 400 `Nenhum parâmetro fornecido. Use 'ativo' e/ou 'minutos_antes'` |
| AC-014-21 | Dado `{"ativo":true}` e Google não conectado, então 400 `Você precisa conectar...`, sem `get_or_create` nem `UPDATE` |
| AC-014-22 | Dado `{"ativo":true}` e Google conectado, então 200 `Alertas de tarefas ativados`, `config` `true`/minutos atuais e `updated_at` renovado |
| AC-014-23 | Dado `{"ativo":false}` sem Google conectado, então 200 `Alertas de tarefas desativados` (sem consulta ao Google) |
| AC-014-24 | Dado `{"minutos_antes":5}`, então 200 `alerta configurado para 5 minuto(s) antes`, `ativo` inalterado, sem consulta ao Google |
| AC-014-25 | Dado `{"ativo":true,"minutos_antes":10}` conectado, então 200 `Alertas de tarefas ativados e alerta configurado para 10 minuto(s) antes` |
| AC-014-26 | Dado `minutos_antes` 0, 61, -1, `5.5`, `5.0`, `"5"`, `true`, `[]`, então 400 `minutos_antes deve ser um número entre 1 e 60`; a linha padrão existe e nada mudou; 1 e 60 são aceitos |
| AC-014-27 | Dado `{"ativo":"abc"}`, `{"ativo":1}` ou `{"ativo":[]}` (e Google conectado quando truthy), então 400 `Erro ao atualizar configuração` e nada muda (divergência, §9) |
| AC-014-28 | Dado `{"ativo":0}` ou `{"ativo":""}` com `minutos_antes` válido, então a consulta ao Google não acontece (falsy) e o resultado segue o passo 5 |
| AC-014-29 | Dado falha do `UPDATE`, então 400 `Erro ao atualizar configuração`; falha da consulta ao Google, então 500 `Erro ao atualizar configuração de alertas`; nenhum texto de exceção no corpo nem número de WhatsApp no log |
| AC-014-30 | `POST ativar` sem Google conectado ⇒ 400 `Você precisa conectar...` (o corpo não é olhado); conectado e sem corpo ⇒ 200 `Alertas de tarefas ativados` mantendo os minutos |
| AC-014-31 | `POST ativar` com `{"minutos_antes":7}` ⇒ 200 `Alertas de tarefas ativados e alerta configurado para 7 minuto(s) antes`; com `{"minutos_antes":0}` ⇒ 400 da faixa |
| AC-014-32 | `POST desativar` ⇒ 200 `Alertas de tarefas desativados`, `ativo=false`, minutos preservados, sem consulta ao Google, cria a linha se faltar |
| AC-014-33 | Falhas inesperadas de `ativar`/`desativar` ⇒ 500 com o texto fixo da rota (§3.3, §3.4) |
| AC-014-40 | Janela: para `agora = 17:59:20` e `minutos_antes = 1`, `timeMin = 17:59:50` e `timeMax = 18:00:50` (±30 s em torno de 18:00:20); com 60 minutos soma 60 min; usa o fuso de São Paulo (relógio fixo em UTC) |
| AC-014-41 | Só calendários selecionados são consultados; evento de dia inteiro e evento com `start <= agora` são ignorados; o `Sem título` é preservado; vários calendários somam |
| AC-014-42 | Erro em um calendário não impede os outros; erro ao listar calendários, usuário sem Google ou exceção qualquer ⇒ lista vazia, sem lançar |
| AC-014-43 | Claim: chave `calendar_alert:<md5>` igual à do Python, valor `processing`, TTL 7200 s; segundo ciclo com o mesmo evento não reenvia |
| AC-014-44 | Redis indisponível ⇒ envia sem lock; falha de envio apaga a chave e o ciclo seguinte reenvia; com Redis indisponível a liberação não faz nada |
| AC-014-45 | Mensagem exata com e sem `location` (`⚠️ Faltam 1 min: Reunião\n🕒 18:00\n📍 Sala 2`); `HH:mm` do texto do evento (`-03:00`, `Z`, `+05:30` mantêm a hora escrita); texto não ISO volta cru |
| AC-014-46 | `ProcessAlertsForUser`: conta só os enviados; exceção do bot em um evento libera o claim e segue para o próximo; falha geral ⇒ 0; envia ao `numero_whatsapp` informado |
| AC-014-47 | Usuários ativos: inclui `needs_reconnect` falso e nulo; exclui `true`, sem linha de token e `alertas_tarefas_ativo = false` (PostgreSQL real) |
| AC-014-50 | Isolamento: A ativa/desativa/altera a sua config sem tocar na de B; `calendaralertconfigs.usuario_id` é UNIQUE (uma linha por usuário, também sob `get_or_create` concorrente) |

## 6. Dados

`calendaralertconfigs` (já na baseline): `usuario_id` UNIQUE com FK `CASCADE`, `alertas_tarefas_ativo` bool default `false`, `minutos_antes` int default `1` com `chk_minutos_antes` (1–60), `created_at`/`updated_at` timestamptz. **Sem trigger** de `updated_at`: o código grava. Lê também `usuarios` e `googlecalendartokens` (RN-014-10). **Sem migration.** Redis: `calendar_alert:{md5}` (já documentada em `integrations.md`; **manter na virada**, senão alertas seriam reenviados).

## 7. Desvios aprovados e como se aplicam

Ver a tabela de abertura. Implementados nesta feature: **003** (JWT e id igual ao token), **004** (`get_or_create`), **008**, **024**. **005** já estava implementado pela 009-0 (nada a registrar além das rotas). Não aplicáveis: os demais.

## 8. Decisões de desenho

- Rotas escritas só para o usuário do token: o caso de uso recebe `ICurrentUser.UserId` e o `<id>` serve para o 403.
- `CalendarAlertService` vive em Application, sem HTTP; fala só com `ICalendarClient`, `ICache`, `IBotClient`, `IClock` e o repositório.
- Domain puro: janela, mensagem, hora, validação de `minutos_antes` e montagem da mensagem de sucesso.
- O Flask só testa o Google antes de ler o corpo em `ativar`; em `config` o corpo vem primeiro: reproduzido.

## 9. Desvios propostos (não implementados; sem aprovação o .NET reproduz o Flask)

| Id | Atual (arquivo:linha) | Proposta | Recomendação |
|---|---|---|---|
| **120** Tipos do corpo de `/config` e `/ativar` | `calendar_alerts.py:51-52`, `calendar_alert_config_service.py:36-60`: `ativo` vira parâmetro SQL sem checar o tipo (`"true"` funciona, `1` e texto qualquer dão 400 com `str(e)`, e a mensagem diz "ativados" mesmo com `"false"`); `minutos_antes: true` passa no `isinstance(int)` e falha no banco | `ativo` só `true`/`false` e `minutos_antes` só inteiro 1–60, cada um com mensagem própria (`ativo deve ser true ou false`) | **Aprovar** (hoje o .NET responde `Erro ao atualizar configuração` para `ativo` não booleano, e `"true"`/`"false"` como texto deixam de funcionar) |
| **121** Janela de ±30 s perde alerta se o ciclo atrasa | `calendar_alert_service.py:121-129`: o job roda a cada minuto e cada ciclo vê só `[alvo ± 30 s]`; ciclo atrasado, job fora do ar por um minuto ou Google lento fazem o evento passar sem aviso | janela `(agora + minutos − 60 s, agora + minutos + 30 s]`, deixando o claim de 2 h (RN-014-13) evitar o aviso repetido | **Aprovar** com cuidado: com Redis fora (fail-open) pode haver aviso duplicado |
| **122** Usuário inativo ainda recebe alertas | `calendar_alert_config_service.py:96-104`: a consulta não filtra `usuarios.ativo` | acrescentar `AND u.ativo = true` | **Aprovar** |

## 10. Como ficou na implementação

1. **Paridade com os desvios aprovados 003, 004, 008 e 024; 120 a 122 seguem `proposto` e nenhum foi implementado.** O único ponto em que o .NET difere do Flask sem aprovação é `ativo` não booleano (Q-014-3): 400 de texto fixo em vez de aceitar `"true"`/`"false"`.
2. **Rotas:** 4 rotas sob `MapGroup("/calendar-alerts")` com `RequireUser`; `<id>` conferido com o token (404 acima de `int.MaxValue`, 403 se diferente); `GET`/`POST` errados dão 405 do roteamento.
3. **Serviço:** `CalendarAlertService` (Application) usa só as portas da 010 (`ICalendarClient.ListSelectedCalendarsAsync` + `ListEventsAsync`, `ICache`, `IBotClient`, `IClock`); a falha do calendário ou do Google nunca sobe. `ICalendarAlertDiagnostics` (Application) é implementado na Infrastructure com `ILogger` (EventIds 14001 a 14006); o módulo Api usa 14010 e 14011.
4. **Tabela sem trigger:** o `UPDATE` grava `updated_at = CURRENT_TIMESTAMP`; `get_or_create` é `INSERT ... ON CONFLICT DO NOTHING` + `SELECT` (seguro sob concorrência, testado com 20 chamadas).
5. **`needs_reconnect` é `NOT NULL`** na baseline; o `IS NULL` da consulta foi mantido por paridade (nunca casa).
6. **Testes:** 67 unitários (Domain e Application) e 58 de integração (PostgreSQL real, Redis real para o claim), todos com `[Trait("AC","014-NN")]`. Sem arquivo compartilhado alterado, exceto as linhas novas no fim de `parity-deviations/README.md`.
7. **Não portado:** laço do job (013) e `_notify_calendar_disconnected` (código morto, Q-014-5).

## 11. O que o Angular e o bot esperam

Nenhum cliente conhecido usa estas rotas (ver "Consumida por"). Se o Angular passar a ter uma tela de alertas de tarefas, ele envia o JWT e usa o `user_id` do próprio token no caminho.

## 12. Fora de escopo

- O job (laço, `Ofelia`, `BaseJob`, lock `job_lock:*`): **013**.
- `CalendarQueryService` e `CalendarManagementService` (consultas e CRUD de agenda pelo WhatsApp): **012**.
- Aviso semanal de "Google desconectado" (código morto, RN-014-16).

## 13. Perguntas em aberto

- **Q-014-1** O 401 de `RequireUser` sai no envelope A (`status`/`message`), mas as demais respostas da rota são B (`status`/`mensagem`). Implementado: **A** (reuso do filtro, igual às outras rotas JWT). Trocar para B exige filtro novo; nenhum cliente conhecido chama as rotas.
- **Q-014-2** Textos do 403 (`Acesso negado`) e dos quatro 500 (`Erro ao obter configuração de alertas`, `Erro ao atualizar configuração de alertas`, `Erro ao ativar alertas de tarefas`, `Erro ao desativar alertas de tarefas`): inventados, já que o Flask devolvia `str(e)`. Confirmar ou trocar.
- **Q-014-3** `ativo` não booleano: o Flask aceita `"true"`/`"false"` (via PostgreSQL) e responde 400 com `str(e)` nos demais. Implementado: 400 `Erro ao atualizar configuração` para tudo que não for `true`/`false`. Ver 120.
- **Q-014-4** `get_users_with_alerts_active` não filtra usuário inativo nem sem `numero_whatsapp`. Implementado como o Flask (122 propõe filtrar `ativo`).
- **Q-014-5** `_notify_calendar_disconnected` é código morto. Portar (e chamar de onde?) ou descartar? Implementado: **não portado**.
- **Q-014-6** `NaN`/`Infinity` e inteiros acima de 64 bits em `minutos_antes`: o Flask aceita o literal e cai na mensagem de faixa; o .NET responde 400 da faixa para o inteiro gigante e trata `NaN` como corpo inválido (⇒ `Nenhum parâmetro fornecido`). Borda, sem proposta.
- **Q-014-7** A janela usa `start > agora` com a precisão do relógio do .NET; o Python usa microssegundos. Sem efeito prático.
- **Q-014-8** Algum atalho de iPhone/Android chama `/calendar-alerts/*`? Não há como verificar no código; com o desvio 003 eles passariam a receber 401 sem JWT.
