# Regras de negócio

> **Estado: aprovado pelo Rafael em 2026-10-02**, incluindo todas as ESCOLHAS abaixo (cada uma com seu desvio aprovado).


Extraídas do código Flask. Onde o Flask tem versões divergentes da mesma regra, está marcado **ESCOLHA** com a definição proposta para o .NET. As escolhas precisam ser aprovadas pelo Rafael (cada uma tem um desvio em `parity-deviations/` quando altera comportamento).

## R1. Valor e saldo
- Despesa é gravada **negativa**, renda positiva. A API recebe valor positivo (≥ 0) e devolve com sinal em `POST/PUT /api/transactions`.
- Saldo da conta = `saldo_inicial + SUM(transacoes.valor)`. Não há coluna de saldo.
- **ESCOLHA (desvio 007, aprovada em 2026-10-02):** hoje `saldo_total` do dashboard soma só `consolidada = true` e `ativa = true`, enquanto `GET /api/accounts` soma todas as transações. Proposta: ambos consideram apenas consolidadas. `inclui_saldo_total` não é usado em nenhuma query; manter ignorado.

## R2. Tipos de transação
- Valores válidos no banco: `Renda`, `Despesa`, `Transferência`, `Pagamento Fatura`.
- **ESCOLHA (desvio 001, aprovada em 2026-10-02):** a API REST aceita/devolve `Receita` mas o banco/bot usam `Renda`. Proposta: a API continua aceitando e devolvendo `Receita` no contrato, e o domínio converte `Receita` ⇄ `Renda` na borda; filtros por `Receita` passam a achar também as do bot, e o resumo do dashboard soma `Renda`.

## R3. Transferência (`transaction_service.py:63`)
- Duas linhas tipo `Transferência`: −valor na origem e +valor no destino, ligadas por `transferencia_par_id` em ambos os sentidos.
- Descrições: "Transferência para X" e "Transferência de Y".
- Subcategoria fixa `Investimentos de Curto Prazo` (grupo `Meta Financeira`); erro se não existir.
- Excluir uma apaga o par (`api.py:1661-1677`).

## R4. Fatura de cartão (`invoice_service.py:13-77`)
Dado conta de cartão com `dia_fechamento` (F) e `dia_vencimento` (V), e data da compra D:
- Se `dia(D) ≤ F`: a fatura fecha **neste** mês; vence neste mês, ou no seguinte se `V < F`.
- Senão: fecha no mês seguinte e vence dois meses à frente (ou o seguinte, conforme `V < F`).
- Dia inexistente no mês cai no último dia (31 em fevereiro ⇒ 28/29).
- A fatura é localizada ou criada por (`conta_id`, `data_vencimento`) com `INSERT ... ON CONFLICT DO NOTHING RETURNING`.
- Valor da fatura = `SUM(ABS(valor)) WHERE valor < 0 AND fatura_id = f.id`; pagamentos positivos ignorados.
- Ciclo: `Aberta` → `Fechada` (job diário 00:01, `data_fechamento < hoje`) → `Paga` (no pagamento). Aviso 3 dias antes do vencimento para `Fechada`.
- `POST /api/transactions` com despesa em conta `Cartão de Crédito` vincula `fatura_id` via `get_or_create_fatura`; `origem='api'`.
- `ensure_current_invoice_exists` cria a fatura do período e, só nesse caminho, lança na fatura os agendamentos ativos do cartão (`process_invoice_schedules`), com idempotência por (`fatura_id`, `agendamento_id`).

## R5. Pagamento de fatura (`transaction_service.py:154`)
- Par de transações `Pagamento Fatura`, subcategoria `Quitação de Empréstimos (Principal)`.
- Marca a fatura `Paga` **sem comparar o valor pago**.
- **ESCOLHA (desvio 010, aprovada em 2026-10-02):** hoje a fatura paga é a do ciclo corrente (pela data do pagamento), não necessariamente a que vence. Proposta: pagar a fatura `Fechada` mais antiga em aberto; se não houver, a do ciclo corrente. (Aprovar com o Rafael.)

## R6. Parcelamento (`installment_service.py`)
- 1ª parcela vira transação imediata.
- Cria agendamento `PARCELADO`/`MENSAL` com `parcelas_executadas = 1`, `data_inicio` = +1 mês, `dia_execucao` = dia da compra.
- Cada execução grava descrição `"X (n/total)"`, incrementa e **desativa ao chegar em `total_parcelas`**.
- **ESCOLHA (desvio 011, aprovada em 2026-10-02):** hoje três lugares incrementam (`schedule_processor.py:142` compara com `==`; `invoice_service.py:741` e `nightly_checkin_service.py:741` com `>=`), e o processador de agendamentos não grava `agendamento_id`, abrindo risco de parcela duplicada. Proposta: uma só rotina de domínio `ExecuteInstallment`, com `>=`, sempre gravando `agendamento_id`, idempotente por (`agendamento_id`, mês).
- Validações: `total_parcelas ≥ 2` e só em `PARCELADO`; trocar para `PARCELADO` no PUT exige `total_parcelas` na mesma requisição.

## R7. Recorrência
- Só existem `dia_execucao` (mensal) e `mes_execucao` (ANUAL). DIARIA, SEMANAL e QUINZENAL **não geram ocorrências**; a periodicidade só afeta a reserva e a ordenação.
- Tipo da transação gerada: `Renda` se `nome_grupo = 'Renda'`, senão `Despesa`.
- "Já pago no mês": igualdade de `descricao` no mês (queries antigas) **ou** `agendamento_id` com janela de +20 dias (check-in). **ESCOLHA (desvio 012, aprovada em 2026-10-02):** usar `agendamento_id` em todas as consultas; `descricao` só como fallback para transações antigas sem vínculo.
- `LEMBRETE_VARIAVEL` só dispara se `notificar_antes_dias = 0` no `schedule_processor` (a query filtra `dia_execucao = hoje` e depois compara com `dia_execucao − notificar_antes_dias`). Tratar como bug; ver desvio 013.
- A condição `g.nome_grupo = 'Despesa'` em `agendamentos_queries.py` **nunca casa** (grupos são `Despesa Essencial` e `Despesa Discricionária`). Spec da feature 005 define o filtro correto.

## R8. Check-in noturno (ADR 5, `.claude/memory/architectural_decisions.md`)
- Regra híbrida de detecção de pagamento ("Bug da Escola", "Kotas").
- A transação recebe a data do vencimento real se o atraso for ≤ 1 dia; senão, a data de hoje (`nightly_checkin_service.py:663`).
- Sessão no Redis: `nightly_checkin:{whatsapp}:{id}` (3600s) + `nightly_checkin_active:{whatsapp}`.
- Janela de envio: horas 18–23, comparando com `checkin_noturno_hora` do usuário.

## R9. Reserva de emergência (`emergency_reserve_service.py`)
- Soma agendamentos **ativos** com `incluir_na_reserva` e tipo `FIXO` ou `LEMBRETE_VARIAVEL`.
- N = `usuarios.meses_reserva_emergencia` (default 6). Multiplicadores: MENSAL × N; ANUAL × 1 (integral); SEMANAL × round(N × 4,33); QUINZENAL × 2N; DIARIA × 30N.
- A fórmula está duplicada em SQL em `whatsapp_handler.py:774` e `api.py:2335` (esta última não filtra o tipo). **ESCOLHA (desvio 014, aprovada em 2026-10-02):** uma só implementação de domínio, com o filtro de tipo. `/api/bills/summary` sai sem arredondar (multiplica por 4,33, 2,16 e 30); a meta de reserva arredonda a 2 casas.

## R10. Potes (orçamento)
Três definições divergentes de período hoje:
- `budget_validation_service.py`: SEMANAL seg–dom; QUINZENAL 1–15 e 16–fim; MENSAL; ANUAL ano civil.
- `transaction_feedback_service.py:37`: `date_trunc('week')` para SEMANAL e QUINZENAL; mês para o resto.
- `pot_service.py:28`: sempre o mês corrente.

**ESCOLHA (desvio 015, aprovada em 2026-10-02):** adotar a de `budget_validation_service` (a mais completa e a única coberta por 17 testes). Estourar o limite **não bloqueia**; pede confirmação. `alerta_potes_threshold` aceita 0, 50, 70, 90. Potes são criados direto no banco; nenhuma rota os cria.

## R11. Contas
- Soft delete (`ativa=false`) só se não houver transações vinculadas; senão 400 `"Não é possível excluir esta conta. Existem N transações vinculadas."`.
- Nome único por usuário (constraint vale para inativas; a API checa só ativas ⇒ desvio 016: checar todas e devolver 400 em vez de 500 do banco).
- `nome_conta` é cortado em 100 caracteres.
- Cadastro cria 3 contas padrão (`/auth/register`: `Carteira` (tipo `Dinheiro`), `Conta Corrente`, `Cartão de Crédito` com os dias informados) — o serviço antigo `user_service.py:141` cria 4; vale a de `auth.py:173`.

## R12. Autenticação e cadastro
- Login: `whatsapp` reduzido a dígitos; senha com `.strip()`; hash Werkzeug (`pbkdf2:sha256` no cadastro; `scrypt` possível em senhas definidas por `definir_senha_manual.py`). As iterações vêm do próprio hash.
- Senha ≥ 6 caracteres; `dia_vencimento` e `dia_fechamento` em 1..31; WhatsApp único (409).
- `api_key_automate` = `secrets.token_urlsafe(32)` cifrada em Fernet; o fallback em texto puro quando a cifra falha (`auth.py:147-152`) é desvio 017 (falhar em vez de gravar em claro).
- Login distingue 404 (`"WhatsApp não cadastrado"`) de 401 (`"Senha incorreta"`): enumeração de usuários. Mantido na paridade; candidato a desvio 018.

## R13. Chaves de API por usuário
- Provedores: `gemini`, `weather`, `openroute`. `GET /api/user/api-keys` devolve sempre 3 itens.
- Chave própria só é usada se `usar_chave_propria` e houver chave ativa; senão a chave do sistema (`tipo_chave = 'sistema'`).
- O .NET passa a chave por chamada ao cliente (sem estado global).
- `weather` é validada contra OpenWeatherMap mas o serviço usa WeatherAPI.com (provedores diferentes; desvio 019).

## R14. LGPD
Consentimentos por tipo e versão; `exportar` devolve chaves decifradas; `deletar-conta` exige `{"confirmacao":"CONFIRMO_DELECAO"}` e apaga em cascata contando registros.

## R15. Confirmações pendentes (Redis, 300s)
Transações vindas de atalhos/WhatsApp ficam em `pending_tx:{whatsapp}:{id}` e `last_pending:{whatsapp}` até o usuário confirmar; eventos de agenda em `pending_event:*`.

## R16. Jobs (comportamento pretendido)
| Job | Agenda | Regra |
|---|---|---|
| Resumo matinal | de hora em hora | usuários com `resumo_matinal_hora` igual à hora atual (minuto 0) |
| Relatório mês anterior | dia 1, 08:00 | `INICIO_MES`, janela de 5 min em torno de `hora_envio` |
| Relatório mês corrente | diário 23:00 | `FIM_MES`; só no último dia do mês |
| Check-in noturno | 18–23h, hora cheia | exige Redis; ver R8 |
| Alertas de tarefas | a cada 1 min | eventos do Calendar em ±30s de agora + `minutos_antes`; dedupe `SET NX` 7200s |
| Processador de faturas | 00:01 | fecha, cria a próxima, popula agendamentos, avisa vencimento em 3 dias; dedupe `invoice_*` 7 dias |
| Motor de agendamentos | gatilho externo hoje | lança FIXO/PARCELADO e lembretes do dia; **sem idempotência** (desvio 013) |

Os jobs de relatório mensal hoje chamam `localhost:5000` (o servidor escuta 8000) e **nunca rodam**; o .NET os executa de fato (desvio 020).
