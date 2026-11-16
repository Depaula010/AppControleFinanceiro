# Auditoria do Meu Secretário, 2026-11-16 (QA + Produto + Financeiro)

## Contexto
Três agentes leram o backend .NET (`dotnet/`) e o front Angular (`meusecretariofront`) sem alterar nada nem acessar
produção:
- **QA:** defeitos técnicos.
- **Produto:** aderência às specs, o que sobra e o que falta.
- **Financeiro:** exatidão das contas.

Os itens marcados ✔ eu conferi pessoalmente no código. Antes de reportar, cada agente comparou com o índice de
desvios, para não tratar como bug o que é intencional.

**Saúde geral:**
- Build com 0 avisos; **3.303 testes unitários passando**.
- Integração não rodou (Docker desligado).
- Nenhum `DateTime.Now`, SQL todo parametrizado, isolamento por usuário correto nos UPDATE/DELETE, telefone
  mascarado nos logs.
- `InvoiceCycle`, motor de agendamentos, saldos (`saldo_inicial + SUM`), parser do check-in e reserva de emergência
  conferem com os casos testados à mão.

---

## ALTO (6)

### A1 ✔ "paguei 500 da fatura" vira despesa avulsa: dinheiro contado duas vezes e fatura continua aberta
- `PaymentStep.cs:42` captura qualquer "paguei/quitei" antes do Gemini, então a intenção `Pagamento Fatura` nunca
  roda (desvio 157, adiado).
- O próprio bot ensina a frase que falha:
  - `help-menu.txt:14` ("paguei 500 da fatura do nubank");
  - `CheckinReplyTexts.cs:90`, quando o check-in não consegue pagar ("Registre manualmente: paguei a fatura do X com
    [conta]").
- **Correção:** implementar o 157 (se o texto citar "fatura", desviar para o pagamento de fatura).

### A2 ✔ Populador põe a conta de cartão na fatura de um mês antes quando o dia é depois do fechamento
- `InvoiceScheduleRules.cs:26` usa o mês do **vencimento** como data e é chamado com a data de vencimento da fatura
  (`AutomationServices.cs:211`).
- **Caso (Inter, fecha 13 e vence 20):** conta fixa no cartão com dia 15.
  - Hoje: "15/11" entra na fatura de 20/11, que já fechou em 13/11.
  - Pela `InvoiceCycle.ForPurchase`, deveria ir para a de 20/12.
  - Num parcelado, a última parcela cai uma fatura antes.
- `data_inicio` futuro também é ignorado na consulta (`AutomationStores.cs:207-224`).
- Contradiz o desvio 065 ("uma função única para compra, populador e pagamento").
- **Precisa de desvio novo.**

### A3 ✔ Segundo pagamento de fatura marca como "Paga" a fatura do ciclo, ainda aberta
- `TransactionPosting.cs:177-196`: sem fatura Fechada, o pagamento vai para a fatura do ciclo da data e a marca como
  Paga sem comparar o valor.
- **Caso:** paga 600 em 18/11 e 400 em 20/11. A fatura de 20/12 (Aberta) vira Paga e some do aviso de fechamento,
  do aviso de vencimento, do check-in e do "minha fatura".
- **Correção:** sem Fechada, só vincular o pagamento, sem mudar o status. **Desvio.**

### A4 ✔ "paguei X" e SMS de conta fixa ou variável gravam sem `agendamento_id` nem competência
- `BillSettlement.cs:23-28`: só o parcelado passa pelo executor.
- **Efeitos:**
  - Conta semanal, quinzenal ou diária paga pelo "paguei" não sai do check-in. Se o usuário confirmar lá também, a
    despesa entra duas vezes, e o índice 0090 não pega porque o `agendamento_id` fica nulo.
  - Conta paga com atraso "quita" o mês errado.
- Ficou mais grave depois do desvio 272, porque conta de fora do cartão agora depende de confirmação.
- **Correção:** fixa e variável também pelo executor, com competência igual ao vencimento em aberto. **Desvio.**

### A5 ✔ Front: tela de contas bancárias inteira quebrada (G-8)
- `account-modal.component.ts:377-381` envia `{nome, tipo:'corrente', saldo, cor, limite}`, e o
  `finances.service.ts:182` repassa sem traduzir. O backend exige `nome_conta`, `tipo_conta: "Conta Corrente"`,
  `saldo_inicial`, `cor_hex` e `limite_credito`.
- **Efeitos:**
  - **Criar:** sempre 400.
  - **Listar:** `accounts.component.ts:66-69` agrupa por `'corrente'`, então nenhum card aparece.
  - **Editar:** 200 sem mudar nada.
- O modal mostra `err.error?.message`, mas o service já virou `Error`, então a mensagem é sempre a genérica.
- `Ajustar Saldo Inicial` do bot existe (`QueryIntents.cs:141`) mas não está no `intent.txt`.
- **Resultado: não há canal funcional para criar ou editar conta.**

### A6 ✔ Front: Extrato com saldo do período errado e edição de transação travada
- `transactions.component.ts:86-107`: a despesa já vem negativa, e `receitas - despesas` soma as duas.
  - **Caso:** receita 5.000 e despesa −1.200 mostram saldo 6.200 (correto: 3.800).
- Os totais são só da página (50 itens).
- Transferência e Pagamento Fatura aparecem como "Despesa".
- **Editar:** `transaction-modal.component.ts:250-256` põe `-50` num campo com `min(0.01)` e não preenche conta nem
  subcategoria, então o formulário nasce inválido.

---

## MÉDIO (13)

| # | Achado | Onde | Tipo |
|---|---|---|---|
| M1 ✔ | O relatório de fatura no WhatsApp conta o **pagamento** como compra e como estorno, e ele aparece no top de categorias | `ReportsReader.cs:56-94` (só filtra `fatura_id`; o pagamento grava as duas pernas com `fatura_id`, `TransactionPosting.cs:182-189`) | bug puro |
| M2 | O total da fatura **não desconta estorno** (só despesa é ligada à fatura): compras de 500 e estorno de 100 fazem o check-in pagar 500 | `TransactionPosting.cs:253`, `JobsQueries.cs:169,322,370`, `CheckinQueries.cs:149,163` | desvio |
| M3 | **Apagar uma parcela** não volta o contador: o populador relança "(4/10)" na mesma fatura e o parcelado termina uma parcela antes | `TransactionRepository.cs:158-212` | desvio |
| M4 | **Parcelamento pelo WhatsApp perde ou ganha centavo**: 100 em 3x = 99,99 e 200 em 3x = 200,01 (divide em `double`) | `TransactionIntentRules.cs:174-179` | desvio |
| M5 | "ajustar saldo inicial C6 1500" grava **R$ 6**; "5000.50" vira 500.050; "-300" vira +300 (pega o 1º número e apaga os pontos) | `FinanceQueryMessages.cs:206-250` | bug (e hoje inalcançável, A5) |
| M6 | **Três regras diferentes de "conta paga" e "a vencer"**: check-in usa competência; "Contas Fixas" compara descrição e mês; o dashboard usa outra regra e nunca mostra atrasada | `SchedulePaymentSql.cs`, `FixedBillsReader.cs:101-186`, `DashboardRepository.cs:155`, `DashboardAlerts.cs:45-133` | desvio |
| M7 | Migration **0090** pode ficar registrada como aplicada sem criar o índice único (com duplicata só emite WARNING), e a trava do desvio 258 fica sem efeito em silêncio | `0090_transacoes_competencia_unica.sql`, `MigrationRunner.cs:50-60` | bug; **conferir em produção**: `SELECT 1 FROM pg_indexes WHERE indexname='ux_transacoes_agendamento_competencia'` |
| M8 | **Dois "OK" seguidos** no bot podem lançar a mesma transação duas vezes (sem trava atômica da pendência; a pendência também some se a gravação falhar) | `TransactionReplyStep.cs:33-77` | bug (suspeita) |
| M9 | Relatório mensal: o JOIN dos potes **não filtra `usuario_id`** (sem impacto hoje, com um usuário só) | `MonthlyReportRepository.cs:113-126` | bug puro |
| M10 | Saldo total inclui lançamentos **futuros** (populador cria dez/ em 14/11), e o `MonthExpense` só soma até hoje | `DashboardRepository.cs:22`, `AccountRepository.cs:32` | decidir |
| M11 | `API_SECRET_KEY` serve ao mesmo tempo de chave do JWT, admin, webhook, OAuth e token no banco do bot: quem ler o banco do bot forja JWT | config | rotação (§9.2) |
| M12 | O bot não extrai **data, conta nem parcelamento** ("gastei 80 ontem" entra hoje; "TV em 10x" entra à vista); com data numa compra de cartão, dá exceção ("Ops! Algo deu errado") | `TransactionIntents.cs:83-87` | desvio 154 |
| M13 | Avisos de fatura saem às **00:01**, ignoram `alertas_financeiros_ativos` e se repetem no resumo da manhã e no check-in | `ScheduledJobs.cs:66-72` | desvio |

Front (médio):
- O front em `HEAD` **não compila**: `environment.bot.apiKey` só existe sem commit (§9.3).
- `environment.prod.ts` usa `http://212.47.65.37` sem HTTPS.
- `settings.service.ts` e `dashboard.service.ts` ignoram `apiUrl`.
- O interceptor imprime o token no console e manda o Bearer para qualquer host.

## BAIXO

**Front**
- `bill-modal.component.ts:827`: `notificar_antes_dias || 3` transforma 0 em 3.
- `settings.service.ts:200`: o label do endereço vai na URL sem `encodeURIComponent`.

**Backend (técnico)**
- Populador sem `FOR UPDATE` e sem competência: corrida entre o job das 00:01 e o `EnsureCurrent` pode duplicar a
  parcela.
- O lock dos jobs é liberado sem conferir o dono.
- As rotas admin de relatório rodam sem a trava.
- Aviso de "fatura fechada" que falhou no envio não é reenviado.
- Rate limit desligado (desvio 081): `/auth/login` não tem proteção contra tentativa de senha.

**Contas e cálculos**
- Relatório mensal:
  - o percentual é calculado sobre o top 5, não sobre o total;
  - "Pendentes" conta agendamentos já pagos.
- QUINZENAL vale 2,16 no resumo e 2 na reserva (o certo é 26/12 ≈ 2,1667).
- `MonthlyExpenses` sai sem arredondar (`300.30000000000004`).
- O resumo soma parcelado já quitado (`executadas == total` sem desativar).
- Atalho e SMS: "10,50" dá 400, e "1.234" vira 1,23 (paridade com o `float()`).
- Compra **no dia** do fechamento (13) cai na fatura que vence no dia 20: conferir com uma fatura real do Inter.

**Mensagens ao usuário**
- Dinheiro sem separador de milhar ("R$ 1234,56") em alertas, resumo e notificação de fatura.
- Textos sem acento em `AutomationTexts.cs` ("Transacao", "Descricao").
- Receita aparece como "(Débito)".
- Menu fala em reserva de "6x", mas produção usa 10.
- "não será mais cobrada automaticamente" ficou falso depois do desvio 272.
- Link `app.meusecretario.com/register` morto.

**Prompt do Gemini**
- `Quitar Conta Fixa` está no prompt e não tem ramo (desvio 160).

## Produto: sobra e falta

**Sobra (candidatos a cortar)**
- Campos "dias antes" no modal de contas e nas configurações: nenhum job os lê desde os desvios 268 e 272;
  `ScheduleRules.ReminderDueDate` só é usado em teste.
- Tela Assinatura (placeholder).
- Cadastro público (`/auth/register`).
- Módulo 008 (API keys e LGPD).
- Chaves Gemini por usuário: origem da armadilha descrita no `CLAUDE.md`.
- `BillsNotificationJob` ("vencem amanhã", contradiz o 272).
- `InvoiceNotificationFormatter.Overdue` e `AnalyticsReport.Money` sem uso.
- Rotas GET `/api/user/*` sem cliente.
- Alertas de tarefa (014): o job roda a cada minuto, mas nenhum canal liga o recurso.

**Falta**
- Criar ou editar conta (A5).
- "Marcar como paga" e status do mês na tela de Contas Mensais.
- Desfazer ou excluir o último lançamento pelo bot.
- Gerenciar potes e subcategorias (hoje só por SQL).
- Exportar CSV.

**Documentação desatualizada**
- `CLAUDE.md` §4/§5 e `RUNBOOK_BOT_NAO_RESPONDE.md` ainda descrevem o whatsapp-saas-bot; o canal hoje é o
  telegram-gateway.
- A tela "WhatsApp" do front administra um bot que o backend não usa mais.
- `parity-deviations/README.md` tem 52 itens ainda como "aprovado" que já foram implementados.

## Não coberto
- Testes de integração (sem Docker).
- SQL do dashboard a fundo.
- Google (Calendar e Drive).
- Analytics e previsões.
- Agenda do Coach (016).
- LGPD.
- Gráficos do ScottPlot.
- Dados reais de produção (ex.: se o índice da 0090 existe).

---

## Status das correções (branch `fix/auditoria-2026-11-16`, desvios 273 a 277)

| Item | Estado | Onde |
|---|---|---|
| A1 "paguei da fatura" | corrigido | desvio 276 |
| A2 populador na data do ciclo | corrigido (sem `data_inicio`) | desvio 277 |
| A3 2º pagamento quita a aberta | corrigido | desvio 274 |
| A4 "paguei X" sem agendamento/competência | corrigido | desvio 275 |
| A5 tela de contas do front | corrigido | front, branch `fix/auditoria-2026-11-16` |
| A6 extrato e edição de transação | corrigido | front |
| M1 relatório de fatura conta o pagamento | corrigido | desvio 273 |
| M3 apagar parcela | corrigido | desvio 276 |
| M4 centavos do parcelamento | corrigido | desvio 276 |
| M5 saldo inicial pelo bot | corrigido | desvio 273 |
| M7 migration 0090 | corrigido (0130; conferir o índice em produção) | desvio 273 |
| M8 "OK" duplicado | corrigido | desvio 273 |
| M9 pote sem filtro de usuário | corrigido | desvio 273 |
| Baixos do front (`\|\| 3`, label, interceptor) | corrigido | front |
| Documentação (§4/§5 do CLAUDE.md, runbook, índice de desvios) | atualizada | |
| M10 saldo inclui lançamentos futuros | corrigido: saldo só até hoje | desvio 278 |
| Cortes de produto (tela Assinatura, "dias antes", cadastro público por flag, flag dos alertas de tarefa) | feitos | desvio 278 + front |
| M2 estorno não abate a fatura | corrigido | desvio 279 |
| M6 três regras de "conta paga" | corrigido (dashboard mostra atrasadas dos últimos 7 dias) | desvio 280 |
| Saldo "projetado" como campo novo | corrigido (`saldo_projetado`; front e bot) | desvio 283 |
| M11 chave única (JWT/admin/webhook) | **código pronto** (`WEBHOOK_API_KEY` opcional); a troca real é manual do Rafael, ver desvio 284 | desvio 284 |
| M12 desvio 154 (data/conta/parcelas no "gastei") | corrigido (leitura determinística do texto) | desvio 281 |
| M13 avisos de fatura às 00:01 | corrigido (resumo da manhã, respeita `alertas_financeiros_ativos`) | desvio 282 |
| Baixos do backend: lock dos jobs, rotas admin sem trava, relatório mensal (percentual e pendentes), QUINZENAL 26/12 e arredondamento, parcelado quitado no resumo, aviso de fechamento não reenviado, textos sem acento, "(Débito)" em receita, "6x", link morto, dinheiro sem milhar, `Quitar Conta Fixa`, código morto, populador (`data_inicio`, competência, `FOR UPDATE`) | corrigido | desvios 282 e 285 |
| Rate limit desligado (`/auth/login`) | **não feito**: decisão do desvio 081, fora desta rodada | |
| Front: specs quebrados (modal de conta, título do scaffold) | corrigido; `ng test` 35 de 35 | front |

### Passos manuais que ficam com o Rafael
- Rotacionar de verdade a chave do webhook e a `API_SECRET_KEY` (desvio 284 e `CLAUDE.md` §9.2): criar o secret `WEBHOOK_API_KEY`, acrescentá-lo ao `.env` do `deploy.yml`, atualizar `sessions.metadata.webhook_auth_token` no banco do bot e só depois rotacionar a `API_SECRET_KEY`.
- Conferir em produção o índice da migration 0090 (`SELECT 1 FROM pg_indexes WHERE indexname='ux_transacoes_agendamento_competencia'`).
- Receitas em cartão lançadas antes do desvio 279 continuam sem fatura (sem backfill).
- Fazer o merge das branches e o deploy (nada foi enviado com `push`).
