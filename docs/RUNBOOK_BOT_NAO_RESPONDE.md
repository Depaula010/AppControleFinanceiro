# Runbook: o bot do WhatsApp parou de responder

Escrito depois do incidente de 26 e 27/10/2026, em que o bot ficou mudo por **três causas diferentes e encadeadas** (saldo do Gemini, modelo desativado e chave pessoal com prioridade; depois chaves do Signal corrompidas no bot). A regra do roteiro: **descubra em qual elo da cadeia a mensagem morre, antes de mexer em qualquer coisa**.

```
seu WhatsApp → bot (whatsapp-saas-bot) → API (/webhook-whatsapp) → Gemini → resposta → bot → seu WhatsApp
```

Nada aqui exige apagar dados. As ações que mexem no estado (reconectar, re-parear) estão separadas, no fim, e dizem quando usar.

## 0. Antes de tudo

- **Segredos não vão para o chat nem para arquivo versionado.** Nos comandos abaixo a chave do bot vem de uma variável de ambiente (`BOT_API_KEY`, a do tenant `meu-secretario`, em `/root/ms-bot.key` no servidor) e nunca é impressa.
- Servidor compartilhado: não reinicie o container do bot sem checar `SELECT session_name, status FROM sessions` no banco `whatsapp_saas` (outras sessões dependem dele).
- A sessão principal tem o id `20fd23cc-d903-4b1c-902c-d7b88d2a3131` (`sessao-principal`, número 55 31 …2385).

## 1. O bot recebe a mensagem? (lado do bot, só leitura)

O bot tem três consultas que **não alteram nada** e dizem muito mais que o log dele (que fica em nível `warn`):

```bash
S=20fd23cc-d903-4b1c-902c-d7b88d2a3131
B=http://212.47.65.37:3001/api/v1/sessions/$S
curl -s -H "X-API-Key: $BOT_API_KEY" $B                 # status (connected?) e last_connected_at
curl -s -H "X-API-Key: $BOT_API_KEY" $B/stats           # messages_received, webhook_errors, circuit_breaker_trips, uptime_seconds
curl -s -H "X-API-Key: $BOT_API_KEY" "$B/logs?limit=10" # eventos: message_failed, circuit_breaker_open, session_failed...
```

Mande uma mensagem de teste e releia o `stats`:

| O que você vê | Significa |
|---|---|
| `messages_received` **sobe** | O bot recebeu. O problema está adiante (passo 2). |
| `messages_received` **não sobe** | O bot não está recebendo (passo 3). |
| `status` diferente de `connected` | Sessão caída. Veja o passo 3. |

No `/logs`, cada `message_failed` traz o `httpStatus` que a **API devolveu** ao bot:

| `httpStatus` | Causa provável |
|---|---|
| **429** | A API recusou por cota/saldo do **Gemini** (passo 4), ou limite de taxa. |
| **401** | `x-api-key` do webhook diferente do `API_SECRET_KEY` da API (`sessions.metadata.webhook_auth_token`). |
| **500** | Exceção na API: veja o log dela (passo 2). Modelo do Gemini desativado dá 500. |
| ausente, com `attempts: 4` | A API estava fora do ar ou inalcançável. |
| `circuit_breaker_open` | O bot abriu o circuito (3 falhas em 60 s) e **recusa mensagens por uns 2 minutos**, respondendo "Estamos com instabilidade". Espere, não reenvie em sequência (cada tentativa pode reiniciar a contagem). |

## 2. A API recebeu? (lado do backend)

```bash
docker logs --since 15m meu-secretario-api 2>&1 | grep -v -E "RESUMO-MATINAL|ALERTAS-TAREFAS|CHECKIN-NOTURNO|Quartz"
```

Procure `Webhook do WhatsApp recebido`. **Se não há nenhuma linha, a mensagem não chegou à API** (volte ao passo 1). O filtro com `grep -v` importa: os jobs escrevem uma linha por minuto e escondem o resto num `tail`.

Se o aviso operacional estiver ligado (`OPS_ALERT_WHATSAPP`), as falhas do Gemini chegam por WhatsApp sozinhas, com a causa e o que fazer (desvio 266).

## 3. O bot não recebe (sessão caída ou degradada)

```bash
docker logs --since 40m whatsapp-saas-bot 2>&1 | grep -E "Connection update|stream errored|statusCode|conflict|replaced" | tail -15
docker logs --since 6m whatsapp-saas-bot 2>&1 | grep "failed to decrypt" | grep -o -E '"remoteJid":"[^"]*"|"message":"[^"]*"' | sort | uniq -c
```

- **`close` com 428/503 repetindo, ou `Timed Out` em `init queries` logo depois de abrir:** a conexão está instável. Em 27/10 a causa foi o armazenamento das chaves do Signal do bot (corrigido no repositório do bot, PR #1); depois da correção, só um estado limpo resolveu.
- **`failed to decrypt` com `Invalid PreKey ID` / `No session record` / `Bad MAC`:** o estado das chaves não bate com o dos aparelhos.
- **`loggedOut` / 401 / `device_removed`:** o WhatsApp desvinculou o aparelho. Precisa re-parear.

**Ações, da mais leve para a mais pesada** (peça confirmação antes da última):

1. **Reconectar** (`POST $B/reconnect`): refaz a conexão com as mesmas credenciais. Não apaga nada. Resolve queda simples.
2. **Re-parear:** só se a reconexão não resolver.
   1. Backup (HeidiSQL, banco `whatsapp_saas`): `CREATE TABLE baileys_auth_bkp_AAAAMMDD AS SELECT * FROM baileys_auth WHERE session_uuid = '<id da sessão>';`
   2. `POST $B/logout` (limpa só as credenciais dessa sessão) e `POST $B/connect`.
   3. `GET $B/qr` devolve o QR como imagem (data URL); salve como PNG e escaneie no celular do 2385 (Configurações → Aparelhos conectados → Conectar um aparelho). Expira em ~1 minuto.
   4. Não reinicie o container. A sessão sobe sozinha depois do QR.

## 4. A API devolve 429 ou 500 (Gemini)

Teste a chave **do servidor** sem imprimir a chave:

```bash
set -a; . <(grep '^GEMINI_API_KEY=' /opt/meu-secretario/.env); set +a
curl -s "https://generativelanguage.googleapis.com/v1beta/models/<MODELO>:generateContent" \
  -H "x-goog-api-key: $GEMINI_API_KEY" -H "Content-Type: application/json" \
  -d '{"contents":[{"parts":[{"text":"oi"}]}]}' | head -c 600
```

| Resposta | O que fazer |
|---|---|
| `RESOURCE_EXHAUSTED` | Cota ou **saldo**. A conta de faturamento pode exigir pré-pagamento (Google AI Studio → Faturamento). Sem saldo, todas as chamadas falham. |
| `404 ... no longer available to new users` | O **modelo** foi desativado para chaves novas. Troque sem deploy: `UPDATE configuracoes SET valor = '<modelo>' WHERE chave = 'gemini.modelo';` (vale em até 60 s). |
| `403 / PERMISSION_DENIED` | Chave errada, revogada ou sem a API habilitada no projeto. |
| `"candidates"` com texto | A chave do servidor está boa: o problema é a chave **que a API escolhe** (abaixo). |

**Armadilha: a chave pessoal tem prioridade.** Se o usuário tem uma chave Gemini cadastrada (`chavesapiusuario`) e `preferenciaschaveapi.usar_chave_propria = true` para `gemini`, a API usa **essa** chave e ignora o `GEMINI_API_KEY` do servidor. Trocar a do servidor não adianta. Confira (só leitura):

```sql
SELECT usuario_id, provedor, ativo FROM chavesapiusuario WHERE provedor = 'gemini';
SELECT usuario_id, provedor, usar_chave_propria FROM preferenciaschaveapi WHERE provedor = 'gemini';
```

Para usar a do servidor: `UPDATE preferenciaschaveapi SET usar_chave_propria = FALSE, atualizado_em = NOW() WHERE usuario_id = <id> AND provedor = 'gemini';` (confira antes o limite do plano em `planos.limite_gemini`).

**Trocar a `GEMINI_API_KEY` do servidor:** atualize o **segredo do GitHub** (Settings → Secrets and variables → Actions) **e** recrie a API (`cd /opt/meu-secretario && docker compose up -d --force-recreate web`). O deploy regrava o `.env` com o valor do GitHub; se só o `.env` do servidor for editado, o próximo deploy devolve a chave antiga.

## 5. O que continua funcionando sem o Gemini

Passos determinísticos, que não chamam o Gemini: **"menu" / "ajuda"** (desvio 265), as respostas do **check-in noturno** ("ok", números, "depois", o valor de um lembrete, a conta que pagou a fatura), os **relatórios sob comando** e a **confirmação/cancelamento** de lançamentos pendentes. Texto livre ("gastei 21 reais na padaria", "tenho compromisso amanhã?") precisa do Gemini. Se o menu responde e o texto livre não, o problema é o Gemini (passo 4).

## 6. Armadilhas conhecidas

- **Formato das chaves do Signal no bot:** `baileys_auth.data_key = key-<categoria>-<id>`, com categorias como `pre-key`, `session`, `sender-key` (o id pode ter `.`, `-` e `@`, ex. `174350003089528.0`). O leitor do bot (`keyStoreLayout.js`) reconhece a categoria pelo prefixo. **Não separe por `-` ou `.` às cegas**: foi isso que corrompeu o estado do bot em 26/10 (corrigido; há teste de regressão).
- **Servidor compartilhado:** nunca `docker system prune`, nunca `down`/`rm` em container de outro projeto. O deploy do Meu Secretário e o do bot constroem **antes** de recriar.
- **Aparelhos vinculados:** o celular principal (do número 2385) precisa abrir o WhatsApp de vez em quando; o bot é um aparelho vinculado.

## 7. Depois de resolver

- Releia `/stats` e o `/logs` do bot, e mande uma mensagem de cada tipo (menu, texto livre, "ok" de um check-in).
- Confirme em algumas horas: `docker logs --since 6h whatsapp-saas-bot 2>&1 | grep -c -E "Invalid PreKey|No session record"` e `... | grep -c "Connection update.*close"` devem ficar em **zero ou perto disso**.
