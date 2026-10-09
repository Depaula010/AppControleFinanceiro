# Spec 012 — Webhook WhatsApp e intents

- **Estado:** aprovada por delegação do Rafael em 2026-10-07 (fluxo SDD delegado ao agente; valem só paridade com o Flask e os desvios já aprovados; achados novos ficam como `proposto` em `parity-deviations/150` a `161` e **não** são implementados)
- **Onda:** 4
- **Origem (Python):** `app/routes/webhooks/__init__.py:49-54` (rota), `handlers/whatsapp_handler.py:48-1584` (orquestrador), `shared/validators.py`, `intents/__init__.py:101-244` (registro e `route_intent`), `intents/base_intent.py:34-227`, `intents/{transaction,query,calendar,notification,analytics,admin,config,drive}_intents.py`, `app/services/gemini_service.py:175-374, 443-462, 536-1254 (menos 676-711 e 1137-1188, sem uso), 1509-1722, 1724-1889`, `transaction_confirmation_service.py`, `event_confirmation_service.py`, `transaction_feedback_service.py`, `finance_service.py:297-407, 457-490, 611-860, 922-1042, 1448-1690, 1695-1961`, `fixed_bills_service.py`, `period_query_service.py`, `calendar_query_service.py`, `calendar_management_service.py`, `free_time_finder_service.py`, `user_schedule_pattern_service.py`, `notification_config_service.py`, `monthly_report_config_service.py`, `location_service.py`, `user_address_service.py`, `analytics_service.py`, `forecast_service.py`, `chart_service.py`, `google_drive_service.py:37-100`, `nightly_checkin_service.py:139-246, 381-617`, `queries/agendamentos_queries.py`, `queries/faturas_queries.py`, `shared/formatters`, `shared/validators/input_sanitizer.py`, `services/redis_service.py`.
- **Depende de:** 001a (`IClock`, `IEncryptionService`), 003 (`ICategoryLookup`), 004 (`ITransactionPosting`, `IInvoiceService`, `ITransactionsUnitOfWork`, `InvoiceLinking`), 005 (`IEmergencyReserveService`), 007 (`IFavoriteAddressRepository`), 008 (`IApiKeyValidator`; chave por usuário via `IProviderKeySelector`), 010 (`ILlmClient`, `ICache`, `IBotClient`, `ICalendarClient`, `IDriveClient`, `IGoogleAccountService`, `GoogleLinks`, `IRouteClient`, `CalendarFormatter`, `DriveFilePolicy`), 011 (`FuzzyScorer`, `BillMatching`, `BudgetPeriods`, `MoneyText`, `InputSanitizer`, `PyJsonValue`, `ConfirmationMessage`, `IPendingTransactionStore`, `TransactionCategorizer`, `DirectPoster`, `IAutomationLookups`, `IBudgetReader`), 014 (nada além da leitura de `calendaralertconfigs` por outras features). 013 (entregue): `NightlyCheckinService` (snapshot, sessão e mensagem), `IScheduleExecutor` e `IChartRenderer` (pizza); o restante do check-in (resposta) é desta feature (§7.9).
- **Consumidores:** o bot Baileys (Node), que repassa cada mensagem recebida em `POST /webhook-whatsapp` e envia ao usuário o texto de `resposta`. O Angular e os atalhos **não** usam esta rota.

## 1. Objetivo

Uma rota síncrona recebe a mensagem do usuário no WhatsApp, autentica o bot, identifica o usuário pelo número, resolve conversas pendentes (confirmação de transação, de evento, check-in) e, se não há nenhuma, pede ao Gemini a **intenção** da mensagem e a executa: registrar despesa/renda, consultar saldo, potes, reserva, faturas, contas fixas, vencimentos, período, agenda, horários livres, análises, gráfico, configurar notificações/relatório/localização/endereços e subir arquivo ao Drive. A resposta é sempre o texto que o bot entrega (`resposta`); só o gráfico é enviado à parte pela porta do bot.

O contrato visível desta feature são **os prompts do Gemini e os textos ao usuário**: são portados literalmente (prompts como modelos de texto gerados do Python; textos conferidos por fixtures e por um harness que executa o handler real do Flask, §10).

## 2. Rota

| Método | Caminho | Auth | Envelope |
|---|---|---|---|
| POST | `/webhook-whatsapp` | `x-api-key` (obrigatório) e `X-Webhook-Signature` (HMAC, opcional) | E: `{"status":"sucesso","resposta":"<texto>"}` / `{"status":"erro","resposta":"..."}`; o 400 e o 503 de configuração usam `mensagem` |

Sem barra final, método diferente de POST ⇒ 405, como em todo o contrato. Não passa por `RequireUser` (não há JWT). A rota é alcançável por IP público (whitelist derivada, desvio 005). `client_max_body_size 10M` fica no nginx (mídia em base64).

## 3. Contrato da rota

Corpo: `texto` (texto, obrigatório), `numero_remetente` (texto, obrigatório; o que vem depois do `@` é descartado), `media_data` (base64, com ou sem prefixo `data:...;base64,`), `media_type`, `media_filename` (padrão `"arquivo"`). Chaves desconhecidas são ignoradas. Respostas em ordem fixa (primeira que casar vence); `Content-Type: application/json; charset=utf-8`.

| # | Condição | Resposta |
|---|---|---|
| 1 | banco fora (`IDatabaseHealth`) | 503 `{"status":"erro","resposta":"Banco de dados temporariamente indisponível"}` |
| 2 | `GEMINI_API_KEY` do sistema vazia | 503 `{"status":"erro","mensagem":"Serviço não configurado"}` |
| 3 | `X-Webhook-Signature` presente (depois de `strip`) e diferente do HMAC-SHA256 em hexadecimal minúsculo do **corpo bruto** com `WEBHOOK_SIGNATURE_KEY` (padrão `API_SECRET_KEY`) | 401 `{"status":"erro","resposta":"Assinatura inválida"}` |
| 4 | `x-api-key` ausente, vazia ou diferente de `API_SECRET_KEY` (comparação em tempo constante) | 401 `{"status":"erro","resposta":"Não autorizado"}` |
| 5 | corpo ausente, vazio, sem `Content-Type`, inválido ou que não seja objeto (desvio 024), ou `texto`/`numero_remetente` falsos | 400 `{"status":"erro","mensagem":"Dados faltando"}` |
| 6 | `texto` ou `numero_remetente` verdadeiros mas não texto (`5`, `[1]`) | 500 `{"status":"erro","resposta":"Erro ao processar."}` |
| 7 | exceção cuja mensagem contém `429`, `quota` ou `rate limit` (sem diferenciar caixa) | 429 `{"status":"erro","resposta":"⚠️ *Limite de IA Excedido*\n\nO sistema atingiu o limite diário de processamento de IA.\n\n🔧 *Soluções:*\n• Aguarde alguns minutos e tente novamente\n• Use comandos diretos (ex: 'gastei 50 em comida')\n• Configure sua própria chave de API do Gemini\n\n⏰ O limite é renovado automaticamente."}` |
| 8 | qualquer outra exceção | 500 `{"status":"erro","resposta":"Erro ao processar."}` |
| 9 | processamento normal | 200 `{"status":"sucesso","resposta":"<texto>"}` |

A ordem 1, 2, 3, 4 é a do Flask (o 503 vem antes da autenticação; a assinatura é conferida antes da chave). Origem: `whatsapp_handler.py:58-105`. O 500 nunca devolve `str(e)` (desvio 008): detalhes vão ao log sem a mensagem do usuário nem o número em claro.

## 4. Pipeline

Origem: `whatsapp_handler.py:99-1554`. Depois da validação da §3, na ordem exata (cada passo que "responde" termina o processamento com 200):

1. **Sanitização.** `texto_msg = sanitize_input(texto, 1000, allow_special_chars=True)`; `numero_limpo = sanitize_input(numero_remetente.split('@')[0], 20)` (sem especiais) — `InputSanitizer` da 011. Texto que vira vazio segue o pipeline (não é erro).
2. **Usuário.** `SELECT id, nome FROM usuarios WHERE numero_whatsapp = :numero` (sem filtrar `ativo`). Não existe ⇒ responde a mensagem de cadastro (§7.1). O estado de cadastro em memória (`user_service.registration_state`) nunca é preenchido no código atual e **não é portado**.
3. **Evento pendente.** `msg_lower = texto_msg.strip().lower()`; `quer_rota = 'calcular' in msg_lower and 'rota' in msg_lower`. Se não `quer_rota` e `msg_lower` ∈ {`sim`,`confirmar`,`confirma`,`ok`,`s`} ∪ {`não`,`nao`,`cancelar`,`cancela`,`desistir`,`n`}: busca o evento pendente mais recente (§7.3). Achou ⇒ confirma (palavras do primeiro grupo) ou cancela; não achou ⇒ continua.
4. **Cálculo de rota** (`quer_rota`), §7.4. Sempre responde.
5. **Check-in noturno ativo.** `nightly_checkin_active:{numero}` existe no Redis: se o texto em minúsculas contém alguma palavra de escape (§7.9), apaga a chave e continua; senão entrega a resposta ao responsável pelo check-in e responde com o que ele devolver.
6. **Confirmação de transação.** Se `msg_lower` **não** está em {`sim`,`s`,`não`,`nao`,`n`,`desistir`} e (o texto em maiúsculas contém `CONFIRMAR`, `OK`, `TROCAR` ou `CANCELAR`, ou é só dígitos): lê `last_pending:{numero}`; havendo id, processa a resposta (§7.2) e responde; não havendo, responde um aviso fixo se o texto é exatamente `OK`/`CONFIRMAR`/`SIM`/`CONFIRMA` ou `CANCELAR`/`CANCELA`/`NÃO`/`NAO` (maiúsculas, sem `strip` adicional além do já feito), senão continua.
7. **`cancelar` solto.** Texto de uma só palavra `cancelar`/`cancela` (minúsculas) sem `last_pending` e sem evento pendente ⇒ aviso fixo (§7.1).
8. **Pagamento** (`paguei`, `quitei`, `liquidei`, `saldei` ou `zerei` em qualquer parte do texto em minúsculas): intenção implícita, antes do Gemini (§6.b).
9. **Intenção.** `get_message_intent` (§7.5; cache `intent` de 7 dias, global). Despacha por nome (§5). Nome desconhecido, `null` ou `Quitar Conta Fixa` ⇒ `"🤔 Não entendi. Tente 'gastei 50' ou 'o que você pode fazer?'."`.

Exceção em qualquer passo cai na linha 7/8 da §3. Dentro das intenções do tipo "template" (§5) a exceção **não** sobe: vira o texto da §7.1 (`❌ Ops! Algo deu errado. Por favor, tente novamente.`), como o `BaseIntent.handle` do Flask; as intenções "inline" deixam subir (cota do Gemini ⇒ 429).

## 5. Intenções e despacho

"Template" = passa por `BaseIntent.handle` (extrai parâmetros, valida, executa, formata; qualquer exceção ⇒ `❌ Ops! Algo deu errado. Por favor, tente novamente.`; erro de validação ⇒ a mensagem da validação). "Inline" = código direto do handler (exceção sobe).

| Intenção (nome do Gemini) | Tipo | Sub-entrega | Origem |
|---|---|---|---|
| `Renda`, `Despesa` | template | 012a | `transaction_intents.py:22-309` |
| `Transferência` | template | 012a | `transaction_intents.py:312-442`; **falha sempre** (desvio 151) |
| `Pagamento Fatura` | inline | 012a | `whatsapp_handler.py:1071-1112` |
| paguei/quitei... | inline | 012b | `whatsapp_handler.py:444-705` |
| `Consulta Saldo`, `Consulta Reserva`, `Consulta Detalhes Reserva`, `Consulta Potes` | inline | 012c | `whatsapp_handler.py:758-944` |
| `Listar Contas`, `Ajustar Saldo Inicial`, `Consulta Período`, `Consulta Valor Fatura`, `Consulta Categoria Específica` | template | 012c | `admin_intents.py`, `analytics_intents.py` |
| `Consulta Contas Fixas`, `Consulta Todas Contas`, `Vencimentos Hoje`, `Vencimentos Amanhã`, `Vencimentos Essa Semana`, `Contas Atrasadas` | template | 012d | `admin_intents.py`, `notification_intents.py` |
| `Criar Evento`, `Deletar Evento`, `Consultar Agenda`, `Horários Livres` (+ confirmação e rota, §4 passos 3-4) | template | 012e | `calendar_intents.py` |
| `Configurar Notificações` | inline | 012f | `whatsapp_handler.py:1192-1370` |
| `Configurar Localização`, `Configurar Relatório Mensal`, `Configurar Endereço`, `Listar Endereços`, `Deletar Endereço`, `Solicitar API Key`, `Menu de Ajuda` | template | 012f | `admin_intents.py`, `config_intents.py` |
| `Análise Inteligente`, `Comparação Mensal`, `Previsão de Gastos`, `Gráfico de Gastos` | template | 012g | `analytics_intents.py` |
| `Upload Drive` | template | 012h | `drive_intents.py` |

Registro, em ordem de verificação: o Flask usa um `if/elif` por nome exato (acentos e maiúsculas contam); `Quitar Conta Fixa` existe no prompt mas não tem ramo (desvio 160).

## 6. Regras por grupo

### 6.0 (012-0) Webhook, estado e despacho

Tudo da §3, §4 passos 1, 2, 7 e 9, o catálogo de prompts (§8) e a infraestrutura de estado no Redis (§7.2, 7.3). Entrega o despacho completo (todas as intenções registradas respondem o texto padrão "não entendi" até as sub-entregas seguintes as habilitarem, e `Menu de Ajuda` já responde).

### 6.a (012a) Transações

**Renda/Despesa** (template). Passos (`transaction_intents.py`):
1. Extrai (`extract_income_params` / `extract_expense_params`, `gemini_service.py:1724-1889`): chama `extract_transaction_details(msg, "Renda"|"Despesa")` (prompt §8, cache `extract_trans` 24 h global) ⇒ `valor_decimal`, `descricao_bruta` (padrões `null`/`"Renda"` ou `"Despesa"`). **Só** esses dois campos: `data`, `conta`, `categoria` e `parcelamento` só existem se o modelo os inventar (o prompt não os pede; desvio 154). Se `valor_decimal` é `null` e há descrição, busca agendamento recorrente (§7.7); achando e `valor_previsto` verdadeiro: `valor`, `conta_id_agendamento`, `subcategoria_id_agendamento`, `descricao_agendamento`.
2. Valida: `valor` falso ou ≤ 0 ⇒ `❌ Não consegui identificar o valor da renda. Por favor, informe o valor.` (despesa: `...da despesa...`); `< 0.01` ⇒ `❌ Valor muito baixo. Verifique se informou corretamente.`; despesa com `parcelamento` verdadeiro que não seja inteiro entre 2 e 48 ⇒ `❌ Número de parcelas inválido (deve ser entre 2 e 48).`
3. Conta: `resolve_account_for_transaction` (§7.6). Nenhuma ⇒ `❌ Conta não encontrada. Por favor, especifique a conta.`
4. Categorias do tipo (`get_user_categories`, `ORDER BY s.id`) e categoria: a do agendamento, se achou; senão `categorize_transaction` (cache `category`, 30 dias, por usuário) com o "Outros" de fallback (desvio 030).
5. Despesa: parcelamento ⇒ `valor_total = valor`, `valor_original = valor/parcelas`; `valor_db = -abs(valor_original)`. Conta cujo tipo contém `crédito` (sem diferenciar caixa) ⇒ `tipo_pagamento = "credito"` e `fatura_id` = fatura do ciclo da data (localiza ou cria, **comita antes** de seguir; desvio 065 via `InvoiceLinking`); senão `"debito"` e `fatura_id = null`. Renda: `valor_db = valor`, `tipo_pagamento = "debito"`.
6. Pendência (`pending_tx:{numero}:{id}` + `last_pending:{numero}`, TTL 300 s, §7.2) com as chaves, na ordem: `usuario_id`, `conta_id`, `conta_nome`, `conta_tipo`, `categoria_id`, `fatura_id`, `descricao`, `descricao_final` (**a `descricao_agendamento`, nula quando não houve agendamento**), `valor_db`, `valor_original`, `valor_total`, `num_parcelas`, `tipo_transacao`, `tipo_pagamento`, `data_transacao` (`yyyy-MM-dd`), `origem` (`"whatsapp"`). Falha ao gravar ⇒ `❌ Erro ao criar transação pendente`.
7. Resposta: mensagem de confirmação (`ConfirmationMessage`, 011 §7.5, com `U+FFFD` no cartão; desvio 100 proposto) com as categorias do passo 4.

**Confirmação** (§4 passo 6, §7.2): `OK`/`CONFIRMAR`/`SIM`/`CONFIRMA` grava; `TROCAR`/`MUDAR`/`ALTERAR` lista categorias numeradas; número escolhe; `CANCELAR`/`NÃO`/`NAO`/`CANCELA` descarta. Gravar = `ITransactionPosting` (004): `Renda` ⇒ receita, qualquer outro tipo ⇒ despesa; `Amount = abs(valor_db)`; descrição `descricao_final` **se a chave existe** (mesmo nula), senão `descricao` (**descrição nula ⇒ exceção ⇒ 500**, desvio 150); `num_parcelas > 1` ⇒ `PostInstallmentPurchase` (valor da parcela = `abs(valor_db)`); depois a mensagem de feedback (§7.8). A pendência é apagada **antes** de gravar (o Flask apaga ao decidir `saved`).

**Transferência** (template; `transaction_intents.py:312-442`). `extract_transfer_params` lê `c["id"]` de `Row` do SQLAlchemy 2: `TypeError` na primeira conta ⇒ a resposta de **todo** usuário com ao menos uma conta é `❌ Ops! Algo deu errado. Por favor, tente novamente.` e o Gemini nem é chamado. Usuário sem nenhuma conta segue: Gemini (`extract_transfer_details`), validações (`❌ Não consegui identificar o valor da transferência.`, `...a conta de origem. Especifique de qual conta transferir.`, `...a conta de destino. Especifique para qual conta transferir.`), contas por nome (`❌ Uma ou ambas as contas não foram encontradas.`). Mensagens de saldo insuficiente e confirmação existem no código mas são inalcançáveis; **não são portadas** (desvio 151).

**Pagamento Fatura** (inline). Contas (ativas) em `[{"nome","tipo"}]` ⇒ `extract_fatura_payment_details`; `valor_decimal`, `conta_origem` e `conta_cartao` verdadeiros senão `🤔 Não consegui identificar todos os dados do pagamento.\n\nPor favor, informe:\n• Valor pago\n• Conta de origem\n• Cartão que foi pago\n\nExemplo: *Paguei 500 reais da fatura do Nubank com o Inter*`; contas por nome (`get_account_by_name` sem fallback); alguma não achada ⇒ `❌ Não encontrei as contas mencionadas ({origem} → {cartao}).`; `PostInvoicePayment` (004: fatura `Fechada` mais antiga, desvio 010) com data de hoje; `NotACreditCard` e `SameAccount` (desvios 071 / 004) respondem o mesmo `❌ Não encontrei as contas mencionadas`; sucesso `✅ Pagamento da fatura '{cartão}' ({R$}) registrado com sucesso!`.

### 6.b (012b) Pagamentos

Origem: `whatsapp_handler.py:444-705`. `extract_payment_items` (prompt §8, sem cache) ⇒ `itens` (lista), `valor_total`, `trigger_word` (padrão `"paguei"`). Lista vazia ⇒ `🤔 Não consegui identificar o que você pagou. Pode reformular?`. Para cada item, na ordem: **conta fixa** (`BillMatching`, 011; WRatio ≥ 65) ⇒ valor = `valor_total` se há um item e `valor_total` verdadeiro, senão `valor_previsto`; valor nulo ou 0 ⇒ item sem valor; senão quita (`DirectPoster`, despesa na conta do agendamento, descrição do agendamento, hoje). **Sem conta fixa** ⇒ despesa avulsa: só com um item e `valor_total` (vários itens ou sem valor ⇒ item sem valor); categoria por `categorize_transaction`, conta por `choose_account_for_transaction` (§7.6), descrição = o item. Erro de um item é registrado e o laço segue. Respostas (§7.1): pedir valor (um ou vários), `CONTA QUITADA`, `N CONTAS QUITADAS`, `DESPESA REGISTRADA`, `N DESPESAS REGISTRADAS`, combinações e a lista de não processados. Cada lançamento é uma transação do banco (o Flask usa uma só que aborta no primeiro erro).

### 6.c (012c) Consultas financeiras

- **Consulta Saldo** (inline): contas ativas ⇒ `extract_saldo_query` (`nome_conta`); nome ⇒ `get_account_by_name` (ativa, desvio 039); não achou ⇒ `🤔 Não encontrei uma conta chamada '{nome}'.`; `get_saldo_contas` (consolidadas, ativas, `ORDER BY tipo_conta, nome_conta`); vazio ⇒ `❌ Você não tem contas cadastradas.`; uma ⇒ ícone + nome + tipo + saldo; várias ⇒ `Seus Saldos` + total geral.
- **Consulta Reserva / Detalhes Reserva** (inline): `IEmergencyReserveService` (005, desvio 014). Textos de `whatsapp_handler.py:758-850`.
- **Consulta Potes** (inline): `get_pote_status` com o período do desvio 015 (cada pote no seu período); `🏷️*{pote}*\n  Gasto: ...\n  Limite: ...\n  Resta: ...`.
- **Listar Contas**, **Ajustar Saldo Inicial** (regex do valor, nome da conta contido na mensagem; `UPDATE contas SET saldo_inicial`), **Consulta Valor Fatura** (`ensure_current_invoice_exists`, resumida ou detalhada), **Consulta Categoria Específica** (`get_category_spending`, mês de hoje), **Consulta Período** (`PeriodQueryService`, `extract_period_query` com cache `period_query`).

### 6.d (012d) Contas fixas e vencimentos

`FixedBillsService.list_pending_bills_formatted`, `list_all_bills_formatted`, `get_vencimentos_periodo` + `format_vencimentos_message` (hoje, amanhã, 7 dias), `Contas Atrasadas` (snapshot e mensagem do `NightlyCheckinService` da 013 em modo somente leitura, §7.9) e a resposta ao check-in noturno (`parse_checkin_response`, `process_response`, `mark_bills_as_paid`). "Já pago no mês" continua por **igualdade de `descricao`** (o desvio 012 não vale aqui: a quitação do WhatsApp/SMS não grava `agendamento_id`; Q-012-3).

### 6.e (012e) Calendário

Criar evento (pendência de evento, TTL 300 s, confirmação/cancelamento/rota no §4), deletar (busca em 90 dias, um achado deleta, vários listam), consultar agenda (`CalendarQueryService` sobre `ICalendarClient`), horários livres (`FreeTimeFinder`, `UserSchedulePatternService`, sugestão da IA opcional).

### 6.f (012f) Configurações

`Configurar Notificações` (inline, resumo matinal, check-in noturno e o ramo do Gemini), `Configurar Localização`, `Configurar Relatório Mensal`, endereços favoritos, `Solicitar API Key` (devolve o valor da coluna como está; desvio 153), `Menu de Ajuda`.

### 6.g (012g) Análises e gráfico

`Análise Inteligente` (Gemini com `UserId`, desvio 063), `Comparação Mensal`, `Previsão de Gastos` (Gemini com `UserId`), `Gráfico de Gastos` (PNG por `IChartRenderer`, enviado por `IBotClient.SendImageAsync` — desvio 026).

### 6.h (012h) Drive

`Upload Drive`: pasta do texto da mensagem, escopo do Drive, validação e upload por `IDriveClient`.

## 7. Regras transversais

### 7.1 Textos fixos

Os textos de resposta ao usuário saem dos mesmos literais do Python (origem: arquivo e linha na tabela de cada sub-entrega no `plan.md`). Os principais:
- Cadastro (`whatsapp_handler.py:122-128`): `Olá! 👋 Parece que você ainda não tem cadastro no Meu Secretário.\n\nPara começar a usar, crie sua conta em nosso site:\nhttps://app.meusecretario.com/register\n\nApós criar sua conta, volte aqui e eu estarei pronto para ajudar! 😊`
- Nada pendente para confirmar: `✅ Não encontrei nada pendente para confirmar.\n\nSe quer registrar algo, diga:\nExemplo: 'gastei 50 em comida'`; para cancelar: `❌ Não encontrei nada pendente para cancelar.\n\nSe quer deletar um evento específico, diga qual:\nExemplo: 'Deletar academia de hoje'`.
- Genérico de intenção: `❌ Ops! Algo deu errado. Por favor, tente novamente.` (`base_intent.py:~190`).
- Não entendi: `🤔 Não entendi. Tente 'gastei 50' ou 'o que você pode fazer?'.`

### 7.2 Pendência de transação no Redis

Formato da 011 (`PendingTransactionStore`): valor JSON em `pending_tx:{numero}:{id}` e o `id` (JSON de texto) em `last_pending:{numero}`, ambos TTL 300 s, `id` = 8 primeiros caracteres de um UUID v4. A 012 lê e **regrava o objeto como veio** (preserva chaves desconhecidas) e acrescenta `awaiting_category_selection` e `categoria_map`. `process_confirmation_response` (porte literal de `transaction_confirmation_service.py:224-296`): mensagem em maiúsculas com `strip`; sem pendência ⇒ `error` `⏱️ Esta transação expirou ou não existe mais. Registre novamente.`; aguardando categoria: `int()` do Python (11) escolhe do mapa (`saved` com a categoria trocada, pendência apagada), número fora do mapa ⇒ `error` `Número inválido. Escolha um número da lista ou digite CANCELAR.`, `CANCELAR` ⇒ `cancelled` `❌ Transação cancelada.`, outro texto ⇒ `error` `Digite um número válido da lista ou CANCELAR.`; fora disso `CONFIRMAR|OK|SIM|CONFIRMA` ⇒ `saved`; `TROCAR|MUDAR|ALTERAR` ⇒ marca aguardando e regrava (TTL 300) ⇒ `awaiting_category`; `CANCELAR|NÃO|NAO|CANCELA` ⇒ `cancelled`; demais ⇒ `error` `Comando não reconhecido. Digite: CONFIRMAR, TROCAR ou CANCELAR.`. Lista de categorias: `📂 *ESCOLHA A CATEGORIA CORRETA:*\n\n` + macros na ordem de primeira aparição, subcategorias numeradas de 1 em diante.

### 7.3 Evento pendente

`pending_event:{numero}:{id}` (JSON, TTL 300 s, sem `last_pending`). "Mais recente" = **última** chave devolvida pelo `KEYS pending_event:{numero}:*` (ordem do Redis, não garantida; desvio 155). Confirmar: lê de novo, cria o evento (`ICalendarClient.InsertEventAsync`), apaga a pendência só se criou. Textos: `✅ *Evento Criado!*\n\n📝 {titulo}\nEvento '{titulo}' criado com sucesso!`; falha `❌ Erro ao criar evento: {motivo}` (motivo só para "usuário sem Google conectado", o texto literal da 010; qualquer outro erro vira `❌ Erro ao criar evento.`, desvio 008); cancelar `✅ Evento cancelado com sucesso!`.

### 7.4 Cálculo de rota

Requer evento pendente: senão `❌ Não encontrei nenhum evento pendente de confirmação.`; sem `localizacao` ⇒ `❌ Este evento não tem localização definida, não posso calcular tempo de deslocamento.`; endereços do usuário (`usuario_id` do evento) ordenados `casa`, `trabalho`, `outro`: nenhum ⇒ aviso com exemplo; um ⇒ geocodifica o destino (`IRouteClient`, chave do sistema) e calcula; vários ⇒ pergunta `🚗 *Qual endereço usar como origem?*` (a resposta `casa`/`trabalho`/`outro` **não** é tratada, desvio 156). O limite diário de rotas **não** é aplicado (desvio 055).

### 7.5 Intenção (Gemini)

Prompt `get_message_intent` (§8). Resposta: JSON `{"intent": "..."}`; `null`/ausente ⇒ "não entendi". JSON inválido ou resposta vazia ⇒ exceção (429/500 da §3). Falhas do Gemini mapeiam como nas demais rotas (RN-011-05: textos de chave/cota/permissão já escritos para o usuário).

### 7.6 Escolha de conta

`extract_mentioned_account` (palavras-chave `com o `, `com a `, ..., `da ` na ordem da lista; primeira que está no texto; até 4 palavras depois dela; `FuzzyScorer.ExtractOne` com `WRatio` e corte 60 sobre os nomes das contas ativas) ⇒ conta padrão do tipo (`usuarios.conta_padrao_renda_id`/`..._despesa_id`, só se ativa) ⇒ primeira conta ativa (`ORDER BY id`). `resolve_account_for_transaction` acrescenta a conta do agendamento (se ainda ativa) e o nome explícito. Só contas **ativas** (desvio 039).

### 7.7 Agendamento recorrente por descrição

`AgendamentosQueries.get_lembrete_variavel_by_description`: ativo, `FIXO`/`LEMBRETE_VARIAVEL`, `descricao ILIKE %x%`, ordem: igualdade exata, `LEMBRETE_VARIAVEL` antes de `FIXO`, `descricao`; `LIMIT 1`.

### 7.8 Feedback da transação salva

`gerar_feedback_transacao`: cabeçalho `✅ Transação Salva!`, bloco do pote (período do desvio 015 e só se o alerta dispara: `alerta_potes_ativo`/`alerta_potes_threshold`), rodapé de fatura (cartão) ou saldo (demais). Qualquer exceção ⇒ `✅ Transação Salva com Sucesso!`.

### 7.9 Check-in noturno (colisão com a 013)

A 013 entregou o job, o snapshot (`NightlyCheckinService.CollectSnapshotAsync`), o formatador (`FormatConsolidatedMessage`, com `checkinId` nulo = modo leitura) e a sessão no Redis (`nightly_checkin:{numero}:{id}`, formato JSON do Python). A 012 **reutiliza** esse serviço para `Contas Atrasadas` e implementa o que a 013 deixou: `INightlyCheckinResponder` (`parse_checkin_response`: `ok`/👍/✅ ⇒ todos, `não`/`depois` ⇒ nenhum e encerra, números ⇒ parcial, inválido ⇒ encerra; `process_response`; `mark_bills_as_paid` via `IScheduleExecutor`, com a data do vencimento real se o atraso é ≤ 1 dia, senão hoje; sessão sempre apagada, inclusive em exceção). Até o responder existir (012d) o padrão devolve "não tratada". Palavras de escape (`nightly_checkin_service.py:140-152`): `gastei`, `gasto`, `comprei`, `compra`, `paguei`, `pagamento`, `recebi`, `recebimento`, `transferi`, `transferência`, `transferencia`, `saldo`, `extrato`, `consulta`, `quanto`, `valor`, `add`, `criar`, `registrar`, `lançar`, `lancar`, `agendar`, `evento`, `compromisso`, `agenda`, `horário`, `horario`, `config`, `configurar`, `mudar`, `alterar`.

### 7.10 Convenções

- `date.today()`/`datetime.now()` do Python viram `IClock` (São Paulo); `CURRENT_DATE` do banco (UTC) viram parâmetro `IClock.Today` (só difere entre 21h e 24h).
- Dinheiro: `formatar_moeda` (011 `MoneyText.Brl`); `{:,.2f}` do Python nos prompts de análise (`MoneyText.Grouped2`); nomes de dia da semana em inglês nas análises (desvio 009).
- Queries de várias linhas sem `ORDER BY` no Flask ganham `ORDER BY id`.
- Nada de `str(e)` em resposta nem em log (desvio 008); nada de número de telefone nem texto do usuário em log (`PhoneMask` da 011 no número).

## 8. Prompts do Gemini

Cada prompt é um modelo de texto versionado (`Domain/Whatsapp/Prompts/*.txt`, recurso embutido) **gerado** de `gemini_service.py` por `tests/Fixtures/generate_whatsapp_fixtures.py`, com marcadores no lugar das variáveis (`⟦MSG⟧`, `⟦CONTAS⟧`...). Os testes comparam o prompt renderizado com o gerado pelo Python para entradas de teste (aspas, acento, chaves, emoji). Prompts e cache:

| Função (Flask) | Cache | Observação |
|---|---|---|
| `get_message_intent` | `intent`, 7 d, global | |
| `extract_transaction_details` | `extract_trans`, 24 h, global | (011 já porta) |
| `categorize_transaction` | `category`, 30 d, usuário | (011) |
| `extract_transfer_details`, `extract_fatura_payment_details`, `extract_fatura_query`, `extract_saldo_query` | — | recebem `json.dumps(contas)` |
| `extract_category_query`, `extract_calendar_query`, `extract_event_deletion_query`, `extract_time_filter_query`, `extract_notification_config`, `extract_free_time_query`, `extract_monthly_report_config`, `extract_location_config`, `extract_address_config`, `extract_address_label_from_deletion`, `extract_payment_items` | — | |
| `extract_period_query` | `period_query`, 7 d, global | |
| `extract_chart_type` | `chart_type`, 7 d, global | |
| `extract_event_creation_details` | — | **desvio 062**: a linha `Hoje é {yyyy-MM-dd} ({dia da semana em português})` entra depois da primeira linha |
| `suggest_best_slot_with_ai`, `generate_ai_insights`, `generate_forecast_insights` | — | **desvio 063**: com `UserId` |

Toda chamada leva `UserId` (chave própria do usuário, como o Flask que passa `usuario_id`), exceto as três do desvio 063 que no Flask não passavam e agora passam.

## 9. Desvios

### 9.1 Aprovados e aplicáveis

| # | Desvio | Como se aplica |
|---|---|---|
| 001 | Receita × Renda | o WhatsApp só usa `Renda`/`Despesa` do banco; consultas somam `Renda` |
| 008 | vazamento | nenhum `str(e)`; mensagens de falha de calendário/endereço/localização sem o motivo; logs sem PII |
| 009 | dia da semana por `lc_time` | nome calculado no código, em inglês (como hoje) |
| 010 | qual fatura é paga | `PostInvoicePayment` |
| 014 | reserva única | `IEmergencyReserveService` |
| 015 | período do pote | `BudgetPeriods` em feedback e `Consulta Potes` |
| 024 | corpo sem `Content-Type` | 400 `Dados faltando` |
| 026 | URL do bot do gráfico | `IBotClient` |
| 030 | `Outros` por `ORDER BY id` | `ICategoryLookup` |
| 039 | só contas ativas | todas as buscas de conta |
| 053 | link de conexão assinado | `GoogleLinks.ConnectUrl` na mensagem do Drive e da agenda |
| 055 | limite de rotas | portado e **sem uso** |
| 059 | texto > 4096 | `IBotClient` divide (o gráfico não) |
| 062 | prompt do evento com a data de hoje | §8 |
| 063 | Gemini com usuário | §8 |
| 065, 071 | vencimento da fatura, pagamento exige cartão | via 004 |

Não aplicáveis: 012/013/011 (parcelas e "já pago" por `agendamento_id`) — a quitação do WhatsApp não grava `agendamento_id` (Q-012-3); 099 e 100 são `proposto` (a 012 reproduz o Flask).

### 9.2 Propostos (novos; **não** implementados)

Numeração a partir de 150 (130+ é da 013; 120-122 da 014). Arquivos em `parity-deviations/`, estado `proposto`.

| # | Título | Recomendação |
|---|---|---|
| 150 | Confirmar despesa/renda por WhatsApp falha quando não há agendamento (descrição nula) | **aprovar com urgência** (é o fluxo central) |
| 151 | Transferência por WhatsApp falha sempre | aprovar (implementar a transferência de verdade) |
| 152 | `Configurar Notificações` pelo ramo do Gemini grava e responde 500 | aprovar |
| 153 | `Solicitar API Key` devolve o valor cru da coluna (cifrado para quem se cadastrou depois da 017) | aprovar (decifrar) |
| 154 | A extração de despesa/renda não pede data, conta, categoria nem parcelamento | aprovar |
| 155 | Evento pendente "mais recente" depende da ordem de `KEYS` | aprovar |
| 156 | Resposta `casa`/`trabalho`/`outro` à pergunta de origem da rota não é tratada | aprovar |
| 157 | `Pagamento Fatura` é inalcançável com "paguei..." | decidir (produto) |
| 158 | Análise e previsão: valores de despesa negativos, desempate aleatório e `KeyError` sem histórico | aprovar |
| 159 | Upload ao Drive: o motivo específico da falha nunca é mostrado | aprovar |
| 160 | `Quitar Conta Fixa` está no prompt do Gemini mas não tem ramo | aprovar (remover do prompt) |
| 161 | Vencimentos e contas fixas pendentes comparam descrição sem filtrar o usuário | aprovar |

## 10. Critérios de aceite

Convenção: `AC-012-NNN`, faixa por sub-entrega (0xx = 012-0, 1xx = 012a, 2xx = 012b, 3xx = 012c, 4xx = 012d, 5xx = 012e, 6xx = 012f, 7xx = 012g, 8xx = 012h). O harness de paridade (`tests/Fixtures/whatsapp_handler_harness.py`) executa o `WhatsAppHandler` real do Flask com serviços simulados e grava, por cenário, resposta e sequência de chamadas; `WhatsAppPipelineParityTests` reproduz os mesmos cenários.

**012-0 — Webhook, estado e despacho**
- **AC-012-001** Dado banco fora, então 503 `Banco de dados temporariamente indisponível` antes de checar chave.
- **AC-012-002** Dado `GEMINI_API_KEY` vazia, então 503 `{"status":"erro","mensagem":"Serviço não configurado"}`.
- **AC-012-003** Dado `X-Webhook-Signature` inválida (mesmo com `x-api-key` certa), então 401 `Assinatura inválida`; assinatura ausente é aceita; HMAC calculado sobre os bytes brutos (espaços e acentos do corpo contam).
- **AC-012-004** Dado `x-api-key` ausente ou errada, então 401 `Não autorizado`; certa passa; comparação em tempo constante.
- **AC-012-005** Dado corpo vazio, não JSON, sem `Content-Type`, lista, ou `texto`/`numero_remetente` falsos, então 400 `Dados faltando`; `texto` numérico ⇒ 500 `Erro ao processar.`.
- **AC-012-006** Dado `numero_remetente` `5531999990001@s.whatsapp.net`, então o número usado é `5531999990001`; caracteres `<>{}\$`|;&` são removidos do número e o texto preserva especiais, máximo de 1000 e 20 caracteres.
- **AC-012-007** Dado usuário não cadastrado, então 200 com a mensagem de cadastro.
- **AC-012-008** Dado erro com `429`/`quota`/`rate limit` na mensagem em qualquer passo fora de intenção template, então 429 com o texto da §3; outro erro ⇒ 500 `Erro ao processar.` sem vazar a mensagem.
- **AC-012-009** Dado intenção template que lança, então 200 com `❌ Ops! Algo deu errado. Por favor, tente novamente.`; dado intenção inline que lança cota ⇒ 429.
- **AC-012-010** Dado `Menu de Ajuda`, então o menu idêntico ao do Python (fixture).
- **AC-012-011** Dado intenção `null`, desconhecida ou `Quitar Conta Fixa`, então `🤔 Não entendi...`.
- **AC-012-012** Dado `cancelar` sem pendência de transação e sem evento, então o aviso de cancelamento; com pendência, segue a confirmação.
- **AC-012-013** Dado resposta de confirmação (`OK`, `ok`, `CONFIRMAR`) sem `last_pending`, então o aviso `Não encontrei nada pendente para confirmar...`; `TROCAR` ou dígitos sem pendência seguem para o Gemini.
- **AC-012-014** Dado `sim`/`s`/`não`/`n`/`desistir`, então a confirmação de **transação** nunca roda (só a de evento).
- **AC-012-015** Dado `nightly_checkin_active` e palavra de escape no texto, então a chave é apagada e o pipeline continua; sem palavra de escape o responsável pelo check-in decide a resposta.
- **AC-012-016** Prompts de todas as chamadas desta feature renderizam idênticos ao Python (fixtures), com `UserId` e política de cache da §8.
- **AC-012-017** O pipeline reproduz o Flask nos cenários do harness (ordem dos passos, respostas e chamadas).
- **AC-012-018** Estado Redis: pendência lida e regravada preserva chaves desconhecidas; `process_confirmation_response` bate com o Python em todos os ramos (fixtures).

**012a — Transações**
- **AC-012-101** Despesa "gastei 50 na padaria": Gemini extrai, conta/categoria resolvidas, pendência gravada (chaves e ordem), resposta = mensagem de confirmação (fixture).
- **AC-012-102** Renda análoga; valor positivo; `tipo_pagamento` `debito`.
- **AC-012-103** Valor ausente, zero, negativo ou < 0,01 ⇒ mensagens da §6.a; despesa com parcelamento inválido ⇒ mensagem.
- **AC-012-104** Valor nulo com agendamento recorrente por descrição ⇒ usa valor, conta e categoria do agendamento (e `descricao_final` do agendamento).
- **AC-012-105** Cartão ⇒ `tipo_pagamento` `credito` e `fatura_id` da fatura do ciclo (criada se faltar, comitada antes do Redis).
- **AC-012-106** Sem contas ⇒ `❌ Conta não encontrada...`; Redis fora ⇒ `❌ Erro ao criar transação pendente`.
- **AC-012-107** Conta mencionada (`no nubank`) vence a padrão do usuário; padrão vence a primeira; inativa nunca é usada (desvio 039).
- **AC-012-110** `OK` grava (`valor` com sinal pelo tipo, categoria, data, conta) e responde o feedback (fixture); a pendência e `last_pending` somem.
- **AC-012-111** `TROCAR` lista as categorias, regrava a pendência com `awaiting_category_selection` e `categoria_map`; o número seguinte grava com a categoria escolhida; número fora do mapa e texto não numérico respondem os erros fixos.
- **AC-012-112** `CANCELAR` descarta e responde `❌ Transação cancelada.`; pendência expirada ⇒ `⏱️ Esta transação expirou...`.
- **AC-012-113** Pendência sem agendamento (`descricao_final` nulo) ⇒ 500 `Erro ao processar.` e nada gravado (desvio 150).
- **AC-012-114** Parcelada (`num_parcelas > 1`) ⇒ `PostInstallmentPurchase` (1ª parcela + agendamento `PARCELADO`).
- **AC-012-115** Feedback: pote (período do desvio 015, alerta por threshold), rodapé de fatura ou saldo; erro ⇒ `✅ Transação Salva com Sucesso!`.
- **AC-012-120** `Transferência` com contas ⇒ `❌ Ops!...` sem chamar o Gemini; sem contas ⇒ cadeia de validações.
- **AC-012-130** `Pagamento Fatura`: dados incompletos, contas não achadas, não cartão, sucesso (par `Pagamento Fatura`, fatura `Paga`).

**012b — Pagamentos**
- **AC-012-201** Um item que casa com conta fixa e valor informado ⇒ quitação com o valor informado; sem valor ⇒ `valor_previsto`; valor previsto nulo ⇒ pede valor.
- **AC-012-202** Vários itens: quitação de cada um com `valor_previsto`; `valor_total` não divide.
- **AC-012-203** Item sem conta fixa: com um item e valor ⇒ despesa avulsa (categoria, conta, descrição = item); sem valor ou vários itens ⇒ lista de "sem valor".
- **AC-012-204** Todas as variantes de resposta (fixtures): uma/várias quitadas (diferença maior que 1 real mostra previsto), uma/várias despesas, combinadas, não processadas, pedir valor (um e vários).
- **AC-012-205** Erro em um item não impede os seguintes; lista vazia ⇒ `Não consegui identificar o que você pagou`.
- **AC-012-206** Palavras de pagamento em qualquer parte do texto (`paguei`, `quitei`, `liquidei`, `saldei`, `zerei`) disparam o fluxo antes da intenção.

**012c — Consultas**
- **AC-012-301** Saldo de uma conta, de todas, conta inexistente, sem contas (fixtures de texto).
- **AC-012-302** Reserva e detalhes (todas as periodicidades; vazio).
- **AC-012-303** Potes (nenhum, vários; período por pote).
- **AC-012-304** Listar contas agrupadas por tipo; vazio.
- **AC-012-305** Ajustar saldo: valor com milhar e vírgula, conta contida na mensagem, falta de valor, falta de conta (lista), sucesso.
- **AC-012-306** Fatura resumida (uma, várias, nenhuma) e detalhada (agrupada por data, parcelas, ícones); cartão não achado.
- **AC-012-307** Categoria específica e período (hoje, ontem, fim de semana, semana, 7 dias, mês, mês passado; com categoria; vazio).

**012d — Contas fixas e vencimentos**
- **AC-012-401** Contas fixas pendentes (despesas, cartão, receitas, totais, status por dia) e catálogo completo (periodicidades, fatores).
- **AC-012-402** Vencimentos hoje, amanhã e 7 dias (virada de mês, anual, receitas × despesas × faturas, totais).
- **AC-012-403** Contas atrasadas: snapshot (pendentes 7 dias, atrasadas, faturas) e texto somente leitura; vazio ⇒ `✅ Você não tem contas atrasadas! Tudo em dia! 🎉`.
- **AC-012-404** Resposta ao check-in: `ok`/👍/✅ confirma todos; `1`, `1 e 3`, `1, 2, 4` parcial; `não`/`depois` encerra sem pagar; inválido encerra; a sessão some sempre; contas pagas viram transações com `agendamento_id` (data do vencimento real se atraso ≤ 1 dia, senão hoje).

**012e — Calendário**
- **AC-012-501** Criar evento: validação, `hoje`/`amanha`/ISO, pendência de evento, mensagem de confirmação (com/sem endereços).
- **AC-012-502** `sim` cria, `não` cancela, falha de Google (sem conexão e outras), sem evento pendente.
- **AC-012-503** `calcular rota`: sem evento, sem local, sem endereços, um endereço (rota calculada), vários (pergunta), destino não localizado, rota falha.
- **AC-012-504** Deletar evento: não achou, um (deleta), vários (lista), filtro `hoje`/`amanha`.
- **AC-012-505** Consultar agenda (um dia, período, filtro de horário); sem Google ⇒ mensagem de conexão.
- **AC-012-506** Horários livres: slots, qualidade, formato da mensagem e sugestão da IA.

**012f — Configurações**
- **AC-012-601** Resumo matinal e check-in noturno: ativar/desativar/configurar, regex da hora, faixa 18–23, mensagens.
- **AC-012-602** Ramo do Gemini (`agenda_diaria`, `contas_vencer`): grava e responde 500 (desvio 152); tipo desconhecido ⇒ mensagem.
- **AC-012-603** Localização, relatório mensal (consultar, ativar, desativar, configurar, erros), endereços (salvar com geocodificação, listar na ordem casa/trabalho/outro, deletar), API key, menu.

**012g — Análises**
- **AC-012-701** Prompt de análise, previsão e sugestão idênticos aos do Python para dados fictícios; `UserId` passado; rodapé; erro do Gemini ⇒ `❌ Ops!...`.
- **AC-012-702** Comparação mensal (variação, top 5, vazio).
- **AC-012-703** Gráfico: pizza, barras, linha, sem dados, falha no envio; imagem PNG válida enviada pelo bot com a legenda.

**012h — Drive**
- **AC-012-801** Pasta extraída (padrões, separadores, caixa preservada, sanitização, padrão `WhatsApp Uploads`).
- **AC-012-802** Sem arquivo, sem tipo, sem escopo do Drive (link assinado), erro de autenticação, arquivo inválido, base64 inválido, sucesso (mensagem), falha de upload (mensagem genérica, desvio 159).

## 11. Tabelas e dados tocados

Leitura: `usuarios`, `contas`, `faturas`, `transacoes`, `subcategoria`, `macrocategoria`, `grupocategoria`, `agendamentos`, `potesdegastos`, `potesubcategorias`, `notificationconfigs`, `monthlyreportconfigs`, `enderecosfavoritos`, `googlecalendartokens`. Escrita: `transacoes`/`faturas`/`agendamentos` **só** pelas portas da 004; `contas.saldo_inicial`; `usuarios.cidade`/`estado`; `notificationconfigs`; `monthlyreportconfigs`; `enderecosfavoritos` (via 007). Redis: `pending_tx:*`, `last_pending:*`, `pending_event:*`, leitura/remoção de `nightly_checkin_active:*`. Sem migration.

## 12. Fora de escopo

- Cadastro pelo WhatsApp (código morto) e `UserRegistrationState`.
- Resumo matinal, relatórios mensais e jobs (013); `daily_briefing`.
- Funções do Flask sem chamador: `extract_bill_payment`, `extract_parcelamento_info`, `validate_event_location_completeness`, `get_category_comparison`, `get_category_forecast`, `generate_simple_forecast_text`, `ConsultaSaldoIntent`/`ConsultaReservaIntent`/`ConsultaPotesIntent`/`ConfigurarNotificacoesIntent`/`PagamentoFaturaIntent` (classes do registro que o handler não usa).
- Aplicar o limite diário de rotas (desvio 055).
- Criação da sessão e o job do check-in (013, já entregues).

## 13. Divergências conhecidas em relação ao Flask

1. **Gráficos:** o Flask desenha com Matplotlib; o .NET com ScottPlot (tech-stack). A imagem não é igual byte a byte (estilo, fonte); dados, título e legenda iguais.
2. **Erros sem `str(e)`** (desvio 008): `❌ Erro ao criar evento: {str(e)}`, `❌ Erro ao deletar evento: ...`, `❌ Erro ao consultar agenda: ...`, `❌ Erro ao salvar endereço: ...`, `Erro ao salvar localização: ...`, `Erro ao atualizar configuração: ...` perdem o motivo.
3. **Pagamentos em lote:** uma transação do banco por item (o Flask usa uma que aborta no primeiro erro e responde como se tivesse gravado tudo).
4. **`NotACreditCard`/`SameAccount`** no `Pagamento Fatura`: o Flask aceita qualquer conta como cartão; aqui responde `Não encontrei as contas mencionadas` (desvio 071 no serviço da 004).
5. **Ordem sem `ORDER BY`** do Flask passa a `id` (contas, agendamentos, categorias); chaves de cache dos prompts mudam só se a ordem física era outra (cache descartado na virada).
6. **`datetime.now()`/`date.today()` do container** e `CURRENT_DATE` do banco (UTC) viram o dia de São Paulo (constituição §7).
7. **Estatísticas aleatórias** (`set` do Python em `get_monthly_comparison`): desempate determinístico (ordem de aparição).
8. **Event "mais recente":** a ordem dos `KEYS` do Redis não é garantida em nenhuma das duas implementações (Q-012-4).
9. **Check-in, confirmação em lote (012d):** cada conta confirmada vira uma transação do `IScheduleExecutor` (desvio 011, idempotente por mês); o Flask grava todas numa só transação que desfaz tudo no primeiro erro. Aqui as contas anteriores à que falhou continuam gravadas, a sessão some e a resposta é `❌ Erro ao salvar transações.` (sem `str(e)`, desvio 008). Conta já lançada no mês conta como confirmada sem duplicar.
10. **Check-in, confirmação parcial (012d):** `list(set(indices))` do Python sai em ordem crescente para números pequenos; o .NET sempre ordena em crescente (a ordem do `set` com 8 itens ou mais depende da inserção).

## 14. Perguntas em aberto (siga o Flask até haver resposta)

- **Q-012-1** O banco de produção aceita `transacoes.descricao` nula? Se sim, a confirmação por WhatsApp grava sem descrição; se não (o dump diz `NOT NULL`), todo `OK` de despesa/renda sem agendamento dá 500 (desvio 150). **Prioridade máxima.**
- **Q-012-2** `Solicitar API Key` deve decifrar a chave (desvio 153) ou o produto prefere um link seguro? Entregar uma chave por WhatsApp é decisão de produto.
- **Q-012-3** A quitação por WhatsApp/SMS deve gravar `agendamento_id` (coerente com o desvio 012 e a 013)? Padrão: não.
- **Q-012-4** Qual evento pendente é "o mais recente"? Hoje a ordem do `KEYS` decide (desvio 155).
- **Q-012-5** Alguém usa `Transferência` pelo WhatsApp? Hoje responde "Algo deu errado" para todos.
- **Q-012-6** (resolvida) A 013 entregou o snapshot e o formatador; a 012 os reutiliza e implementa só a resposta ao check-in.
- **Q-012-7** O motivo específico dos erros do Google (`Erro ao criar evento: ...`) pode ser omitido (desvio 008) ou o produto quer uma lista de motivos permitidos?
- **Q-012-8** A migração da matplotlib para ScottPlot (já na 013) é aceitável para barras e linha também (imagem diferente, dados iguais)?
