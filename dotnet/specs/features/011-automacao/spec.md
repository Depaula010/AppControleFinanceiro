# Spec 011 — Automação (atalho do iPhone, Automate, SMS, reserva de emergência, reprocessar fatura)

- **Estado:** aprovada por delegação do Rafael em 2026-10-05 e **implementada** na mesma data (fluxo SDD delegado ao agente; valem só paridade com o Flask e os desvios já aprovados; os desvios **096 a 101** (aprovados em 2026-10-09) foram implementados na mesma data; o 102 foi **adiado** e não é implementado)
- **Onda:** 3
- **Origem (Python):** `app/routes/webhooks/__init__.py:59-75, 103-188` (rotas), `app/routes/webhooks/handlers/transaction_handler.py:38-583` (`/webhook-automate`, `/api/transacao`, `/webhook-sms-payment`), `handlers/reserve_handler.py:21-345` (`PATCH` e `GET` da reserva), `app/services/finance_service.py:251-288` (`get_user_by_api_key`), `:297-407` (contas, categorias, nomes), `:409-425` (`create_transaction`), `:943-1013` (`get_reserva_status`), `app/services/finance/invoice_service.py:590-800` (`process_invoice_schedules`), `app/services/fixed_bills_service.py:80-215, 507-515` (casamento difuso e quitação), `app/services/transaction_confirmation_service.py:9-230` (pendência no Redis e mensagem), `app/services/finance/budget_validation_service.py:1-313` (pote), `app/services/gemini_service.py:94-173, 376-420` (prompts de extração e categoria), `app/shared/validators/input_sanitizer.py`, `app/shared/formatters/currency_formatter.py`.
- **Depende de:** 001a (`IEncryptionService`, `IClock`), 003 (`ICategoryLookup`), 004 (`ITransactionPosting`, `IInvoiceService`, `IInvoiceSchedulePopulator`, `InvoiceLinking`), 005 (`IEmergencyReserveService`), 008 (`IApiKeyValidator`/`RequireApiKey`; `IApiKeyProvider` via 010-0), 010 (`ILlmClient`, `ICache`, `IBotClient`, `IIntegrationSettings`).
- **Desvios aplicáveis (aprovados):** 003 (`reprocessar` exige `x-api-key`), 005 (rotas alcançáveis: whitelist derivada), 008 (sem `str(e)` nem segredo em resposta ou log), 014 (reserva única, via `IEmergencyReserveService`), 015 (período do pote), 017 (chave do usuário em texto puro ou Fernet), 024 (corpo sem `Content-Type` JSON), 025 (`Cartão de Crédito` com acento), 030 (`Outros` por `ORDER BY id`), 039 (só contas ativas). Implementados em 2026-10-09: 096 a 101 (§9). Adiado: 102.
- **Consumidores:** os atalhos do iPhone (`/api/transacao`, `/webhook-sms-payment`), o Automate Android (`/webhook-automate`), scripts de reserva (`/api/agendamento*/reserva`) e a manutenção (`reprocessar`). O Angular **não** usa nenhuma destas rotas.

## 1. Objetivo

Seis rotas para clientes que não têm JWT: o usuário se identifica pela **chave do atalho** (`user_api_key`, a `api_key_automate` do cadastro) e o sistema registra uma despesa (com categoria sugerida pelo Gemini e confirmação pelo WhatsApp), quita uma conta fixa por SMS, liga ou desliga um agendamento da reserva de emergência, lista os agendamentos da reserva e reprocessa os lançamentos de uma fatura. Todas gravam só os dados do dono da chave.

Esta feature **não** escreve SQL de `transacoes` nem de `faturas` por conta própria: lança pela porta `ITransactionPosting` e `IInvoiceService` da 004 e calcula a reserva pela `IEmergencyReserveService` da 005. O que é novo e fica aqui: autenticação por chave do usuário, casamento difuso de conta fixa (`rapidfuzz`), validação de pote, pendência de confirmação no Redis, populador real de faturas (`process_invoice_schedules`) e as seis rotas.

## 2. Rotas

| Método | Caminho | Auth | Envelope |
|---|---|---|---|
| POST | `/webhook-automate` | `user_api_key` no corpo | B (`sucesso`/`erro`, `mensagem`) |
| POST | `/api/transacao` | `user_api_key` no corpo | exceção do contrato (§3.2): sucesso `success`/`message`; erro `erro`/`mensagem` ou `{"erro"}` |
| POST | `/webhook-sms-payment` | `user_api_key` no corpo | B; o 503 usa `resposta` |
| PATCH | `/api/agendamento/<int:id>/reserva` | `api_key` no corpo | B |
| GET | `/api/agendamentos/reserva` | `api_key` na query | B |
| POST | `/api/fatura/<int:id>/reprocessar` | `x-api-key` (desvio 003) | B |

Como em todo o contrato: sem barra final (404 HTML), `<int:id>` não numérico ou negativo ⇒ 404 antes de qualquer outra coisa, método não permitido ⇒ 405. As cinco primeiras **não** passam por `RequireUser` (não há JWT). Nenhuma rota é desligada por configuração. A whitelist de segurança é derivada das rotas registradas (desvio 005), logo todas são alcançáveis por IP público.

## 3. Contrato por rota

Convenções: "falso" é a verdade do Python (`null`, `false`, `0`, `0.0`, `""`, `[]`, `{}`); "texto" é string JSON; chaves de resposta na ordem escrita; dinheiro como número JSON (`100` no .NET, `100.0` no Flask, igual nas demais features); textos em português **sem acento** onde o Flask não os usa (`"API key invalida"`, `"Dados faltando"`), copiados literalmente. Corpo ausente, vazio, sem `Content-Type`, inválido ou que não seja objeto é tratado como `{}` (desvio 024): cai na validação de campos obrigatórios da rota, nunca 500. Chaves desconhecidas são ignoradas. O 500 devolve **sempre** a mensagem fixa da rota (desvio 008), nunca `str(e)`.

### 3.0 Autenticação pela chave do usuário (`get_user_by_api_key`)

Origem: `finance_service.py:251-288`. Lê `usuarios` com `api_key_automate IS NOT NULL` e compara cada uma com a chave recebida: se o valor guardado parece cifrado (`gAAAAA`, ≥ 40 caracteres), decifra com a `ENCRYPTION_KEY` e compara (falha de decifração ⇒ ignora e segue); senão compara o texto puro (a chave do usuário atual está em texto puro, desvio 017). Devolve `(usuario_id, numero_whatsapp)` do primeiro que casar; ninguém casou ⇒ 401 na rota. A comparação é em tempo constante (melhoria invisível). Não filtra `usuarios.ativo` (como o Flask). A chave só vale se for **texto**: número, booleano ou lista nunca casam (o Python compara `str == int`).

### 3.1 `POST /webhook-automate`

Origem: `transaction_handler.py:38-173`. Corpo: `texto` (texto da notificação do banco) e `user_api_key`.

Ordem:

| # | Condição | Resposta |
|---|---|---|
| 1 | `GEMINI_API_KEY` do sistema vazia (o Flask não cria o modelo e `gemini_model` é nulo) | 503 `{"status":"erro","mensagem":"Servico nao configurado"}` |
| 2 | `texto` ou `user_api_key` falso | 400 `"Dados faltando"` |
| 3 | nenhuma chave casa | 401 `"API key invalida"` |
| 4 | `texto` verdadeiro mas não texto (`5`, `[1]`) | 400 `"Campo 'texto' deve ser texto"` (desvio 097; o Flask quebrava em `.lower()`, 500) |

Passos (depois da autenticação):

1. **Extração (Gemini).** Prompt `extract_from_notification` (§7.4) com `UserId` (a chave do usuário vale, desvio 063 já aprovado para chamadas com usuário; o Flask já passa `usuario_id` aqui) e **sem cache**. A resposta é `LlmJson.Parse` ⇒ objeto com `tipo_fluxo` (padrão `"Despesa"`), `descricao_bruta` (padrão `null`) e `valor_decimal` (`float()`, padrão `0`). Resposta que não é objeto, JSON inválido ou `descricao_bruta` que não é texto ⇒ 500 (falha do Gemini). **Desvio 097:** `tipo_fluxo` fora de `Renda`/`Despesa` ⇒ 400 `"tipo_fluxo invalido na notificacao. Use: Renda ou Despesa"`; `valor_decimal` ausente, `null`, não numérico, zero, negativo, `nan` ou acima de 1 bilhão ⇒ 400 `"Valor invalido na notificacao: deve ser positivo"` (o Flask respondia 500 ou criava pendência de R$ 0,00). Nada é gravado. Falha do Gemini: `LlmKeyUnavailableException` ⇒ 500 com a mensagem literal dela (o Flask mostra o texto ao usuário, ver RN-011-05); cota ⇒ 500 `"429 Quota exceeded - Gemini API rate limit reached"`; permissão ⇒ 500 `"403 Permission denied - Invalid API key or permissions"`; outras ⇒ mensagem fixa da rota.
2. **Conta.** Contas **ativas** do usuário (`id, nome_conta, tipo_conta`, `ORDER BY id`; desvio 039). Se `tipo_fluxo == "Despesa"` e o `texto` em minúsculas contém `cartao`, `compra` ou `credit`, usa o primeiro `Cartão de Crédito` (desvio 025: o Flask comparava `Cartao de Credito` sem acento e nunca achava). Se não achou, usa a primeira `Conta Corrente`; se não há, a primeira conta da lista; lista vazia ⇒ 400 `"Nenhuma conta ativa encontrada. Cadastre uma conta antes de usar o atalho."` (desvio 097; o Flask quebrava com `IndexError`).
3. **Categoria.** Categorias visíveis do usuário para o tipo (`Despesa` ⇒ grupos `Despesa Essencial`, `Despesa Discricionária`, `Meta Financeira`, `Geral`; qualquer outro tipo ⇒ grupo `Renda`; globais ou do usuário, `ORDER BY s.id`), "Outros" de fallback (`ICategoryLookup.FindFallbackAsync(income: tipo == "Renda")`, desvio 030) e `categorize_transaction` (§7.4, cache `category` de 30 dias por usuário). Resposta que não é um id da lista, não numérica, vazia ou bloqueada ⇒ id do fallback.
4. **Fatura.** Se a conta escolhida é `Cartão de Crédito`: `IInvoiceService.EnsureCurrentAsync(usuário, conta)`, depois a fatura do ciclo de **hoje** (`IClock.Today`, `InvoiceLinking`), criada se faltar, **comitada antes** de seguir (o Flask faz `conn.commit()` aqui). `fatura_id` é `null` nos demais casos e para cartão sem os dois dias.
5. **Valor.** `valor_db = valor` se `tipo_fluxo == "Renda"`, senão `-valor`.
6. **Redis indisponível** (`ICache.IsAvailableAsync` falso): lança direto por `ITransactionPosting.PostSimpleAsync` (tipo `Renda` ⇒ `Income`, qualquer outro ⇒ `Expense`; origem `null`, Q-004-3) e avisa o WhatsApp `"Transacao salva!\n\nDescricao: {descricao}\nValor: {R$}\nCategoria: {Macro -> Sub}"`; resposta **200** `{"status":"sucesso"}`. Categoria desconhecida: `"Categoria Desconhecida"`.
7. **Pendência.** `IPendingTransactionStore.CreateAsync(whatsapp, dados)` grava `pending_tx:{whatsapp}:{id}` (JSON, TTL 300 s) e `last_pending:{whatsapp}` (o `id`, TTL 300 s); o `id` são os 8 primeiros caracteres de um UUID v4 (minúsculas). Dados, na ordem: `usuario_id`, `conta_id`, `categoria_id`, `fatura_id`, `descricao`, `valor_db`, `valor_original`, `tipo_transacao`, `data_transacao` (`yyyy-MM-dd`), `origem` (`"automate"`). Se gravar falhou (sem `id`): lança direto como no item 6, **sem** avisar o WhatsApp, resposta **200** `{"status":"sucesso"}`.
8. **Confirmação.** Envia ao WhatsApp a mensagem de §7.5 (o retorno do bot é ignorado) e responde **200** `{"status":"sucesso","mensagem":"Aguardando confirmacao do usuario","transaction_id":"<id>"}`.

Erro inesperado: **500** `{"status":"erro","mensagem":"Erro ao processar a notificação. Tente novamente."}`.

### 3.2 `POST /api/transacao`

Origem: `transaction_handler.py:175-452`. Atalho do iPhone: `user_api_key`, `valor`, `local`, `conta`, `tipo_pagamento` (obrigatórios) e `descricao` (opcional).

Ordem:

| # | Condição | Resposta |
|---|---|---|
| 1 | `GEMINI_API_KEY` do sistema vazia | 503 `{"status":"erro","mensagem":"Servico nao configurado"}` |
| 2 | corpo ausente, sem `Content-Type` JSON, inválido, `null` ou que não seja objeto (desvio 024) | 400 `{"erro":"JSON invalido ou ausente"}` (**sem** `status`) |
| 3 | campos de texto verdadeiros que não são texto (`local: 5`, `tipo_pagamento: [1]`) | 400 `"Campo '<campo>' deve ser texto"`, para o primeiro na ordem `local`, `descricao`, `conta`, `tipo_pagamento` (desvio 097; o Flask quebrava em `sanitize_input`, 500) |
| 4 | algum obrigatório falso, **depois** de sanear os textos (§7.2) | 400 `"Campos obrigatorios faltando: a, b"`, na ordem `user_api_key`, `valor`, `local`, `conta`, `tipo_pagamento` |
| 5 | `valor` não conversível por `float()` | 400 `"Valor invalido: <erro do Python>"` (§7.1) |
| 6 | `valor <= 0` | 400 `"Valor invalido: Valor deve ser positivo"` |
| 7 | `valor > 1000000000` | 400 `"Valor invalido: Valor muito alto (max: 1 bilhao)"` |
| 8 | `tipo_pagamento` normalizado fora de `credito`, `debito`, `pix`, `dinheiro` | 400 `"tipo_pagamento invalido. Use: credito, debito, pix, dinheiro"` |
| 9 | nenhuma chave casa | 401 `"API key invalida"` |
| 10 | conta não encontrada (§7.3) | 400 `"Conta '<conta>' nao encontrada. Verifique o nome exato."` (o nome já saneado) |

Tratamento: `local` (≤ 100), `conta` (≤ 50), `descricao` (≤ 200, aceita caracteres especiais; `trim`; vazia ⇒ `null`) e `tipo_pagamento` (≤ 20, `trim`, minúsculas, sem acentos por NFKD) passam por `sanitize_input` (§7.2). `valor` é `float` (texto numérico serve). O Flask aceitava `valor` `nan` (passava nas duas comparações e gravava `NaN`); desvio 097: `nan` responde o 400 do item 6.

Passos (depois da autenticação e da conta):

1. **Categoria.** Texto para a IA = `local` ou `"{local} - {descricao}"`; categorias de `Despesa` do usuário, fallback `Despesas Gerais`/`Outros`, `categorize_transaction` (como em §3.1, item 3).
2. **Fatura.** Se `tipo_pagamento == "credito"` (**qualquer** tipo de conta; o Flask não confere o tipo): `EnsureCurrentAsync` para a conta e a fatura do ciclo de hoje por `InvoiceLinking` (só devolve fatura se a conta é cartão com os dois dias; senão `null`), comitada antes de seguir.
3. **Pote (desvio 015).** `validate_budget` (§7.6) com a categoria, o valor positivo e hoje; `requer_confirmacao` ⇒ `budget_warning` = mensagem; senão `null`. **Não bloqueia.**
4. `descricao_final` = `local` ou `"{local} - {descricao}"`; `valor_db = -valor`; `origem` da pendência = `"api_endpoint"`.
5. **Redis indisponível:** lança direto (`PostSimple`, `Expense`, origem `null`) e avisa o WhatsApp `"Transacao salva!\n\n{descricao_final}\n{R$}\n{Macro -> Sub}"`; **200** `{"status":"success","message":"Transacao salva com sucesso (Redis indisponivel)","categoria_sugerida":"<Macro -> Sub>"}`.
6. **Pendência** com os dados da §7.5 (na ordem: `usuario_id`, `conta_id`, `conta_nome`, `conta_tipo`, `categoria_id`, `fatura_id`, `local`, `descricao`, `descricao_final`, `valor_db`, `valor_original`, `tipo_transacao` `"Despesa"`, `tipo_pagamento`, `data_transacao`, `origem` `"api_endpoint"`, `budget_warning`). Sem `id` (falha do Redis): lança direto (sem avisar o WhatsApp), **200** `{"status":"success","message":"Transacao salva (erro no Redis)","transaction_id":null}`.
7. `last_pending:{whatsapp}` é regravado com o `id` (300 s, redundante com a criação da pendência, mantido) e a mensagem de §7.5 vai ao WhatsApp.
8. **200** `{"status":"success","message":"Transacao pendente de confirmacao no WhatsApp","transaction_id":"<id>","categoria_sugerida":"<Macro -> Sub>","conta_utilizada":"<nome real da conta>","vinculado_fatura":<bool>}`.

Erro inesperado: **500** `{"status":"erro","mensagem":"Erro ao registrar a transação. Tente novamente."}`.

### 3.3 `POST /webhook-sms-payment`

Origem: `transaction_handler.py:454-583`. Atalho do iPhone por SMS: `user_api_key`, `descricao_pagamento`, `valor_pago`, `conta_pagamento` (obrigatórios) e `data_pagamento` (`YYYY-MM-DD`, padrão hoje).

Ordem:

| # | Condição | Resposta |
|---|---|---|
| 1 | banco indisponível (o `SELECT 1` falha) | 503 `{"status":"erro","resposta":"Banco de dados temporariamente indisponivel"}` |
| 2 | algum dos quatro obrigatórios falso | 400 `{"status":"erro","mensagem":"Dados faltando"}` |
| 3 | nenhuma chave casa | 401 `"API key invalida"` |
| 4 | `data_pagamento` verdadeira que não é `YYYY-MM-DD` (`date.fromisoformat` do Python 3.11 também aceita `YYYYMMDD`), ou não é texto | 400 `"data_pagamento invalida. Use o formato AAAA-MM-DD"` (desvio 097; o Flask respondia 500) |
| 5 | `descricao_pagamento` verdadeira que não é texto | 400 `"Campo 'descricao_pagamento' deve ser texto"` (desvio 097) |
| 6 | `valor_pago` que o `float()` recusa, `nan`, zero ou negativo (`0` já é "Dados faltando"), ou acima de 1 bilhão | 400 `"Valor invalido: <motivo>"` com os motivos da §7.1 e de `/api/transacao` (desvios 097 e 098; o Flask gravava despesa positiva para valor negativo e respondia 500 nos demais) |

(O SMS **não** verifica `GEMINI_API_KEY`: não há 503 "Servico nao configurado".)

Passos:

1. **Conta fixa.** `find_matching_bill` (§7.3): entre os agendamentos **ativos** `FIXO` ou `LEMBRETE_VARIAVEL` do usuário, o de descrição mais parecida com `descricao_pagamento` (artigos iniciais removidos, minúsculas e sem acentos nos dois lados, `WRatio` ≥ 65; desvio 099). 2. **Achou** ⇒ `settle_fixed_bill`: despesa de `float(valor_pago)` negativo, na conta **do agendamento** (não na `conta_pagamento`), subcategoria e descrição do agendamento, data do pagamento, sem `agendamento_id` (Q-011-3), via `PostSimple` (`Expense`, origem `null`); em cartão, a fatura é a do ciclo da data (a do `PostSimple`). Avisa o WhatsApp `"✅ *CONTA QUITADA!* ✅\n\n📝 {descricao}\n💰 {R$ do valor_pago}\n📊 {categoria}\n\n_Esta conta não será mais cobrada automaticamente este mês._"` e responde **200** `{"status":"sucesso","mensagem":"Conta fixa quitada com sucesso!","conta_identificada":"<descrição do agendamento>","transaction_id":<id>}`.
3. **Não achou** ⇒ despesa avulsa: **não chama** `extract_transaction_details` (o Flask chamava e ignorava o resultado, e a falha dele derrubava a rota; desvio 096); categoriza a `descricao_pagamento` (categorias de `Despesa`, fallback `Despesas Gerais`/`Outros`); conta = a de nome `Carteira` (exato, ou `ILIKE '%Carteira%'`, ou a primeira conta; só ativas, `ORDER BY id`); lança `-valor` (`PostSimple`) com a descrição `"{descricao_pagamento} ({conta_pagamento})"`, tipo `Despesa` (se a conta escolhida for cartão com os dois dias, o serviço da 004 vincula a fatura do ciclo; o Flask passava `fatura_id` nulo, ver §11); avisa o WhatsApp `"Pagamento Registrado!\n\n{descricao}\n{R$}\n{conta_pagamento}\n\n_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._"` e responde **200** `{"status":"sucesso","mensagem":"Despesa registrada","conta_identificada":null}`.
4. **Desvio 098:** o valor é convertido uma vez (`float()`, texto numérico como `"150.50"` serve) e o número vale no lançamento e na mensagem do WhatsApp. O Flask gravava e depois falhava ao formatar a moeda (500 sem aviso, transação já gravada).

Erro inesperado: **500** `{"status":"erro","mensagem":"Erro ao processar o pagamento. Tente novamente."}`.

### 3.4 `PATCH /api/agendamento/<id>/reserva`

Origem: `reserve_handler.py:21-148`. Corpo: `incluir` (booleano) e `api_key` (a chave do usuário; **note o nome: `api_key`, não `user_api_key`**).

| # | Condição | Resposta |
|---|---|---|
| 1 | `incluir` ausente ou `null`, ou `api_key` falsa | 400 `{"status":"erro","mensagem":"Campos 'incluir' e 'api_key' são obrigatórios"}` (`incluir: false` **é** válido: o Flask testa `is None`) |
| 2 | nenhuma chave casa | 401 `"API key inválida"` (com acento) |
| 3 | `incluir` que não é booleano JSON (texto, número, lista, objeto), depois da autenticação e antes de procurar o agendamento | 400 `"Campo 'incluir' deve ser true ou false"` (desvio 097; o Flask dependia do driver) |
| 4 | agendamento do usuário, **ativo**, com esse `id`, inexistente | 404 `"Agendamento não encontrado ou inativo"` |

Efeito: `gasto_mensal` e `reserva_ideal` **antes** (`IEmergencyReserveService.GetStatusAsync`, desvio 014: só `FIXO` e `LEMBRETE_VARIAVEL`, `ativo`, `incluir_na_reserva`), `UPDATE agendamentos SET incluir_na_reserva = @incluir WHERE id AND usuario_id` e de novo **depois**. O `PARCELADO` pode ser marcado (a coluna muda), mas só entra no cálculo se for `FIXO`/`LEMBRETE_VARIAVEL`.

Sucesso **200**:
```json
{"status":"sucesso","mensagem":"Agendamento 'Netflix' incluído do cálculo de reserva",
 "agendamento":{"id":123,"descricao":"Netflix","valor_previsto":49.9,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","incluir_na_reserva":true},
 "impacto":{"gasto_mensal_anterior":2500,"gasto_mensal_novo":2549.9,"reserva_ideal_anterior":15000,"reserva_ideal_nova":15299.4}}
```
A mensagem usa `incluído` quando `incluir` é `true` e `excluído` quando é `false` (`"Agendamento '{descricao}' incluído do cálculo de reserva"` e `"... excluído do cálculo de reserva"`: o texto do Flask é `f"Agendamento '{descricao}' {status_text} do cálculo de reserva"`; a primeira forma lê estranha mas é a literal). `valor_previsto` é `float(valor or 0)`; números **sem arredondar** (o `float` do Python). Erro inesperado: **500** `"Erro ao atualizar a reserva. Tente novamente."`.

### 3.5 `GET /api/agendamentos/reserva`

Origem: `reserve_handler.py:150-345`. Query: `api_key` (obrigatório), `incluir_na_reserva`, `periodicidade`, `categoria`, `limit` (padrão 100), `offset` (padrão 0).

| # | Condição | Resposta |
|---|---|---|
| 1 | `api_key` ausente ou vazia | 400 `{"status":"erro","mensagem":"Parâmetro 'api_key' é obrigatório"}` |
| 2 | nenhuma chave casa | 401 `"API key inválida"` |
| 3 | `periodicidade` não vazia e (em maiúsculas) fora de `DIARIA`, `SEMANAL`, `QUINZENAL`, `MENSAL`, `ANUAL` | 400 `"Periodicidade inválida. Use: DIARIA, SEMANAL, QUINZENAL, MENSAL ou ANUAL"` |
| 4 | `limit` ou `offset` que o `int()` do Python não converte, negativo ou acima de `int64`, validados (`limit` primeiro) antes da periodicidade | 400 `"Parâmetro 'limit' inválido. Use um número inteiro maior ou igual a zero"` (ou `'offset'`) (desvio 097; o Flask respondia 500) |

Filtros (todos sobre `ativo = true` e o usuário): `incluir_na_reserva` presente (mesmo vazio) ⇒ `= (valor.lower() == "true")`; `periodicidade` ⇒ igual à maiúscula; `categoria` ⇒ `s.nome_sub ILIKE '%categoria%'` (os curingas `%` e `_` do cliente valem, como hoje). Ordem `valor_previsto DESC, descricao ASC`; `LIMIT`/`OFFSET`. O `total` conta com os mesmos filtros, **sem** paginação.

Sucesso **200**:
```json
{"status":"sucesso","total":15,"limit":100,"offset":0,
 "agendamentos":[{"id":123,"descricao":"Aluguel","valor_previsto":1500,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","dia_execucao":5,"categoria":"Moradia","macro_categoria":"Moradia","grupo":"Despesa Essencial","incluir_na_reserva":true}],
 "resumo_reserva":{"gasto_mensal_essencial":2500,"reserva_ideal":15000,"meses_configurados":6}}
```
`valor_previsto` é `float(valor or 0)`; `resumo_reserva` vem de `IEmergencyReserveService.GetStatusAsync` sem arredondar. Erro inesperado: **500** `"Erro ao listar a reserva. Tente novamente."`.

### 3.6 `POST /api/fatura/<id>/reprocessar`

Origem: `webhooks/__init__.py:115-188`. Sem corpo. Exige `x-api-key` (desvio 003): ausente ou errada ⇒ 401 `{"status":"erro","mensagem":"Não autorizado"}`; o corpo nem é lido.

| # | Condição | Resposta |
|---|---|---|
| 1 | não existe fatura com esse `id` | 404 `{"status":"erro","mensagem":"Fatura ID <id> não encontrada"}` |

Efeito: `process_invoice_schedules` (§7.7) para a fatura, o cartão e o **vencimento** da fatura como data de referência, numa transação. Sucesso **200**:
```json
{"status":"sucesso","fatura_id":123,"cartao":"Cartão Inter","vencimento":"15/04/2026","status_fatura":"Aberta",
 "stats":{"total_processados":2,"fixos":1,"parcelados":1,"lembretes":0,"erros":0,"detalhes":[{"tipo":"FIXO","descricao":"Netflix","valor":"49.90"}]}}
```
`stats.erros` (desvio 101) é a contagem de agendamentos que falharam e foram desfeitos: cada agendamento roda num `SAVEPOINT`, o erro de um não perde os outros e `total_processados`/`detalhes` só contam o que foi gravado (sem erro, `"erros":0`). `vencimento` é `dd/MM/yyyy`; `detalhes[].valor` sai como **texto com duas casas** (`"49.90"`: o Flask serializa o `Decimal` do banco como texto) e como `0` (número) quando o agendamento não tem `valor_previsto`. A rota é idempotente (§7.7). Erro inesperado: **500** `{"status":"erro","mensagem":"Erro ao reprocessar a fatura. Tente novamente."}`.

## 4. Regras de negócio

- **RN-011-01** Autenticação (§3.0) é a primeira coisa depois das validações de campo de cada rota (a ordem é a das tabelas); o `usuario_id` e o WhatsApp vêm **só** da chave. Nenhuma rota lê o dono de corpo, query ou caminho. *Origem: `finance_service.py:251-288`; todas as rotas.*
- **RN-011-02** Tenant: toda leitura e escrita filtra pelo usuário da chave (contas, categorias, agendamentos, potes, lançamentos). Contas e agendamentos de outro usuário são indistinguíveis de inexistentes. A fatura do `reprocessar` não tem dono no corpo: o `x-api-key` é administrativo. *Origem: `reserve_handler.py:87-95`; desvios 003 e 039.*
- **RN-011-03** (desvio 039) Resolução de conta por nome ou tipo só vê contas `ativa = true`; ordem estável por `id` onde o Flask não ordena (desvio 030 como precedente). *Origem: `finance_service.py:297-300, 329-401`.*
- **RN-011-04** (desvio 025) `Cartão de Crédito` é a constante `AccountTypes.Card` da 002/004. *Origem: `transaction_handler.py:77, 92`.*
- **RN-011-05** (desvio 008) 500 devolve a mensagem fixa da rota; a exceção vai ao log só como tipo (e `SqlState`), sem corpo, descrição, valor, `user_api_key`, número de WhatsApp (só 4 últimos dígitos), prompt ou resposta do Gemini. **Exceção aprovada pelo desenho:** os textos já escritos para o usuário final, que o Flask deixa subir (`LlmKeyUnavailableException`, cota e permissão do Gemini), saem como estão, porque são literais e seguros (Q-011-5).
- **RN-011-06** (desvio 024) Corpo inválido é `{}`; o 400 de cada rota é o da tabela da §3 (para `/api/transacao`, o `{"erro":"JSON invalido ou ausente"}`).
- **RN-011-07** `data_transacao` e "hoje" vêm de `IClock.Today` (America/Sao_Paulo), nunca do relógio do contêiner (constituição §7). *Origem: `date.today()` no Flask.*
- **RN-011-08** Lançamento só por `ITransactionPosting` (004); o valor negativo é feito pelo serviço (`Amount` é positivo). O serviço **não** verifica pote, saldo nem duplicidade: o pote é desta feature (§7.6). *Origem: `finance_service.py:409-425`; spec 004 §7.2.*
- **RN-011-09** Pendência (§7.5): `pending_tx:{whatsapp}:{id}` e `last_pending:{whatsapp}` com TTL de 300 s, formato JSON como o `redis_service` do Python (a 012 lê a mesma chave). `ICache` é fail-open: Redis fora ⇒ lançamento direto (§3.1 item 6, §3.2 item 5).
- **RN-011-10** (desvio 014) A reserva é calculada só por `IEmergencyReserveService` (005); esta feature não repete a fórmula nem o SQL do cálculo. A listagem (§3.5) lê as linhas dos agendamentos por conta própria porque as colunas e filtros são outros.
- **RN-011-11** (desvio 015) O período do pote é o de `budget_validation_service`: `SEMANAL` segunda a domingo, `QUINZENAL` 1–15 e 16–fim do mês, `MENSAL` mês civil, `ANUAL` ano civil, outro valor ⇒ mensal. Estourar o limite **não bloqueia** e `requer_confirmacao` só vira aviso na pendência. *Origem: `budget_validation_service.py:84-130`.*
- **RN-011-12** `process_invoice_schedules` é **idempotente** por (`fatura_id`, `agendamento_id`), grava sempre `agendamento_id` e isola o erro de cada agendamento com um `SAVEPOINT` (§7.7, desvio 101). É o único lugar da 011 que incrementa parcela; a rotina única do desvio 011 (013) pode substituí-lo depois sem mudar o contrato.
- **RN-011-13** Segredos: `user_api_key`/`api_key` nunca em log nem em mensagem. A `api_key` do `GET` vem na query string (o Flask também): o log de requisição não registra a query. *Origem: desvio 008.*
- **RN-011-14** Populador real: esta feature registra `IInvoiceSchedulePopulator` (implementação real) **antes** do `NullInvoiceSchedulePopulator` da 004 (que usa `TryAdd`). Assim `IInvoiceService.EnsureCurrentAsync` volta a popular a fatura nova, como `ensure_current_invoice_exists` do Flask (Q-004-6: a 011 entrega o serviço que o `reprocessar` e a 013 reutilizam).

## 5. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","011-NNN")]`). Os de integração comparam o corpo como texto exato.

**Autenticação e roteamento (comuns)**
- **AC-011-001** Dada a chave do usuário em texto puro, então a rota autentica; dada a chave cifrada em Fernet no banco, a chave em claro autentica; dado token Fernet que não decifra, nunca autentica (nem com o próprio token como chave).
- **AC-011-002** Dada chave de outro texto, número, booleano ou lista, então 401 da rota (mensagem de cada rota).
- **AC-011-003** Dado `usuarios.ativo = false`, então a chave continua valendo (paridade).
- **AC-011-004** Dado `/webhook-automate/`, `/api/transacao/` ou `/api/agendamentos/reserva/` (barra final), `/api/agendamento/abc/reserva`, `/api/agendamento/-1/reserva`, `/api/fatura/abc/reprocessar`, então 404 HTML do Flask; dado outro método (`GET /webhook-automate`, `POST /api/agendamentos/reserva`, `GET /api/fatura/1/reprocessar`), então 405.
- **AC-011-005** Dado erro inesperado (repositório lança, inclusive `PostgresException` com texto interno), então 500 com a mensagem fixa da rota e log só com o tipo; nenhuma resposta nem log contém a chave, o corpo ou o texto da exceção (desvio 008).
- **AC-011-006** Dado corpo ausente, sem `Content-Type`, inválido, `null`, `[]` ou `"abc"`, então o 400 da rota (`"Dados faltando"`, `"Campos 'incluir' e 'api_key' são obrigatórios"`, `{"erro":"JSON invalido ou ausente"}`), nunca 500.

**`/webhook-automate`**
- **AC-011-010** Dado `GEMINI_API_KEY` vazia, então 503 `"Servico nao configurado"` antes de ler o corpo; dado `texto` ou `user_api_key` falso, 400 `"Dados faltando"`; dada chave inexistente, 401 `"API key invalida"` (sem chamar o Gemini).
- **AC-011-011** Dado Gemini devolvendo `{"valor_decimal": 45.9, "descricao_bruta": "Mercado", "tipo_fluxo": "Despesa"}` (com ou sem cerca ```` ```json ````), Redis disponível, conta corrente, então 200 `{"status":"sucesso","mensagem":"Aguardando confirmacao do usuario","transaction_id":"<8 hex>"}`; no Redis, `pending_tx:{whatsapp}:{id}` com os dados da §3.1 item 7 (TTL ≤ 300) e `last_pending:{whatsapp}` com o `id`; o bot recebe a mensagem de §7.5; **nada** é gravado em `transacoes`.
- **AC-011-012** Dado texto com `cartao`, `compra` ou `credit` (qualquer caixa) e despesa, então a conta é o primeiro `Cartão de Crédito` ativo (desvio 025) e a pendência leva o `fatura_id` da fatura do ciclo de hoje, criada e comitada; sem cartão, cai na conta corrente.
- **AC-011-013** Dada renda (`tipo_fluxo: "Renda"`), então a conta é a primeira `Conta Corrente` (mesmo com palavra de cartão), a categoria vem dos grupos `Renda`, o fallback é o `Outros` de `Receitas Gerais` e `valor_db` é positivo; dada despesa, `valor_db` é negativo e `valor_original` positivo.
- **AC-011-014** Dado usuário sem `Conta Corrente`, então a primeira conta ativa (por `id`); com todas as contas inativas ou nenhuma conta, 400 `"Nenhuma conta ativa encontrada. Cadastre uma conta antes de usar o atalho."` (desvio 097), sem pendência nem gravação; conta inativa nunca é escolhida (desvio 039).
- **AC-011-015** Dado Redis indisponível, então a transação é gravada (`PostSimple`, `valor` negativo, tipo `Despesa`, origem `NULL`, `fatura_id` do cartão), o bot recebe `"Transacao salva!\n\nDescricao: ...\nValor: R$ 45,90\nCategoria: Macro -> Sub"` e a resposta é 200 `{"status":"sucesso"}`.
- **AC-011-016** Dado Redis disponível mas a gravação da pendência falhando, então a transação é gravada, o bot **não** recebe aviso e a resposta é 200 `{"status":"sucesso"}`.
- **AC-011-017** Dado Gemini com JSON inválido, resposta que não é objeto ou `descricao_bruta` que não é texto, então 500 fixo e nada gravado nem pendente; dado `tipo_fluxo` ausente, vale `Despesa`. Dado `tipo_fluxo` fora de `Renda`/`Despesa`, ou `valor_decimal` ausente, `null`, `"abc"`, zero, negativo, `nan` ou acima de 1 bilhão, então 400 com as mensagens da §3.1, sem pendência, gravação nem aviso (desvio 097).
- **AC-011-018** Dado categorização com resposta fora da lista, não numérica, vazia ou bloqueada, então a categoria é o `Outros` de fallback; com id válido, é a escolhida; o prompt enviado ao Gemini é byte a byte o de §7.4.
- **AC-011-019** Dado falha de chave (`LlmKeyUnavailableException`), cota e permissão, então 500 com os textos literais de RN-011-05; outras falhas do Gemini ⇒ mensagem fixa; nenhum log contém o prompt, a resposta ou a chave.
- **AC-011-019a** Dada despesa em cartão, então `EnsureCurrentAsync` é chamado antes e a fatura do ciclo de hoje existe depois da resposta, mesmo com a pendência nunca confirmada (commit antes do Redis).

**`/api/transacao`**
- **AC-011-030** Dado corpo ausente, sem `Content-Type`, inválido, `null` ou `[]`, então 400 `{"erro":"JSON invalido ou ausente"}`; dado `GEMINI_API_KEY` vazia, 503 `"Servico nao configurado"` (antes do corpo).
- **AC-011-031** Dados campos obrigatórios falsos (`valor: 0`, `local: ""`, `conta: "<>"` que vira vazio depois do saneamento), então 400 `"Campos obrigatorios faltando: ..."` listando **todos** os faltantes na ordem `user_api_key, valor, local, conta, tipo_pagamento`.
- **AC-011-032** Dado `valor` `"abc"` ⇒ `"Valor invalido: could not convert string to float: 'abc'"`; `[1]` ⇒ `"... float() argument must be a string or a real number, not 'list'"`; `{"a":1}` ⇒ `... not 'dict'`; `-5` ou `"-5"` ⇒ `"Valor invalido: Valor deve ser positivo"`; `1000000000.01` ⇒ `"Valor invalido: Valor muito alto (max: 1 bilhao)"`; `"inf"` ⇒ idem ("muito alto"); `"nan"` ⇒ `"Valor invalido: Valor deve ser positivo"` (divergência §11); `"15.90"` e `true` (=1) aceitos.
- **AC-011-033** Dado `tipo_pagamento` `"Crédito"`, `" PIX "`, `"débito"`, então normalizado (`credito`, `pix`, `debito`); `"cartao"`, `"boleto"` ⇒ 400 `"tipo_pagamento invalido. Use: credito, debito, pix, dinheiro"`.
- **AC-011-034** Dada chave inválida, então 401 `"API key invalida"` (depois da validação de campos, antes de qualquer acesso ao banco além da chave).
- **AC-011-035** Dada `conta` que não existe, então 400 `"Conta '<nome saneado>' nao encontrada. Verifique o nome exato."`; a busca é (§7.3): nome exato, depois `nome_conta ILIKE '%nome%'`, depois `tipo_conta ILIKE '%nome%'` (`conta: "Crédito"` acha o `Cartão de Crédito` pelo tipo; `"credito"` sem acento **não** acha), sempre só contas ativas (desvio 039) e do usuário, de menor `id` em caso de várias.
- **AC-011-036** Dado crédito em conta de cartão com dias, então `EnsureCurrentAsync` roda, a fatura do ciclo de hoje existe e a pendência leva `fatura_id`; `vinculado_fatura` é `true`; crédito em conta que não é cartão ⇒ `fatura_id` `null`, `vinculado_fatura` `false`; débito, pix e dinheiro nunca vinculam.
- **AC-011-037** Dado pote ativo que contém a categoria e que seria ultrapassado, então `budget_warning` da pendência é a mensagem de §7.6, a pendência é criada e a resposta é a mesma 200 (não bloqueia); sem pote ou dentro do limite, `budget_warning` é `null`.
- **AC-011-038** Dado Redis disponível e conta corrente, então 200 `{"status":"success","message":"Transacao pendente de confirmacao no WhatsApp","transaction_id":"<8 hex>","categoria_sugerida":"Macro -> Sub","conta_utilizada":"<nome real>","vinculado_fatura":false}`; pendência com os dados da §3.2 item 6 (`origem` `api_endpoint`, `tipo_pagamento` normalizado, `descricao_final`); `last_pending` regravado; mensagem de §7.5 enviada ao WhatsApp **do dono da chave**.
- **AC-011-039** Dado Redis indisponível, então grava (`Expense`, origem `NULL`, `valor` negativo), avisa `"Transacao salva!\n\n{descricao_final}\n{R$}\n{Macro -> Sub}"` e responde 200 `{"status":"success","message":"Transacao salva com sucesso (Redis indisponivel)","categoria_sugerida":"..."}`; falha ao gravar a pendência ⇒ grava, **sem** aviso, 200 `{"status":"success","message":"Transacao salva (erro no Redis)","transaction_id":null}`.
- **AC-011-040** Dado `local` com `<script>`, `{}`, `$`, `;`, `&` e espaços repetidos, então saneado (§7.2); `descricao` aceita os especiais; `local` com mais de 100 caracteres é cortado em 100 antes da limpeza.
- **AC-011-041** Dado `local`, `descricao`, `conta` ou `tipo_pagamento` verdadeiro que não é texto, então 400 `"Campo '<campo>' deve ser texto"` (nada gravado; desvio 097).
- **AC-011-042** Dado `descricao` `"  "`, então `null` (descrição final = `local`); com texto, `"{local} - {descricao}"` e o texto da IA é o mesmo.

**`/webhook-sms-payment`**
- **AC-011-060** Dado banco fora, então 503 `{"status":"erro","resposta":"Banco de dados temporariamente indisponivel"}` antes de ler o corpo; dado algum dos quatro obrigatórios falso (`valor_pago: 0`), 400 `"Dados faltando"`; chave inválida, 401.
- **AC-011-061** Dada descrição parecida com um agendamento `FIXO` ou `LEMBRETE_VARIAVEL` ativo (`WRatio` ≥ 65 do texto sem artigo contra a descrição **como está gravada**, §7.3), então uma despesa de `-valor_pago` na conta do agendamento, subcategoria e descrição do agendamento, na data de `data_pagamento` (padrão hoje), `agendamento_id` `NULL`; aviso no WhatsApp com o texto da §3.3 item 2; resposta 200 com `transaction_id`. `PARCELADO` e agendamento inativo nunca casam.
- **AC-011-062** Dada a conta do agendamento sendo cartão, então a transação leva a fatura do ciclo de `data_pagamento`.
- **AC-011-063** Dada descrição sem correspondência, então despesa avulsa na conta `Carteira` (ou a primeira ativa), descrição `"{descricao} ({conta_pagamento})"`, categoria do Gemini ou `Outros` (em conta de cartão com os dois dias a 004 vincula a fatura, §11); aviso e resposta 200 `{"status":"sucesso","mensagem":"Despesa registrada","conta_identificada":null}`. O Gemini é chamado **uma única vez**, só para categorizar (nenhum prompt `extract_transaction_details`, desvio 096); falha da categorização (que não seja bloqueio) vira 500 sem gravar nada.
- **AC-011-064** Dada nenhuma conta ativa no ramo avulso, então 500 fixo e nada gravado.
- **AC-011-065** Dado `valor_pago` como texto numérico (`"150.50"`, `" 80.5 "`), então a transação é gravada com o número, o WhatsApp recebe o aviso com `R$ 150,50` e a resposta é 200, na quitação e na despesa avulsa (desvio 098).
- **AC-011-066** Dado `data_pagamento` `"2026-02-30"`, `"15/01/2026"`, número, então 400 `"data_pagamento invalida. Use o formato AAAA-MM-DD"` e nada gravado (desvio 097); `"2026-01-15"` e `"20260115"` valem. `descricao_pagamento` que não é texto ⇒ 400 `"Campo 'descricao_pagamento' deve ser texto"`.
- **AC-011-067** Dado `valor_pago` negativo, `nan`, que o `float()` recusa (`"abc"`, `[1]`) ou acima de 1 bilhão, então 400 `"Valor invalido: <motivo>"`, nada gravado e nenhum aviso (desvio 097; o Flask gravava uma despesa **positiva** para o negativo).
- **AC-011-068** Dada a transação gravada e o bot fora do ar, então a resposta continua 200 (o bot nunca falha a rota).

**`PATCH /api/agendamento/<id>/reserva`**
- **AC-011-080** Dado `incluir` ausente ou `null`, ou `api_key` falsa, então 400 `"Campos 'incluir' e 'api_key' são obrigatórios"`; `incluir: false` com `api_key` válida passa; chave inválida ⇒ 401 `"API key inválida"`.
- **AC-011-081** Dado agendamento próprio ativo, então a coluna muda, a resposta 200 traz `agendamento` (`id`, `descricao`, `valor_previsto`, `periodicidade`, `tipo_agendamento`, `incluir_na_reserva` com o valor enviado) e `impacto` com os quatro números: anterior e novo pelo `IEmergencyReserveService`, sem arredondar; a mensagem usa `incluído` (`true`) ou `excluído` (`false`).
- **AC-011-082** Dado agendamento inexistente, inativo ou de outro usuário, então 404 `"Agendamento não encontrado ou inativo"` e nada muda.
- **AC-011-083** Dado `PARCELADO` marcado, então a coluna muda e `impacto` não muda (não conta na reserva); `FIXO` e `LEMBRETE_VARIAVEL` mudam o `impacto` na proporção da periodicidade (MENSAL × meses do usuário; ANUAL integral; SEMANAL × round(meses × 4,33); QUINZENAL × 2·meses; DIARIA × 30·meses).
- **AC-011-084** Dado `incluir` texto, número, lista ou objeto (com `api_key` válida), então 400 `"Campo 'incluir' deve ser true ou false"` e nada muda (desvio 097).
- **AC-011-085** Dado `id` > `int.MaxValue`, então 404 `"Agendamento não encontrado ou inativo"` (id tratado como inexistente).

**`GET /api/agendamentos/reserva`**
- **AC-011-100** Dado `api_key` ausente ou vazia, então 400 `"Parâmetro 'api_key' é obrigatório"`; chave inválida ⇒ 401 `"API key inválida"`.
- **AC-011-101** Dado usuário com agendamentos, então 200 com `total`, `limit` (100), `offset` (0), `agendamentos` ordenados por `valor_previsto DESC, descricao ASC`, só `ativo = true` do usuário, cada item com as chaves `id, descricao, valor_previsto, periodicidade, tipo_agendamento, dia_execucao, categoria, macro_categoria, grupo, incluir_na_reserva` e `resumo_reserva` (`gasto_mensal_essencial`, `reserva_ideal`, `meses_configurados`).
- **AC-011-102** Dado `incluir_na_reserva=true` (qualquer caixa) ⇒ só os incluídos; `false`, `abc` ou vazio ⇒ só os **não** incluídos; ausente ⇒ todos.
- **AC-011-103** Dado `periodicidade=mensal` ⇒ só `MENSAL`; `semestral` ⇒ 400 `"Periodicidade inválida. Use: DIARIA, SEMANAL, QUINZENAL, MENSAL ou ANUAL"`; vazia ⇒ sem filtro.
- **AC-011-104** Dado `categoria=net` ⇒ só subcategorias com `net` no nome (sem diferenciar caixa); `%` e `_` funcionam como curinga; `total` conta com os filtros e ignora `limit`/`offset`.
- **AC-011-105** Dado `limit=2&offset=1`, então 2 itens a partir do 2º e `total` completo; `limit=0` ⇒ lista vazia; `limit=abc`, `limit=1.5`, `limit=-1`, `offset=-1`, `offset=` ⇒ 400 `"Parâmetro '<limit|offset>' inválido. Use um número inteiro maior ou igual a zero"` (desvio 097); `limit=" 5 "` e `+5` valem.
- **AC-011-106** Dado usuário sem agendamentos ⇒ 200 com `total: 0`, `agendamentos: []` e `resumo_reserva` zerado com `meses_configurados` do usuário (6 se nulo).
- **AC-011-107** Dado agendamento sem `valor_previsto`, então `valor_previsto` `0` e a ordem do SQL (`NULL` primeiro no `DESC`, como o Postgres).

**`POST /api/fatura/<id>/reprocessar`**
- **AC-011-120** Dado `x-api-key` ausente ou errada, então 401 `{"status":"erro","mensagem":"Não autorizado"}` sem ler o corpo nem tocar o banco; certa ⇒ segue.
- **AC-011-121** Dado `id` inexistente, então 404 `"Fatura ID <id> não encontrada"`.
- **AC-011-122** Dada fatura de cartão com um `FIXO`, um `PARCELADO` (2 de 3 executadas) e um `LEMBRETE_VARIAVEL` ativos da conta, então 3 transações novas com `agendamento_id`, `consolidada = true`, dia de `dia_execucao` no **mês e ano do vencimento** (dia inexistente cai no último), `Renda` (positivo) se o grupo é `Renda`, senão `Despesa` (negativo); a parcela leva `"{descricao} (3/3)"`, o contador vai a 3 e o agendamento é desativado (`>=` total); resposta 200 com `stats` (`total_processados` 3, `fixos` 1, `parcelados` 1, `lembretes` 1, `detalhes` na ordem de `dia_execucao, descricao`) e `detalhes[].valor` como texto `"49.90"`.
- **AC-011-123** Dada a segunda chamada (idempotência), então 200 com `total_processados: 0` e `detalhes: []`, nenhuma transação nova e o contador do `PARCELADO` intacto.
- **AC-011-124** Dado agendamento `ANUAL` com `mes_execucao` diferente do mês do vencimento, então ignorado; igual ⇒ processado; agendamentos de **outra conta** ou `ativo = false` nunca entram.
- **AC-011-125** Dado agendamento sem `valor_previsto`, então a transação sai com valor `0` e o detalhe com `valor: 0` (número).
- **AC-011-126** Dado erro ao gravar um dos agendamentos (ex.: gatilho que recusa o `INSERT`), então só o que ele gravou é desfeito (`ROLLBACK TO SAVEPOINT`, inclusive o avanço da parcela), os outros ficam gravados, a resposta é 200 e `stats.erros` conta a falha; `total_processados` e `detalhes` só trazem os gravados (desvio 101).
- **AC-011-127** Dado `IInvoiceService.EnsureCurrentAsync` (004) criando uma fatura nova, então o populador real é chamado **depois do commit** e processa a fatura nova (a mesma rotina; AC-004-95 com populador real); erro da rotina é engolido.

**Rotinas puras e portas**
- **AC-011-140** `sanitize_input`: corta em `max_length` por **pontos de código**, remove controles exceto `\t\n\r`, remove `< > { } \ $ ` | ; &` quando não aceita especiais, colapsa `\s+` em espaço e faz `trim`; vazio devolve vazio; tabela gerada pelo Python.
- **AC-011-141** `WRatio` e `extractOne` batem com o `rapidfuzz` 3.14.3 (tabela gerada pelo Python, inclusive os casos com maiúscula na descrição guardada, que **não** é normalizada).
- **AC-011-142** `find_matching_bill` remove, nesta ordem e uma única vez cada, os prefixos `a `, `o `, `da `, `do `, `de `, `conta de `, `conta ` (da lista, em sequência, sobre o texto já reduzido) e compara com o corte 65; sem agendamentos devolve nada.
- **AC-011-149** (desvio 099) Dado o texto do SMS e as descrições guardadas com qualquer caixa e acento, então ambos os lados são minúsculos e sem acentos antes do `WRatio` (`cemig` × `CEMIG` casa, `agua` × `Água` casa com 100); o fixture `bill_matching` traz o resultado novo e o do Flask (`legacy_result_id`), e há ao menos um caso que só casa com a correção.
- **AC-011-143** `calcular_periodo_pote` e a mensagem de aviso (um e vários potes) batem com o Python, inclusive `R$ 1,234.50` com vírgula de milhar e ponto decimal (formato `{:,.2f}` do Python, não `pt-BR`), `percentual` arredondado a 1 casa (meio-par) e `limite <= 0` ⇒ percentual 0.
- **AC-011-144** A mensagem de confirmação (§7.5) bate caractere a caractere com o Python (com o único ajuste do desvio 100: `💳` no lugar do U+FFFD) para: Automate (sem `local`), `/api/transacao` com e sem `descricao`, com `credito` e fatura, `debito`, `pix`, `dinheiro`, com `budget_warning`, categoria desconhecida. A linha do cartão é `💳 {conta} (na fatura)` (ou `(1ª parcela na fatura atual)`) e nenhuma mensagem contém U+FFFD.
- **AC-011-145** `formatar_moeda`: `R$ 1.234,56`, `R$ 0,00` para nulo, arredondamento do Python (`'{:,.2f}'`), negativo `R$ -5,00` (formato do fallback).
- **AC-011-146** `process_invoice_schedules` (§7.7) na ordem de `dia_execucao, descricao`, cada ramo do `stats`, o filtro de `ANUAL`, o dia 31 em fevereiro, o contador e a desativação; parcela com `parcelas_executadas` nulo conta como erro (o `TypeError` do Flask, desvio 101).
- **AC-011-147** `IUserApiKeyAuthenticator` com vários usuários (cifrado e texto puro), usuário sem chave e chave que não decifra.
- **AC-011-148** Os prompts de `extract_from_notification`, `categorize_transaction` (com a lista de categorias serializada como o `json.dumps` do Python, `ensure_ascii`, separadores `, ` e `: `) e `extract_transaction_details` batem byte a byte com os do Python.

## 6. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `usuarios` | SELECT | chave do atalho (`api_key_automate`), `numero_whatsapp`; reserva lê `meses_reserva_emergencia` pela 005 |
| `contas` | SELECT | resolução por nome/tipo, sempre `ativa = true` e do usuário; posse e tipo pela 004 |
| `subcategoria`, `macrocategoria`, `grupocategoria` | SELECT | categorias da IA, nome da categoria, listagem da reserva |
| `transacoes` | INSERT | **só** por `ITransactionPosting` (004) e pelo populador (`process_invoice_schedules`, que grava `agendamento_id` e `consolidada`) |
| `faturas` | SELECT, INSERT | criada pela 004 (`EnsureCurrent`, `InvoiceLinking`); lida no `reprocessar` |
| `agendamentos` | SELECT, UPDATE | `incluir_na_reserva` (PATCH); `parcelas_executadas` e `ativo` (populador) |
| `potesdegastos`, `potesubcategorias` | SELECT | só leitura (potes são criados direto no banco) |

Sem migration nova. Sem coluna nova.

## 7. Detalhes que precisam ser exatos

### 7.1 `float()` do Python (rotas `/api/transacao` e SMS)
`float(valor)` aceita número JSON, booleano (`true` = 1), texto numérico com espaços nas pontas, sinal, expoente, `_` entre dígitos, `inf`, `infinity`, `nan` (qualquer caixa). Texto que não converte ⇒ `ValueError("could not convert string to float: '<repr>'")`; lista ⇒ `TypeError("float() argument must be a string or a real number, not 'list'")`; objeto ⇒ `... not 'dict'`; `null` nunca chega (é falso). `<repr>` é o `repr` do Python do texto: aspas simples, ou duplas se o texto tem `'` e não tem `"`; `\\`, `\n`, `\r`, `\t` e controles ⇒ escapes. A mensagem completa vai para o cliente em `"Valor invalido: ..."` (Python 3.11).

### 7.2 `sanitize_input(text, max_length, allow_special_chars)`
Falso (`""`) ⇒ devolve como veio. Senão: corta em `max_length` pontos de código; remove caracteres com código < 32 exceto `\t`, `\n`, `\r`; se `allow_special_chars` falso, remove `< > { } \ $ ` | ; &`; troca `\s+` por um espaço; `trim`. Aplicações: `local` 100, `conta` 50, `tipo_pagamento` 20 (depois `trim`, minúsculas, NFKD e descarte do que não é ASCII), `descricao` 200 **com** especiais.

### 7.3 Casamento de conta e de conta fixa
`get_account_details_by_name`: (1) `nome_conta = @nome`; (2) `nome_conta ILIKE '%nome%'`; (3) `tipo_conta ILIKE '%nome%'` — o primeiro que devolver linha, **ativas e do usuário**, `ORDER BY id LIMIT 1`. `get_account_by_name('Carteira', fallback=True)`: (1) exato; (2) `ILIKE`, com 1 ou mais resultados devolve o de menor `id`; (3) fallback: a de menor `id` do usuário; nenhuma ⇒ `null`.
`find_matching_bill`: agendamentos `ativo` do usuário, `tipo_agendamento IN ('FIXO','LEMBRETE_VARIAVEL')`, com subcategoria (`JOIN`); `descricao_limpa = descricao_pagamento.lower().strip()`; para cada prefixo da lista `['a ', 'o ', 'da ', 'do ', 'de ', 'conta de ', 'conta ']`, nesta ordem, se o texto começa por ele remove e faz `strip()`; `process.extractOne(fold(descricao_limpa), descrições, processor=fold, scorer=WRatio, score_cutoff=65)` com `fold` = minúsculas e sem acentos (decomposição Unicode sem as marcas), aplicado aos **dois lados** (desvio 099; o Flask não normalizava as descrições guardadas e o `rapidfuzz` 3.x é sensível à caixa). Em empate de pontuação vale a primeira na ordem do dicionário (descrições repetidas colapsam na **última** linha, como o `dict` do Python); a ordem do dicionário é a da consulta (`ORDER BY id`, estável).

### 7.4 Prompts do Gemini (copiados literalmente; recuo de 4 espaços do Python)
`extract_from_notification` (sem cache):
`"\n    Analise a notificação: \"{texto}\"\n    Retorne APENAS JSON com: \"valor_decimal\" (sempre positivo), \"descricao_bruta\", \"tipo_fluxo\" (\"Renda\" ou \"Despesa\").\n    "`
`categorize_transaction` (cache `category`, por usuário):
`"\n    Minhas subcategorias são: {json.dumps(lista)}\n    A transação teve a descrição: \"{descricao}\"; O tipo é: \"{tipo}\"\n    Qual é o \"id\" da subcategoria que melhor corresponde? Se for genérico, use o \"id\" de \"Outros\" (que é {fallback}).\n    Responda APENAS com o número do ID.\n    "`
`extract_transaction_details` (cache `extract_trans`, global): o texto de `gemini_service.py:390-406` com `mensagem = "Paguei {valor} de {descricao}"` (a `valor` como veio no JSON, sem formatar) e `intent = "Despesa"`. Os testes de snapshot usam a saída do Python (`tests/Fixtures/generate_automation_fixtures.py`).
`{fallback}` é o id do `Outros` ou `None` (texto `None`).

### 7.5 Pendência e mensagem de confirmação
`create_pending_transaction` + `format_confirmation_message` (`transaction_confirmation_service.py:9-165`), com a categoria procurada na lista de categorias (`"{macro} → {sub}"`, senão `Desconhecida`). Valores monetários pelo `formatar_moeda`; o texto contém emojis e `💳` antes do nome da conta de cartão (o arquivo do Python tem U+FFFD no lugar, corrompido; desvio 100). Layout, nesta ordem: `💰 *CONFIRME SUA TRANSAÇÃO*`, `📍 *{local}*` (`local`; sem `local` usa a `descricao` e zera a descrição), `📝 {descricao}` (se houver), `💵 *{R$}*`, `{💰|💸} {tipo}` (`💰` só para `Renda`), linha da conta (`💳 {conta} (na fatura)` se `credito` com `fatura_id`, senão `🏦 {conta} ({Débito|PIX|Dinheiro|<tipo_pagamento>})`; sem conta não há linha), `📂 {categoria}`, bloco `AVISO DE ORÇAMENTO` se houver `budget_warning`, e o rodapé de opções com `_ID: {id} | Expira em 5 min_`. O teste compara com a saída do Python.

### 7.6 `validate_budget`
Potes ativos do usuário que contêm a subcategoria (`potesdegastos` ⨝ `potesubcategorias`); período por `calcular_periodo_pote` (RN-011-11) sobre **hoje**; gasto do período = `abs(SUM(valor))` das `Despesa` do usuário nas subcategorias do pote dentro do período (inclusivo); `gasto_apos = gasto + valor`; `percentual = gasto_apos / limite * 100` (0 se `limite <= 0`) arredondado a 1 casa (meio-par do Python); ultrapassa se `gasto_apos > limite`. Mensagens de aviso (um pote e vários) copiadas de `budget_validation_service.py:262-293` com `{:,.2f}` do Python.

### 7.7 `process_invoice_schedules`
**Desvio 135 (implementado na 013, 2026-10-09): `LEMBRETE_VARIAVEL` é pulado, não vira transação e `lembretes` fica sempre 0 (AC-011-122 vale com `FIXO` e `PARCELADO`).**

Cada agendamento roda entre `SAVEPOINT` e `RELEASE SAVEPOINT` (desvio 101); se qualquer passo dele falha, `ROLLBACK TO SAVEPOINT`, `stats.erros` sobe e o laço segue com o próximo. Só o que foi liberado entra em `total_processados` e `detalhes`. A transação externa é uma só e é comitada no fim.
Agendamentos `ativo` da **conta do cartão**, `FIXO`/`PARCELADO`/`LEMBRETE_VARIAVEL`, `ANUAL` só se `mes_execucao` = mês da data de referência (o vencimento da fatura), ordem `dia_execucao, descricao`. Para cada um, se já existe transação (`fatura_id`, `agendamento_id`): pula. Senão: tipo `Renda` se o grupo é `Renda`, senão `Despesa`; valor = `valor_previsto` (0 se nulo), negativo na despesa; descrição = `descricao`, ou `"{descricao} ({parcelas_executadas + 1}/{total_parcelas})"` no `PARCELADO`; data = `dia_execucao` no mês e ano da data de referência (último dia se inexistente); `INSERT` com `fatura_id`, `agendamento_id`, `consolidada = TRUE`, sem `origem`. No `PARCELADO`: `parcelas_executadas = parcelas_executadas + 1` e `ativo = NOT (total_parcelas não nulo e novo >= total_parcelas)`. `stats`: contadores por tipo e `detalhes` `{tipo, descricao, valor}` (`valor` = `abs(valor_previsto)`; texto com 2 casas se há valor, `0` se não).

## 8. Desvios aplicados

| Desvio | Estado | Como se aplica |
|---|---|---|
| 003 rotas sem autenticação | aprovado (parte 011) | `reprocessar` exige `x-api-key`; ninguém chama a rota hoje (atalhos e bot não a usam; só manutenção) |
| 005 whitelist | implementado | as 6 rotas são alcançáveis (whitelist derivada) |
| 008 vazamento | aprovado | RN-011-05, AC-011-005 e 019 |
| 014 reserva única | aprovado | RN-011-10, AC-011-081, 083 |
| 015 período do pote | aprovado | RN-011-11, §7.6, AC-011-037, 143 |
| 017 chave em texto puro ou Fernet | implementado | §3.0, AC-011-001, 147 |
| 024 corpo sem `Content-Type` | aprovado | RN-011-06, AC-011-006 |
| 025 cartão com acento | aprovado | RN-011-04, AC-011-012 |
| 030 `Outros` por `ORDER BY id` | aprovado | `ICategoryLookup.FindFallbackAsync` (§3.1 item 3), AC-011-013, 018 |
| 039 contas inativas | aprovado | RN-011-03, AC-011-014, 035 |
| 012 `agendamento_id` em vez de descrição | aprovado (004, 005, 013) | **não** se aplica à quitação por SMS (§3.3, Q-011-3); o populador (§7.7) grava `agendamento_id` |
| 096 a 101 | implementado (feature 011, 2026-10-09) | §3.1 a 3.6, §7.3, §7.5, §7.7; AC-011-017, 041, 063, 065 a 067, 084, 105, 126, 144, 146, 149 |
| 102 chave do usuário (índice cego) | adiado | não implementado; segue `get_user_by_api_key` (§3.0) |
| 011 parcelas unificadas | aprovado (013) | `process_invoice_schedules` já usa `>=` e grava `agendamento_id`; a unificação é da 013 |
| 001 Receita × Renda | aprovado | não se aplica: estas rotas só usam `Renda`/`Despesa` do banco |

## 9. Desvios 096 a 102 (decididos em 2026-10-09)

| # | Título | Estado | O que mudou |
|---|---|---|---|
| 096 | SMS: remover a chamada inútil ao Gemini | implementado | a despesa avulsa não chama `extract_transaction_details`; só a categorização usa a IA |
| 097 | Entradas inválidas viram 400 | implementado | mensagens em português por caso (§3.1 a 3.5); nada é gravado; `nan` e valor negativo do SMS também |
| 098 | SMS: `valor_pago` como texto numérico | implementado | o valor é convertido uma vez e vale no lançamento e no aviso |
| 099 | Quitação por SMS sem diferenciar a caixa | implementado | minúsculas e sem acentos nos dois lados do `WRatio` (a 012 usa a mesma função) |
| 100 | Emoji `💳` na confirmação | implementado | substitui o U+FFFD (a 012 compartilha o texto) |
| 101 | Reprocessar fatura: isolar o erro por agendamento | implementado | `SAVEPOINT` por agendamento; `stats.erros` |
| 102 | Chave do usuário: índice cego e cabeçalho no `GET` | **adiado** | não implementado |

## 10. Fora de escopo

- `POST /webhook-whatsapp`, confirmação (`OK`, `TROCAR`, `CANCELAR`) das pendências criadas aqui, intents (012). A 012 lê `pending_tx:*` e `last_pending:*` no formato desta spec.
- Motor de agendamentos, execução de parcelas, jobs e fechamento de faturas (013).
- `connect-calendar`, `oauth2callback`, `disconnect-calendar` (010d, já entregues).
- Validação de pote nas rotas JWT (`POST /api/transactions`): o Flask não valida (spec 004 §10).
- Cadastro da chave do atalho (`/auth/register`, 001).

## 11. Divergências conhecidas em relação ao Flask

Documentadas aqui porque não dá para reproduzir (ou reproduzir seria perverso) e não bloqueiam nenhum cliente conhecido:

1. **Comparação da chave do usuário** é em tempo constante (o Flask usa `==`): invisível.
2. **Conta do `Automate` quando não há `Conta Corrente` e a primeira conta é cartão e o fluxo é `Renda`:** o Flask vincula a fatura; o `PostSimple` só vincula despesa. Só afeta o caminho sem Redis (a pendência leva `fatura_id` igual ao do Flask).
3. **Ordem das categorias** enviadas ao Gemini: o Flask não ordena (ordem física do Postgres); o .NET ordena por `id`. Muda o texto do prompt (e a chave de cache) só se a ordem física era outra; o cache é descartado na virada.
4. **Despesa avulsa por SMS em conta de cartão** (só acontece sem conta `Carteira` e com um cartão como primeira conta): o Flask grava sem `fatura_id`; o `PostSimple` vincula a fatura do ciclo (a mesma regra de todo lançamento de despesa em cartão).
5. **Valor máximo no SMS e no Automate:** acima de 1 bilhão dá 400 (como em `/api/transacao`); o Flask só falhava no banco (500) a partir de 10^13.

(As divergências antigas de `nan`, valor negativo do SMS e erros por agendamento no `reprocessar` deixaram de existir com os desvios 097 e 101.)

## 12. Perguntas em aberto (siga o Flask até haver resposta)

- **Q-011-1** Quem chama `reprocessar` hoje? (Desvio 003: agora exige `x-api-key`.) Padrão: ninguém além de manutenção manual.
- **Q-011-2** O atalho do iPhone envia `valor_pago` do SMS como número ou como texto? Resolvido pelo desvio 098: os dois funcionam.
- **Q-011-3** A quitação por SMS deve gravar `agendamento_id` na transação (coerente com o desvio 012 e com a detecção de "já pago" da 013)? Padrão: **não** (paridade).
- **Q-011-4** `origem` das transações dessas rotas: `null` (paridade, Q-004-3) ou `automate`/`api_endpoint`/`sms`? Padrão: `null`.
- **Q-011-5** Os textos de falha de chave e cota do Gemini (já escritos para o usuário) podem sair como estão no 500 (RN-011-05)? Padrão: sim.
- **Q-011-6** O 503 `"Servico nao configurado"` depende de `GEMINI_API_KEY` do sistema (o `gemini_model` global do Flask). Com usuários só com chave própria isso bloqueia as rotas. Padrão: paridade.
- **Q-011-7** Populador real: a 011 registra `IInvoiceSchedulePopulator` e o `EnsureCurrentAsync` da 004 volta a popular a fatura nova (como o Flask). A 013 pode trocar pela rotina única do desvio 011. Confirmar.
- **Q-011-8** Idempotência: o mesmo SMS reenviado cria duas despesas (como hoje). Aceitar? Padrão: sim (fora do escopo).
