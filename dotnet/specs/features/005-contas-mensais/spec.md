# Spec 005 — Contas mensais / agendamentos (listar, criar, atualizar, remover, resumo) e reserva de emergência

- **Estado:** aprovada em 2026-10-04 (recomendações de foundation/decisoes-onda-2.md; onde a spec diverge, vale esse documento)
- **Onda:** 2
- **Origem (Python):** `app/routes/api.py:346-347` (constantes `TIPOS_AGENDAMENTO`, `PERIODICIDADES`), `api.py:1795-1858` (`_get_bills_impl`), `:1861-2037` (`_create_bill_impl`), `:2040-2203` (`_update_bill_impl`), `:2206-2228` (`_delete_bill_impl`), `:2231-2261` (rotas `/api/bills`), `:2264-2387` (`GET /api/bills/summary`), `:2390-2418` (aliases `/api/contas-mensais`); filtro `token_required` em `api.py:34-90`; `app/services/finance/emergency_reserve_service.py:13-93` (`get_reserva_status`) e os SQL duplicados de reserva em `routes/webhooks/handlers/whatsapp_handler.py:759-840` e `api.py:2326-2381`. `services/queries/agendamentos_queries.py` e `services/finance/bills_service.py` (vencimentos, check-in) **não são chamados por estas rotas**: são do motor e dos jobs (013).
- **Depende de:** 001a (`RequireUser`, `ICurrentUser`, `IDbConnectionFactory`, `Envelopes`), 002 (tabela `contas`; `RawBody`/`PyNumber`/`AccountsBodyReader`, só como código reutilizado), 003 (grupos e macros para `tipo_transacao`). **Não precisa de migration** (números `0060-0069` reservados, nenhum usado).
- **Desvios aplicáveis:** 008, 014, 024 (aprovados); 011 e 012 só como contexto (execução é da 013); P-005-1 a P-005-5 propostos (§9). Decisões em §10.
- **Consumidores:** 011 (`PATCH /api/agendamento/<id>/reserva`, `GET /api/agendamentos/reserva`) e 012 (consulta de reserva e detalhes) usam `IEmergencyReserveService` (§7); 013 usa os tipos de domínio de agendamento.

## 1. Objetivo

O usuário do site vê suas contas mensais (agendamentos ativos: contas fixas, parceladas e lembretes variáveis, de despesa ou de renda), cria, edita e remove (remoção lógica) e vê o resumo financeiro com a meta de reserva de emergência. Toda operação vale só para os agendamentos do usuário do token.

A feature também entrega a **implementação única do cálculo da reserva de emergência** (desvio 014, R9), que o Flask repetia em três lugares.

## 2. Rotas

| Método | Caminho | Alias | Auth | Envelope |
|---|---|---|---|---|
| GET | `/api/bills` | `/api/contas-mensais` | JWT (`RequireUser`) | A (com `data`) |
| POST | `/api/bills` | `/api/contas-mensais` | JWT | A |
| PUT | `/api/bills/<int:id>` | `/api/contas-mensais/<int:id>` | JWT | A |
| DELETE | `/api/bills/<int:id>` | `/api/contas-mensais/<int:id>` | JWT | A |
| GET | `/api/bills/summary` | **sem alias** | JWT | A |

São 9 operações. Alias e rota principal têm comportamento idêntico (`api.py:2391-2418`). Como em todo `/api/*`: sem barra final (404 do Flask), `<int:id>` não numérico ou negativo ⇒ **404 HTML do Flask** antes da autenticação, método não permitido ⇒ 405. Particularidades de roteamento:
- `/api/bills/summary` só existe para GET: `POST|PUT|DELETE /api/bills/summary` respondem **405** (a rota estática existe), e não 404. `/api/contas-mensais/summary` **não existe**: `GET` ⇒ 404 HTML (o `summary` não é `<int:id>`).
- O Flask registra `PUT` e `DELETE` numa só função por rota (`manage_bill`); para os outros métodos em `/{id}` vale 405 como nas demais features.

Envelope A de erro: `{"status":"error","message":"..."}`. Falha de autenticação: as 4 mensagens 401 do `RequireUser` (spec 001 §4.4), antes de qualquer validação da rota.

## 3. Contrato por rota

Convenções (iguais às da 002 §3): "número" é o que o Python converte com `float()`, "inteiro" o que `int()` aceita (`PyNumber.TryMoney`/`TryInt`, RN-002-09); chaves de resposta na ordem escrita; dinheiro como número JSON; o 503 `"Banco de dados não configurado"` não existe no .NET. Onde uma proposta P-005-N mudaria o comportamento, a linha traz a nota "(P-005-N)"; **sem aprovação o .NET faz o que está na coluna do Flask**.

Os textos de tipo e periodicidade: `TIPOS_AGENDAMENTO = FIXO, LEMBRETE_VARIAVEL, PARCELADO` (nesta ordem na mensagem) e `PERIODICIDADES = DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL`.

### 3.1 `GET /api/bills` e `GET /api/contas-mensais`

Origem: `api.py:1795-1858, 2232-2236, 2391-2395`. Sem corpo nem query. Sucesso: **200**
```json
{"status":"success","data":[{"id":7,"descricao":"Aluguel","valor_previsto":1500.0,"tipo_agendamento":"FIXO","tipo_transacao":"Despesa","grupo":"Despesa Essencial","periodicidade":"MENSAL","dia_execucao":5,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":12,"subcategoria_nome":"Aluguel","macro_categoria_nome":"Moradia e Contas","conta_id":3,"conta_nome":"Mercado pago","data_inicio":"2026-01-05","incluir_na_reserva":true,"total_parcelas":null,"parcelas_executadas":0}]}
```
- `data` = agendamentos do usuário com `ativo = true` (inativos nunca saem), ordenados por `periodicidade`, `dia_execucao`, `descricao` (ordenação do banco, sem `COLLATE`; ordem por bytes: `ANUAL`, `DIARIA`, `MENSAL`, `QUINZENAL`, `SEMANAL`), com `id` como desempate (sem mudança de contrato).
- Chaves, nesta ordem: `id, descricao, valor_previsto, tipo_agendamento, tipo_transacao, grupo, periodicidade, dia_execucao, mes_execucao, notificar_antes_dias, subcategoria_id, subcategoria_nome, macro_categoria_nome, conta_id, conta_nome, data_inicio, incluir_na_reserva, total_parcelas, parcelas_executadas`.
- `valor_previsto` número ou `null`; `tipo_transacao` = `"Receita"` se `grupo` é `Renda`, senão `"Despesa"` (inclusive `grupo` nulo) (com P-005-4: também a macro `Receitas Gerais`); `grupo`, `subcategoria_nome`, `macro_categoria_nome`, `conta_nome` vêm de `LEFT JOIN` (podem ser `null`); `data_inicio` ISO `YYYY-MM-DD`; `mes_execucao`, `total_parcelas` `null` quando não se aplicam; `incluir_na_reserva` booleano ou `null` (coluna aceita NULL); a conta e os agendamentos de conta **inativa** continuam saindo (o filtro é só `ativo`).
- Usuário sem agendamentos ativos: 200 `{"status":"success","data":[]}`.

Erro inesperado: **500** `"Erro ao carregar contas mensais"` (o Flask devolve `"Erro ao carregar contas mensais: <str(e)>"`; desvio 008, texto fixo).

### 3.2 `POST /api/bills` e `POST /api/contas-mensais`

Origem: `api.py:1861-2037, 2239-2243, 2398-2402`. Entrada (JSON): `descricao`, `tipo_agendamento`, `periodicidade`, `subcategoria_id`, `conta_id`, `dia_execucao` (obrigatórios); `valor_previsto` (obrigatório em `FIXO` e `PARCELADO`), `total_parcelas` (obrigatório em `PARCELADO`), `mes_execucao` (só em `ANUAL`), `notificar_antes_dias`, `data_inicio`, `incluir_na_reserva` (opcionais). Chaves desconhecidas (`usuario_id`, `ativo`, `parcelas_executadas`, `tipo_transacao`...) são ignoradas; o dono é sempre o `user_id` do token.

Validações, **nesta ordem** (a primeira que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido, não-objeto ou `{}` (desvio 024; o Flask testa `if not data`) | 400 `"Dados não fornecidos"` |
| 2 | `descricao` ausente ou vazia depois de `trim` (`null`/não texto dá 500, P-005-1) | 400 `"Descrição é obrigatória"` |
| 3 | `tipo_agendamento` (com `trim`) ausente, vazio ou fora de `FIXO, LEMBRETE_VARIAVEL, PARCELADO` (exato, maiúsculas) (`null`/não texto dá 500, P-005-1) | 400 `"Tipo de agendamento inválido. Valores permitidos: FIXO, LEMBRETE_VARIAVEL, PARCELADO"` |
| 4 | `periodicidade` (com `trim`) ausente, vazia ou fora de `DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL` (idem) | 400 `"Periodicidade inválida. Valores permitidos: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL"` |
| 5 | `subcategoria_id` ausente ou falso (`null`, `0`, `""`, `false`) | 400 `"Subcategoria é obrigatória"` |
| 6 | `conta_id` ausente ou falso | 400 `"Conta bancária é obrigatória"` |
| 7 | `dia_execucao` não conversível por `int()` (ausente, `null`, `"abc"`, lista) ou fora de 1..31 | 400 `"Dia de execução deve ser entre 1 e 31"` |
| 8 | tipo `FIXO` ou `PARCELADO` e `valor_previsto` ausente ou `null` | 400 `"Valor previsto é obrigatório para contas fixas ou parceladas"` |
| 9 | tipo `FIXO` ou `PARCELADO` e `valor_previsto` não conversível por `float()` ou `≤ 0` | 400 `"Valor previsto deve ser um número positivo"` |
| 10 | tipo `PARCELADO` e `total_parcelas` não conversível por `int()` (ausente, `null`, texto) ou `< 2` | 400 `"Número de parcelas deve ser um inteiro maior ou igual a 2"` |
| 11 | periodicidade `ANUAL` e `mes_execucao` presente e não nulo: não conversível por `int()` ou fora de 1..12 | 400 `"Mês de execução deve ser entre 1 e 12"` |
| 12 | `conta_id` sem conta **ativa do usuário** | 404 `"Conta bancária não encontrada"` |

Tratamento dos campos:
- `descricao`: `trim`, depois cortada em **255 pontos de código** (`descricao[:255]`, sem erro).
- `valor_previsto`: `FIXO`/`PARCELADO` ⇒ `float()` positivo (linhas 8 e 9). `LEMBRETE_VARIAVEL`: ausente/`null` ⇒ NULL; presente mas não conversível ⇒ **NULL sem erro**; conversível ⇒ gravado como veio (zero e negativo aceitos). `NUMERIC(15,2)`.
- `total_parcelas`: só `PARCELADO`; em qualquer outro tipo é **ignorado** (grava NULL).
- `mes_execucao`: só vale se `periodicidade = ANUAL` e `mes_execucao` não nulo; em todos os outros casos grava NULL. **`ANUAL` sem mês grava NULL e viola `chk_anual_mes`** ⇒ 500 `"Erro ao criar conta mensal"` (P-005-3).
- `notificar_antes_dias`: ausente ⇒ 3; `int()` que falha (texto, `null`, lista) ⇒ 3; aceita zero e negativos.
- `data_inicio`: ausente, `null` ou falso (`""`) ⇒ **hoje** em São Paulo (`IClock`); senão o valor é passado ao banco como veio (texto fora de data ⇒ 500, P-005-1).
- `incluir_na_reserva`: ausente ⇒ `false` (**não** o default `true` do banco); presente é gravado como veio (`null` grava NULL; texto dá 500, P-005-1).
- Gravados também: `ativo = true`, `parcelas_executadas = 0`.
- `subcategoria_id` **não é validado** (inexistente ⇒ violação de FK ⇒ 500; de outro usuário ⇒ aceita; P-005-2).

Sucesso: **201** (sem `message`)
```json
{"status":"success","data":{"id":41,"descricao":"Internet","valor_previsto":120.0,"tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":10,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":12,"subcategoria_nome":"Internet","conta_id":3,"conta_nome":"Mercado pago","data_inicio":"2026-10-04","incluir_na_reserva":false,"total_parcelas":null,"parcelas_executadas":0}}
```
`data` tem 16 chaves, nesta ordem: `id, descricao, valor_previsto, tipo_agendamento, periodicidade, dia_execucao, mes_execucao, notificar_antes_dias, subcategoria_id, subcategoria_nome, conta_id, conta_nome, data_inicio, incluir_na_reserva, total_parcelas, parcelas_executadas`. **Não** traz `tipo_transacao`, `grupo` nem `macro_categoria_nome`. Lida do banco depois da gravação.

Erro inesperado (inclusive violação de `CHECK`, FK ou tipo): **500** `"Erro ao criar conta mensal"`.

### 3.3 `PUT /api/bills/<id>` e `PUT /api/contas-mensais/<id>`

Origem: `api.py:2040-2203, 2246-2261, 2405-2418`. Atualização **parcial**: só as chaves presentes no corpo entram.

Validações e leitura de campos, **nesta ordem**:

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido, não-objeto ou `{}`. **Vem antes do 404** | 400 `"Dados não fornecidos"` |
| 2 | não existe agendamento com esse `id` **ativo e do usuário** (inexistente, inativo e de outro usuário dão a mesma resposta) | 404 `"Conta mensal não encontrada"` |
| 3 | `descricao` presente: `str(valor).strip()[:255]` vazio (**`null` vira o texto `"None"`** e é gravado; número vira texto; P-005-1) | 400 `"Descrição não pode ser vazia"` |
| 4 | `tipo_agendamento` presente e fora da lista (**sem `trim`**) | 400 `"Tipo inválido. Valores: FIXO, LEMBRETE_VARIAVEL, PARCELADO"` |
| 5 | `periodicidade` presente e fora da lista (sem `trim`) | 400 `"Periodicidade inválida. Valores: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL"` |
| 6 | `dia_execucao` presente e não conversível por `int()` ou fora de 1..31 (inclusive `null`) | 400 `"Dia de execução deve ser entre 1 e 31"` |
| 7 | `mes_execucao` presente: `null` ⇒ grava NULL; senão não conversível por `int()` ou fora de 1..12 | 400 `"Mês deve ser entre 1 e 12"` |
| 8 | `valor_previsto` presente: `null` ⇒ grava NULL; senão não conversível por `float()` (**sem validação de sinal**: zero e negativo aceitos) | 400 `"Valor previsto inválido"` |
| 9 | `subcategoria_id` presente: **nenhuma validação** (`null` ou id inexistente ⇒ 500; id de outro usuário ⇒ aceito; P-005-2) | — |
| 10 | `conta_id` presente e sem conta **ativa do usuário** (inclusive `null`) | 404 `"Conta bancária não encontrada"` |
| 11 | `notificar_antes_dias` presente e não conversível por `int()` (`"abc"`, `null`, lista): o Flask **acrescenta o campo ao `UPDATE` antes de converter**, então o parâmetro falta e o comando falha ⇒ **500** `"Erro ao atualizar conta mensal"` (a intenção do código é ignorar o campo; P-005-1) | — |
| 12 | `data_inicio` presente: passado ao banco como veio (texto fora de data ou `null` ⇒ 500; P-005-1) | — |
| 13 | `incluir_na_reserva` presente: `bool(valor)` do Python (`"false"` vira `true`, `null` vira `false`) | — |
| 14 | `total_parcelas` presente: `null` ⇒ grava NULL; senão `int()` falha ou `< 2` | 400 `"Número de parcelas deve ser um inteiro maior ou igual a 2"` |
| 15 | o corpo traz `tipo_agendamento` igual a `PARCELADO` e **não** traz a chave `total_parcelas` (o tipo já gravado não conta) | 400 `"Número de parcelas é obrigatório para contas parceladas"` |
| 16 | nenhuma das chaves reconhecidas entrou (`{"foo":1}`) | 400 `"Nenhum campo para atualizar"` |

Chaves reconhecidas: `descricao, tipo_agendamento, periodicidade, dia_execucao, mes_execucao, valor_previsto, subcategoria_id, conta_id, notificar_antes_dias, data_inicio, incluir_na_reserva, total_parcelas`. `parcelas_executadas`, `ativo` e `usuario_id` são ignorados.

Efeito: um `UPDATE agendamentos SET <campos presentes> WHERE id AND usuario_id` (o trigger atualiza `updated_at`), depois a leitura do registro. **O PUT não confere o estado resultante contra os `CHECK`**: trocar `ANUAL` por `MENSAL` sem `mes_execucao: null`, trocar para `ANUAL` sem mês, gravar `total_parcelas` em tipo que não é `PARCELADO`, sair de `PARCELADO` com parcelas já executadas e gravar `mes_execucao` em periodicidade não anual violam `chk_anual_mes`/`chk_parcelado` ⇒ **500** `"Erro ao atualizar conta mensal"` (P-005-3). O Angular produz esses casos (§11).

Sucesso: **200** (sem `message`): `{"status":"success","data":{...}}` com os mesmos 16 campos e a mesma ordem do POST (§3.2).
Erro inesperado: **500** `"Erro ao atualizar conta mensal"`.

### 3.4 `DELETE /api/bills/<id>` e `DELETE /api/contas-mensais/<id>`

Origem: `api.py:2206-2228`. Sem corpo.

| # | Condição | Resposta |
|---|---|---|
| 1 | não existe agendamento com esse `id` **ativo e do usuário** | 404 `"Conta mensal não encontrada"` |

Efeito: `UPDATE agendamentos SET ativo = false WHERE id AND usuario_id AND ativo = true`; uma única instrução (sem leitura prévia). A linha não é apagada; transações já lançadas por ela continuam com `agendamento_id`. Não há reativação por rota.

Sucesso: **200** `{"status":"success","message":"Conta mensal removida com sucesso"}` (sem `data`).
Erro inesperado: **500** `"Erro ao remover conta mensal"`.

### 3.5 `GET /api/bills/summary`

Origem: `api.py:2264-2387`. Sem corpo nem query. Sucesso: **200**
```json
{"status":"success","data":{"total_despesas_mensais":2143.3,"total_despesas_anuais":1200.0,"total_receitas_mensais":3000.0,"reserva_emergencia":{"meta":14400.0,"gasto_mensal_equivalente":2400.0,"meses_configurados":6}}}
```
- **Totais** (sem arredondar; números JSON como saem do `double`): linhas de `agendamentos` do usuário com `ativo = true`, `valor_previsto IS NOT NULL` e `> 0`, agrupadas por periodicidade e grupo. `tipo` = `Receita` se o grupo é `Renda` (com P-005-4, ou macro `Receitas Gerais`), senão `Despesa`. Para `Despesa`: `MENSAL` soma o valor; `SEMANAL` × **4,33**; `QUINZENAL` × **2,16**; `DIARIA` × **30** (em `total_despesas_mensais`); `ANUAL` soma em `total_despesas_anuais`. Para `Receita`: iguais, mas `ANUAL` **não entra em nenhum total**. Todos os tipos de agendamento (`FIXO`, `LEMBRETE_VARIAVEL`, `PARCELADO`) entram.
- **Reserva** (R9, desvio 014): `meses` = `COALESCE(usuarios.meses_reserva_emergencia, 6)` (usuário inexistente ⇒ 6); total da reserva pelo `IEmergencyReserveService` (§7, **com filtro de tipo** `FIXO`/`LEMBRETE_VARIAVEL`); se o total é `0` e `total_despesas_mensais > 0`, usa `total_despesas_mensais × meses` (regra **só do resumo**); `meta = round(reserva_total, 2)`, `gasto_mensal_equivalente = round(reserva_total / meses, 2)` (`0` se `meses ≤ 0`), `meses_configurados = meses` (inteiro). O `round` é o do Python (meio-par sobre o valor binário exato, `PyRound`).
- Usuário sem agendamentos: todos os totais `0.0`, `meta 0.0`, `gasto_mensal_equivalente 0.0`, `meses_configurados 6` (ou o configurado).

Erro inesperado: **500** `"Erro ao calcular resumo"`.

## 4. Regras de negócio

- **RN-005-01** As 9 operações exigem JWT válido; `user_id` vem só do token (`ICurrentUser`), nunca de corpo, query ou URL. *Origem: `api.py:34-90, 2232-2264, 2391-2418`.*
- **RN-005-02** Tenant: toda leitura e escrita filtra `a.usuario_id = <token>`; agendamento de outro usuário é indistinguível de inexistente (404 `"Conta mensal não encontrada"`); a conta citada precisa ser do usuário e ativa. *Origem: `api.py:1827, 1962, 2052, 2121, 2216, 2291, 2340`.*
- **RN-005-03** Só agendamentos com `ativo = true` aparecem no GET, no resumo e podem ser alterados ou removidos; o DELETE é lógico (`ativo = false`). A coluna é **`ativo`** (em `contas` é `ativa`). *Origem: `api.py:1827, 2052, 2214-2216`.*
- **RN-005-04** Tipos (`FIXO`, `LEMBRETE_VARIAVEL`, `PARCELADO`) e periodicidades (`DIARIA`, `SEMANAL`, `QUINZENAL`, `MENSAL`, `ANUAL`) são exatos e sensíveis a maiúsculas; o banco tem os mesmos `CHECK`. *Origem: `api.py:346-347`; `0000_baseline.sql:30-31`.*
- **RN-005-05** (R7) Só existem `dia_execucao` (1..31, dia inexistente cai no último dia do mês pelo motor) e `mes_execucao` (só `ANUAL`). `DIARIA`, `SEMANAL` e `QUINZENAL` **não geram ocorrências**: a periodicidade só afeta o resumo e a reserva. A transação gerada por um agendamento é `Renda` se o grupo da subcategoria for `Renda` (013). Esta feature não gera transações.
- **RN-005-06** `valor_previsto` é obrigatório e positivo em `FIXO` e `PARCELADO` (no POST) e opcional em `LEMBRETE_VARIAVEL`; `NUMERIC(15,2)`. *Origem: `api.py:1908-1923`.*
- **RN-005-07** `PARCELADO` exige `total_parcelas ≥ 2`; fora de `PARCELADO` o POST ignora `total_parcelas` e o banco exige `total_parcelas IS NULL AND parcelas_executadas = 0` (`chk_parcelado`). No POST `parcelas_executadas` começa em **0** (a compra parcelada feita pelo bot começa em 1; ver Q-005-2). Trocar para `PARCELADO` no PUT exige `total_parcelas` na mesma requisição. *Origem: `api.py:1925-1934, 2142-2157`; `business-rules.md` R6.*
- **RN-005-08** `mes_execucao` só se aplica a `ANUAL` (`chk_anual_mes`: `ANUAL` exige mês, os demais exigem NULL). *Origem: `api.py:1936-1946`; `0000_baseline.sql:35`.*
- **RN-005-09** `tipo_transacao` do GET é derivado do grupo da categoria (`Renda` ⇒ `Receita`, o resto ⇒ `Despesa`); não é coluna. *Origem: `api.py:1836`.*
- **RN-005-10** `incluir_na_reserva` ausente no POST grava `false`; a reserva só soma `incluir_na_reserva = TRUE`. *Origem: `api.py:1958`; `emergency_reserve_service.py:62`.*
- **RN-005-11** (R9) Reserva de emergência: soma por periodicidade de `valor_previsto` dos agendamentos **ativos**, `incluir_na_reserva = TRUE` e tipo `FIXO` ou `LEMBRETE_VARIAVEL` (desvio 014), com `N` = meses do usuário: `MENSAL × N`; `ANUAL × 1` (integral); `SEMANAL × round(N × 4,33)` (`round` do Python, meio-par); `QUINZENAL × 2N`; `DIARIA × 30N`. Uma só implementação (`EmergencyReserve` no Domain, `IEmergencyReserveService` na Application). *Origem: `emergency_reserve_service.py:13-93`; `api.py:2326-2361`; `whatsapp_handler.py:774-790`; desvio 014.*
- **RN-005-12** Fatores de equivalência mensal do **resumo** (distintos dos da reserva): `SEMANAL 4,33`, `QUINZENAL 2,16`, `DIARIA 30`, `ANUAL` fora (separado). `QUINZENAL` usa 2,16 no resumo e `2N` na reserva: mantido (desvio 014 descreve assim). *Origem: `api.py:2308-2324`.*
- **RN-005-13** Arredondamento a 2 casas **só** em `meta` e `gasto_mensal_equivalente` do resumo; os três totais saem sem arredondar. A ordem de soma dos `double` do resumo é fixada pela consulta (`ORDER BY periodicidade, nome_grupo`), de modo que o resultado é determinístico (o Flask somava na ordem que o banco devolvia). *Origem: `api.py:2299-2381`.*
- **RN-005-14** 500 devolve sempre a mensagem fixa da rota (inclusive o GET, que hoje vaza `str(e)`); a exceção vai ao log só como tipo (e `SqlState`), sem descrição, valor nem corpo (desvio 008). EventIds 5001–5005.
- **RN-005-15** Corpo lido sem exigir `Content-Type` (desvio 024); ausente, vazio, inválido, não-objeto ou `{}` ⇒ `"Dados não fornecidos"` (POST e PUT).
- **RN-005-16** `id` da URL: só dígitos casam a rota; número que não cabe em `int` é tratado como agendamento inexistente (404 JSON `"Conta mensal não encontrada"`), como na 002 (RN-002-18).
- **RN-005-17** `data_inicio` ausente usa a data de hoje do `IClock` (America/Sao_Paulo), não do relógio do container. *Origem: `api.py:1954-1956`; constituição §7.*
- **RN-005-18** O resumo e o GET refletem **todas** as contas do usuário (inclusive inativas); contas inativas não impedem o agendamento de aparecer. A feature 002 já bloqueia remover conta com agendamentos ativos (desvio 037).
- **RN-005-19** Atomicidade: POST e PUT leem o registro final na mesma conexão da gravação; DELETE é uma instrução.

## 5. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","005-NN")]`); os de integração comparam o corpo como texto exato.

**Autenticação, roteamento e erros (comuns)**
- **AC-005-80** Dado nenhum `Authorization`, `Token abc`, token expirado/adulterado ou sem `user_id`, quando qualquer das 9 operações, então as 4 mensagens 401 do `RequireUser`.
- **AC-005-81** Dado cada operação, quando chamada por `/api/bills` e por `/api/contas-mensais`, então status e corpo idênticos (o `summary` só em `/api/bills`).
- **AC-005-82** Dado `/api/bills/` (barra final), `/api/bills/abc`, `/api/bills/-1`, `/api/contas-mensais/1.5`, `GET /api/contas-mensais/summary`, então 404 com o HTML do Flask, **mesmo sem token**.
- **AC-005-83** Dado `GET /api/bills/1`, `POST /api/bills/1`, `DELETE /api/bills`, `PUT /api/bills`, `POST|PUT|DELETE /api/bills/summary`, então **405**.
- **AC-005-84** Dado que o repositório lança exceção (inclusive `PostgresException` com texto interno), então 500 com a mensagem fixa da rota (`"Erro ao carregar contas mensais"`, `"Erro ao criar conta mensal"`, `"Erro ao atualizar conta mensal"`, `"Erro ao remover conta mensal"`, `"Erro ao calcular resumo"`), sem trecho da exceção; o log leva só o tipo (desvio 008).
- **AC-005-85** Dado `DELETE /api/bills/99999999999999999999`, então 404 JSON `"Conta mensal não encontrada"` (RN-005-16).
- **AC-005-86** Dados dois usuários A e B, então nenhuma das 9 operações lê ou altera agendamento de outro (AC 04, 31, 59, 76, 95).

**GET**
- **AC-005-01** Dado usuário com agendamentos ativos, então 200 com `data` de 19 chaves na ordem da §3.1; `tipo_transacao` `"Receita"` para grupo `Renda` e `"Despesa"` para o resto; `data_inicio` ISO.
- **AC-005-02** Dados agendamentos com periodicidades, dias e descrições variadas, então a ordem é `periodicidade`, `dia_execucao`, `descricao`, `id` (nomes ASCII); `ANUAL` antes de `DIARIA` antes de `MENSAL`.
- **AC-005-03** Dados agendamentos `ativo = false`, então não aparecem; dados de outro usuário, idem (AC-005-04).
- **AC-005-05** Dado usuário sem agendamentos, então 200 `{"status":"success","data":[]}`.
- **AC-005-06** Dado `valor_previsto` NULL, `mes_execucao` NULL, `total_parcelas` NULL, então saem `null`; `valor_previsto` `1500.5` sai número com centavos (`1500.5`), não texto; `incluir_na_reserva` NULL sai `null`.
- **AC-005-07** Dado agendamento de conta inativa, então continua saindo; dada subcategoria cujo grupo é `Renda` ⇒ `grupo: "Renda"`, `tipo_transacao: "Receita"`.
- **AC-005-08** Dado o GET, então o 500 do repositório devolve texto fixo (nunca `": <exceção>"`).

**POST**
- **AC-005-10** Dado `{"descricao":"Internet","tipo_agendamento":"FIXO","periodicidade":"MENSAL","subcategoria_id":<global>,"conta_id":<ativa>,"dia_execucao":10,"valor_previsto":120}`, então 201 com o corpo exato da §3.2 (16 chaves, `incluir_na_reserva: false`, `notificar_antes_dias: 3`, `parcelas_executadas: 0`, `data_inicio` = hoje do `IClock`, `subcategoria_nome` e `conta_nome` preenchidos); no banco `ativo = true`, `usuario_id` do token.
- **AC-005-11** Dado corpo ausente, sem `Content-Type`, JSON inválido, `{}`, `null`, `[]` ou `"abc"`, então 400 `"Dados não fornecidos"`, nunca 500.
- **AC-005-12** Dada a ordem da §3.2 (linhas 2 a 11), com dois erros no mesmo corpo responde o primeiro: sem `descricao` e com `tipo_agendamento` inválido ⇒ `descricao`; `tipo_agendamento` e `periodicidade` inválidos ⇒ tipo; `subcategoria_id` ausente e `conta_id` ausente ⇒ subcategoria; `dia_execucao` inválido e `valor_previsto` ausente (FIXO) ⇒ dia; a conta inexistente só aparece depois de todas as validações (404 depois dos 400).
- **AC-005-13** Dado `descricao` ausente, `""`, `"   "` ⇒ 400 `"Descrição é obrigatória"`; `"  Luz  "` ⇒ gravado `Luz`; descrição com 300 caracteres (inclusive acentos e emoji) ⇒ gravados os primeiros 255 pontos de código, sem erro.
- **AC-005-14** Dado `tipo_agendamento` `"fixo"`, `""`, ausente ou `"MENSAL"`, então 400 `"Tipo de agendamento inválido. Valores permitidos: FIXO, LEMBRETE_VARIAVEL, PARCELADO"`; `" FIXO "` ⇒ aceito (o POST faz `trim`) e gravado `FIXO`.
- **AC-005-15** Dado `periodicidade` `"mensal"`, `""`, ausente ou `"BIMESTRAL"` ⇒ 400 `"Periodicidade inválida. Valores permitidos: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL"`; `" MENSAL "` aceito.
- **AC-005-16** Dado `subcategoria_id` ausente, `null`, `0`, `""`, `false` ⇒ 400 `"Subcategoria é obrigatória"`; `conta_id` idem ⇒ `"Conta bancária é obrigatória"`.
- **AC-005-17** Dado `dia_execucao` ausente, `null`, `"abc"`, `0`, `32`, `-1`, `[]` ⇒ 400 `"Dia de execução deve ser entre 1 e 31"`; `"15"`, `15.9` (⇒ 15), `true` (⇒ 1), `31` aceitos.
- **AC-005-18** Dado `FIXO` ou `PARCELADO` com `valor_previsto` ausente ou `null` ⇒ 400 `"Valor previsto é obrigatório para contas fixas ou parceladas"`; `0`, `-5`, `"abc"`, `[]` ⇒ 400 `"Valor previsto deve ser um número positivo"`; `"120.5"`, `true` aceitos; `LEMBRETE_VARIAVEL` sem valor ⇒ 201 com `valor_previsto: null`; `LEMBRETE_VARIAVEL` com `"abc"` ⇒ 201 com `null`; com `0` ou `-5` ⇒ 201 gravado.
- **AC-005-19** Dado `PARCELADO` com `total_parcelas` ausente, `null`, `1`, `0`, `"x"` ⇒ 400 `"Número de parcelas deve ser um inteiro maior ou igual a 2"`; `2`, `"12"` aceitos e `parcelas_executadas = 0`; `FIXO` com `total_parcelas: 5` ⇒ 201 com `total_parcelas: null` (ignorado).
- **AC-005-20** Dado `ANUAL` com `mes_execucao` `13`, `0`, `"x"` ⇒ 400 `"Mês de execução deve ser entre 1 e 12"`; `ANUAL` com `mes_execucao: 3` ⇒ 201 com `mes_execucao: 3`; `MENSAL` com `mes_execucao: 5` ⇒ 201 com `mes_execucao: null` (ignorado); `ANUAL` sem mês ou com `null` ⇒ 500 `"Erro ao criar conta mensal"` (violação de `chk_anual_mes`; muda com P-005-3).
- **AC-005-21** Dado `notificar_antes_dias` ausente, `"abc"`, `null` ⇒ gravado 3; `0` e `-2` e `"7"` ⇒ gravados.
- **AC-005-22** Dado `data_inicio` ausente, `null` ou `""` ⇒ hoje do `IClock` (com relógio falso em 2026-10-04 23:30 de São Paulo, `2026-10-04`, mesmo que em UTC já seja dia 5); `"2026-02-01"` ⇒ gravado; texto fora de data ⇒ 500 (muda com P-005-1).
- **AC-005-23** Dado `incluir_na_reserva` ausente ⇒ `false`; `true` ⇒ `true`; `null` ⇒ `null`.
- **AC-005-24** Dado `conta_id` de conta de outro usuário, inexistente ou **inativa**, então 404 `"Conta bancária não encontrada"` e nada gravado.
- **AC-005-25** Dado corpo com `usuario_id` de outro usuário, `ativo: false`, `parcelas_executadas: 9` ou `id: 1`, então ignorados (agendamento do token, ativo, `parcelas_executadas = 0`).
- **AC-005-26** Dado `subcategoria_id` inexistente, então 500 `"Erro ao criar conta mensal"` (FK; muda com P-005-2); dado `subcategoria_id` própria de outro usuário, 201 (muda com P-005-2).
- **AC-005-27** Dado o corpo que o Angular envia na criação (`descricao`, `tipo_agendamento`, `periodicidade`, `dia_execucao`, `data_inicio`, `subcategoria_id`, `conta_id`, `notificar_antes_dias`, `incluir_na_reserva`, `total_parcelas: null`, `valor_previsto` só quando preenchido, `mes_execucao` só se `ANUAL`), então 201.
- **AC-005-28** Dado o POST bem-sucedido, então o agendamento aparece no `GET` com `tipo_transacao` e `grupo` e no `summary` (AC-005-90).

**PUT**
- **AC-005-40** Dado corpo ausente, vazio, inválido, não-objeto ou `{}`, então 400 `"Dados não fornecidos"`, **inclusive quando o agendamento não existe** (400 antes do 404).
- **AC-005-41** Dado `id` inexistente, **inativo** ou de **outro usuário**, então 404 `"Conta mensal não encontrada"` com a mesma resposta nos três casos; o agendamento do outro permanece intacto.
- **AC-005-42** Dado `{"descricao":"Nova"}`, então 200 com o corpo da §3.3 (16 chaves), só a descrição muda e `updated_at` avança; `{"descricao":"   "}` e `""` ⇒ 400 `"Descrição não pode ser vazia"`; descrição com 300 caracteres ⇒ cortada em 255.
- **AC-005-43** Dado `tipo_agendamento` `"X"`, `"fixo"`, `" FIXO "` (sem `trim`: inválido) ⇒ 400 `"Tipo inválido. Valores: FIXO, LEMBRETE_VARIAVEL, PARCELADO"`; `periodicidade` inválida ⇒ 400 `"Periodicidade inválida. Valores: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL"`.
- **AC-005-44** Dado `dia_execucao` `0`, `32`, `"x"`, `null` ⇒ 400 `"Dia de execução deve ser entre 1 e 31"`; `"20"` ⇒ 20.
- **AC-005-45** Dado `mes_execucao` `null` ⇒ NULL; `13`, `"x"` ⇒ 400 `"Mês deve ser entre 1 e 12"`; `6` ⇒ 6 (só passa o `CHECK` se a periodicidade resultante for `ANUAL`).
- **AC-005-46** Dado `valor_previsto` `null` ⇒ NULL; `"abc"`, `[]` ⇒ 400 `"Valor previsto inválido"`; `0`, `-10`, `250.75` ⇒ gravados (sem validação de sinal).
- **AC-005-47** Dado `conta_id` de conta própria ativa ⇒ atualiza; de outro usuário, inexistente, inativa ou `null` ⇒ 404 `"Conta bancária não encontrada"`.
- **AC-005-48** Dado `subcategoria_id` existente ⇒ atualiza e a resposta traz o novo `subcategoria_nome`; inexistente ou `null` ⇒ 500 (muda com P-005-2).
- **AC-005-49** Dado `notificar_antes_dias` `"7"`, `0` ⇒ gravados; `"abc"`, `null` ⇒ **500** `"Erro ao atualizar conta mensal"` e nenhum campo do corpo gravado (muda com P-005-1).
- **AC-005-50** Dado `incluir_na_reserva` `true`/`false` ⇒ gravados; `"false"` ⇒ `true`; `null` ⇒ `false`.
- **AC-005-51** Dado `data_inicio` `"2026-03-01"` ⇒ gravado; texto fora de data ou `null` ⇒ 500 (muda com P-005-1).
- **AC-005-52** Dado `total_parcelas` `null` ⇒ NULL; `1`, `"x"` ⇒ 400 `"Número de parcelas deve ser um inteiro maior ou igual a 2"`; `3` ⇒ 3 (só passa o `CHECK` em agendamento `PARCELADO`).
- **AC-005-53** Dado `{"tipo_agendamento":"PARCELADO"}` sem `total_parcelas` ⇒ 400 `"Número de parcelas é obrigatório para contas parceladas"`; `{"tipo_agendamento":"PARCELADO","total_parcelas":6}` ⇒ 200; num agendamento já `PARCELADO`, `{"descricao":"x"}` ⇒ 200 (o tipo gravado não conta).
- **AC-005-54** Dado `{"foo":1}` ⇒ 400 `"Nenhum campo para atualizar"`; `{"parcelas_executadas":3,"ativo":false}` ⇒ idem (ignorados).
- **AC-005-55** Dado corpo com dois erros (`dia_execucao` inválido e `conta_id` de outro usuário), então responde o primeiro da ordem da §3.3 (`dia_execucao`) e nada é gravado.
- **AC-005-56** Dado o corpo que o Angular envia na edição (todas as chaves do formulário, `total_parcelas: null` se não for `PARCELADO`, `mes_execucao` só se `ANUAL` e preenchido): num `FIXO` mensal, 200; num `ANUAL` editado para `MENSAL`, 500 `"Erro ao atualizar conta mensal"` (mês antigo continua gravado; muda com P-005-3); num `PARCELADO` com parcelas executadas editado para `FIXO`, 500 (`chk_parcelado`; muda com P-005-3). Documenta por que P-005-3 é recomendado.
- **AC-005-57** Dado `tipo_agendamento` inválido e `descricao` vazia no mesmo corpo do PUT, então responde `descricao` (linha 3 antes da 4).
- **AC-005-58** Dado `ANUAL` sem mês no PUT que troca `periodicidade` para `ANUAL`, então 500 (muda com P-005-3).
- **AC-005-59** Dado token do usuário B e `id` de agendamento de A, então 404 e o agendamento de A não muda; `conta_id` de A no PUT de B ⇒ 404.

**DELETE**
- **AC-005-70** Dado agendamento ativo, então 200 `{"status":"success","message":"Conta mensal removida com sucesso"}`, `ativo = false` no banco (linha continua existindo) e some do GET e do resumo.
- **AC-005-71** Dado `id` inexistente, já inativo (segundo DELETE) ou de outro usuário, então 404 `"Conta mensal não encontrada"`; o do outro permanece ativo.
- **AC-005-72** Dado agendamento com transações vinculadas (`agendamento_id`), então o DELETE é aceito e as transações continuam com o vínculo.
- **AC-005-76** Dado token do usuário B e `id` de A, então 404 e o agendamento de A continua ativo.

**Resumo e reserva**
- **AC-005-90** Dados `MENSAL` despesa 100, `SEMANAL` despesa 10, `QUINZENAL` despesa 50, `DIARIA` despesa 2, `ANUAL` despesa 1200 e `MENSAL` receita 3000, então `total_despesas_mensais = 100 + 10×4,33 + 50×2,16 + 2×30 = 311,3` (como `double`: o mesmo valor que o Python), `total_despesas_anuais = 1200`, `total_receitas_mensais = 3000`.
- **AC-005-91** Dado receita `ANUAL` 500, então não entra em nenhum total; dado agendamento com `valor_previsto` NULL, `0` ou negativo, então fora dos totais (mas, se `incluir_na_reserva`, negativo entra na reserva: ver AC-005-102); agendamento inativo, fora.
- **AC-005-92** Dado usuário com `meses_reserva_emergencia` NULL, então `meses_configurados = 6`; com 12 ⇒ 12; usuário sem linha em `usuarios` ⇒ 6.
- **AC-005-93** Dados, com `meses = 6`, `FIXO MENSAL 100`, `FIXO ANUAL 1200`, `LEMBRETE_VARIAVEL SEMANAL 10`, `FIXO QUINZENAL 50`, `FIXO DIARIA 2`, todos `incluir_na_reserva`, então `meta = 100×6 + 1200 + 10×26 + 50×12 + 2×180 = 3020,0`, `gasto_mensal_equivalente = round(3020/6, 2) = 503,33`, `meses_configurados = 6` (`round(6 × 4,33) = round(25,98) = 26`).
- **AC-005-94** Dado agendamento `PARCELADO` com `incluir_na_reserva`, então **não** entra na meta (desvio 014: só `FIXO` e `LEMBRETE_VARIAVEL`) mas entra nos totais; agendamento de outro usuário, `incluir_na_reserva = false` ou NULL, ou inativo, fora da meta.
- **AC-005-95** Dado nenhum agendamento marcado para a reserva e `total_despesas_mensais = 1000` com `meses = 6`, então `meta = 6000.0` e `gasto_mensal_equivalente = 1000.0` (regra do resumo); sem nenhum agendamento ⇒ `meta 0.0`, `gasto_mensal_equivalente 0.0`.
- **AC-005-96** Dado `meses = 0`, então `gasto_mensal_equivalente = 0` (sem divisão por zero) e `meta` conforme a fórmula (`0`; com despesas, o fallback multiplica por 0).
- **AC-005-97** Dado arredondamento, então `PyRound.Round2` devolve os mesmos valores que `round(x, 2)` do Python para a tabela de fixtures gerada pelo Python (`0.125 ⇒ 0.12`, `2.675 ⇒ 2.67`, `1.115 ⇒ 1.11`, `0.5 ⇒ 0.5`, negativos, valores grandes), o que `Math.Round(double, 2)` não garante.
- **AC-005-98** Dado o resumo, então as chaves saem na ordem `total_despesas_mensais, total_despesas_anuais, total_receitas_mensais, reserva_emergencia{meta, gasto_mensal_equivalente, meses_configurados}`; `meses_configurados` é inteiro; valores como número JSON.
- **AC-005-99** Dado o resumo do usuário A e agendamentos do usuário B, então nada do B entra.

**Serviço `IEmergencyReserveService`** (§7)
- **AC-005-100** Dado `GetStatusAsync`, então devolve `(MonthlyEquivalent, Total, Months)` com os mesmos números do `get_reserva_status` do Flask (tupla `gasto_mensal_equivalente, reserva_ideal, meses`) **sem arredondar**; sem nada marcado ⇒ `(0, 0, N)`.
- **AC-005-101** Dado `GetItemsAsync`, então devolve as linhas ativas, `incluir_na_reserva`, `FIXO`/`LEMBRETE_VARIAVEL`, ordenadas por impacto decrescente (e `id`), com `Impact` = valor × fator da periodicidade (mesmo fator do `GetStatusAsync`).
- **AC-005-102** Dado `valor_previsto` negativo marcado para a reserva, então é somado como está (paridade; o resumo o ignora nos totais, que exigem `> 0`).
- **AC-005-103** Dados `SEMANAL` e `meses = 50`, então `round(50 × 4,33) = round(216,5) = 216` (meio-par, como o Python), não 217.
- **AC-005-104** Dado o fator por periodicidade (tabela `MENSAL N`, `ANUAL 1`, `SEMANAL round(N×4,33)`, `QUINZENAL 2N`, `DIARIA 30N`), então `ReserveFactor` do Domain devolve exatamente isso para `N` de 1 a 36.

## 6. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `agendamentos` | SELECT, INSERT, UPDATE | nunca DELETE; coluna de ativo é `ativo` |
| `contas` | SELECT | posse + `ativa = true` (POST e PUT); nome no retorno |
| `subcategoria`, `macrocategoria`, `grupocategoria` | SELECT (`LEFT JOIN`) | nomes e grupo; existência só com P-005-2 |
| `usuarios` | SELECT | `meses_reserva_emergencia` (resumo e reserva) |
| `transacoes` | nenhuma | o vínculo `agendamento_id` é lido pela 013; esta feature não lança transação |

### 6.1 Constraints de `agendamentos` que a API encontra (`0000_baseline.sql:10-36`)
- `agendamentos_tipo_agendamento_check`: `FIXO`, `PARCELADO`, `LEMBRETE_VARIAVEL`; `agendamentos_periodicidade_check`: as 5 periodicidades.
- `agendamentos_dia_execucao_check`: 1..31; `agendamentos_mes_execucao_check`: NULL ou 1..12.
- `chk_anual_mes`: `(periodicidade = 'ANUAL' AND mes_execucao IS NOT NULL) OR (periodicidade <> 'ANUAL' AND mes_execucao IS NULL)`.
- `chk_parcelado`: `tipo_agendamento = 'PARCELADO' OR (total_parcelas IS NULL AND parcelas_executadas = 0)`.
- `descricao varchar(255) NOT NULL`, `valor_previsto numeric(15,2) NULL`, `data_inicio date NOT NULL`, `dia_execucao integer NOT NULL`, `notificar_antes_dias integer NULL DEFAULT 3`, `ativo boolean NOT NULL DEFAULT true`, `incluir_na_reserva boolean NULL DEFAULT true`, `parcelas_executadas integer NULL DEFAULT 0`.
- FKs: `usuario_id` CASCADE, `conta_id` CASCADE, `subcategoria_id` RESTRICT. `transacoes.agendamento_id` SET NULL. Trigger `trigger_update_agendamentos_updated_at`.
- Índices: `idx_agendamentos_usuario_id`, `idx_agendamentos_reserva (usuario_id, incluir_na_reserva, periodicidade) WHERE ativo = true`.

### 6.2 Tenant (`usuario_id` do JWT)
Todas as consultas filtram `a.usuario_id`. Pontos do Flask sem filtro de usuário, avaliados:

| Onde | Consulta | Risco |
|---|---|---|
| `api.py:2010, 2176` (releitura depois de gravar) | `WHERE a.id = :id` | nenhum (o id acabou de ser criado/verificado para o usuário); o .NET relê com `usuario_id` |
| `api.py:1823-1826` (`JOIN`s de subcategoria, macro, grupo, conta) | sem `usuario_id` | nenhum hoje para o texto; mas o `subcategoria_id` **não é validado** (P-005-2): um usuário pode apontar para a subcategoria **própria de outro usuário** e ver `subcategoria_nome` e `macro_categoria_nome` dela (vazamento de nomes de categoria) |
| `api.py:2117-2118` (PUT `subcategoria_id`) | nenhuma checagem | idem; e inexistente ⇒ 500 |

### 6.3 Dados de produção
38 agendamentos (28 ativos: conta 2, Cartão Inter, tem 5; conta 3, Mercado pago, tem 23; os 10 inativos são os removidos), `potesdegastos` vazio. O resumo e a reserva já têm dados reais para a conferência de paridade da Fase 3; `usuarios.meses_reserva_emergencia` do único usuário deve ser lido na homologação.

## 7. Serviço de reserva de emergência (porta para 011 e 012)

Em `MeuSecretario.Application/Bills/` (arquivo `EmergencyReserveContracts.cs`):

```csharp
public interface IEmergencyReserveService
{
    /// get_reserva_status: (gasto_mensal_equivalente, reserva_ideal, meses), sem arredondar.
    Task<EmergencyReserveStatus> GetStatusAsync(int userId, CancellationToken cancellationToken);

    /// Linhas incluídas na reserva, por impacto decrescente (detalhes da 012 e a reserva_handler da 011).
    Task<IReadOnlyList<EmergencyReserveItem>> GetItemsAsync(int userId, CancellationToken cancellationToken);
}

public sealed record EmergencyReserveStatus(double MonthlyEquivalent, double Total, int Months);

public sealed record EmergencyReserveItem(int ScheduleId, string Description, double Amount, string Periodicity, string? Category, double Impact);
```

- `MonthlyEquivalent = Total / Months` (`0` se `Months ≤ 0`); `Total` = soma por periodicidade × `ReserveFactor(periodicidade, Months)` (RN-005-11); `Months` = `COALESCE(meses_reserva_emergencia, 6)` (usuário inexistente ⇒ 6).
- Filtro único (desvio 014): `ativo = TRUE AND incluir_na_reserva = TRUE AND tipo_agendamento IN ('FIXO','LEMBRETE_VARIAVEL')` (com P-005-5, também exclui grupo de receita). Sem `valor_previsto > 0` (NULL some na soma; negativo entra).
- `EmergencyReserveItem.Category` é `subcategoria.nome_sub` (como o SQL do `whatsapp_handler.py:774-790`), `Impact = Amount × fator`.
- A 011 usa `GetStatusAsync` antes e depois de `PATCH .../reserva` (`reserve_handler.py:84, 119`); a 012 usa os dois para "Consulta Reserva" e "Consulta Detalhes Reserva". Nenhum deles repete a fórmula nem o SQL.
- O resumo (§3.5) é a única consumidora do `fallback` "despesas mensais × meses" e do `round(..., 2)`; ficam no caso de uso `GetBillsSummary`, não no serviço.

## 8. Desvios de paridade aprovados e como se aplicam

| # | Estado | Como se aplica |
|---|---|---|
| 008 | aprovado | os 5 erros 500 usam texto fixo (o GET deixa de vazar `str(e)`); log só com tipo (AC-005-84, 08) |
| 014 | aprovado | uma só fórmula de reserva com filtro de tipo; `PARCELADO` deixa de entrar na meta do resumo (AC-005-93, 94, 100 a 104). Impacto: `meta` pode mudar se houver `PARCELADO` marcado para a reserva (o modal do site permite marcar qualquer tipo de despesa); Q-005-3 confirma em produção |
| 024 | aprovado | corpo ausente/inválido ⇒ 400 `"Dados não fornecidos"` (AC-005-11, 40) |
| 011 | aprovado | **não é desta feature**: execução, incremento e desativação das parcelas são da 013; aqui só vale o invariante `total_parcelas ≥ 2` e `chk_parcelado` |
| 012 | aprovado | **não é desta feature**: "já pago no mês" por `agendamento_id` é do motor/check-in (013) |
| 013 | proposto/outro | motor de agendamentos: fora de escopo |

## 9. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask (coluna "Atual"). Recomendação: **aprovar todos**; P-005-3 é o mais importante porque o Angular já produz os 500 (AC-005-56).

| Id | Atual (arquivo:linha) | Proposta | Impacto | Recomendação |
|---|---|---|---|---|
| **P-005-1** Entradas que viram 500 ou texto errado | POST `descricao`/`tipo_agendamento`/`periodicidade` `null` ou não texto: `.strip()` falha ⇒ 500 (`api.py:1872, 1877, 1884`); PUT `descricao: null` grava o texto `"None"` e número vira texto (`:2060`); PUT `notificar_antes_dias` inválido ⇒ 500 por parâmetro faltando (`:2127-2132`); `valor_previsto` `NaN` passa e `inf`/estouro ⇒ 500 (`:1913-1918, 2109-2112`); `data_inicio` texto inválido ou `null` ⇒ 500 (`:1954-1956, 2134-2136`); `incluir_na_reserva` texto ⇒ 500 no POST e `bool("false") = true` no PUT (`:1958, 2140`) | POST: `descricao` não texto ⇒ linha 2; tipo/periodicidade não texto ⇒ linhas 3/4; PUT: `descricao` não texto ⇒ `"Descrição não pode ser vazia"`; `notificar_antes_dias` inválido ⇒ **ignorado** (intenção do código); `valor_previsto` não finito ou fora de `numeric(15,2)` ⇒ `"Valor previsto deve ser um número positivo"` (POST) / `"Valor previsto inválido"` (PUT); `data_inicio` fora de `YYYY-MM-DD` ⇒ 400 `"Data de início deve estar no formato YYYY-MM-DD"` (POST e PUT, `null` no PUT inclusive); `incluir_na_reserva` não booleano ⇒ 400 `"Campo 'incluir_na_reserva' deve ser verdadeiro ou falso"` (`null` no POST ⇒ `false`) | 500 e dados estranhos viram 400/ignorado. Duas mensagens novas | **Aprovar** (mesmo critério do desvio 033 da 002) |
| **P-005-2** Validar a subcategoria | POST e PUT não validam `subcategoria_id` (`api.py:1891-1893, 2116-2118`): inexistente ⇒ 500 por FK, `null` no PUT ⇒ 500, subcategoria própria de outro usuário é aceita e seu nome vaza na resposta | `subcategoria_id` precisa existir e ser global (`usuario_id IS NULL`) ou do usuário ⇒ senão 404 `"Subcategoria não encontrada"` (mesma regra e texto da 004); no PUT a checagem vem entre `valor_previsto` e `conta_id` (ordem dos campos); id não inteiro ⇒ 404 | Nenhum campo muda; fecha o vazamento de nomes entre usuários | **Aprovar** |
| **P-005-3** Consistência do estado resultante (`chk_anual_mes`, `chk_parcelado`, `valor_previsto`) | POST `ANUAL` sem mês ⇒ 500 (`:1936-1946`); PUT não confere os `CHECK` e não olha o estado resultante (`:2040-2203`): `ANUAL`⇄`MENSAL`, mês em periodicidade não anual, sair de `PARCELADO`, `total_parcelas` em tipo não parcelado ⇒ 500; PUT aceita `valor_previsto` ≤ 0 ou `null` em `FIXO`/`PARCELADO` | POST: `ANUAL` sem `mes_execucao` ⇒ 400 `"Mês de execução é obrigatório para periodicidade anual"`. PUT, sobre o estado **resultante** (corpo + gravado): periodicidade resultante ≠ `ANUAL` ⇒ `mes_execucao` vira NULL automaticamente (como o POST ignora o mês); resultante `ANUAL` sem mês ⇒ 400 com a mesma mensagem; tipo resultante ≠ `PARCELADO` ⇒ `total_parcelas = NULL` e `parcelas_executadas = 0` automaticamente (como o POST ignora as parcelas); tipo resultante `PARCELADO` com `total_parcelas` < `parcelas_executadas` ⇒ 400 `"Número de parcelas não pode ser menor que as parcelas já executadas"`; tipo resultante `FIXO`/`PARCELADO` com `valor_previsto` resultante `null` ⇒ 400 `"Valor previsto é obrigatório para contas fixas ou parceladas"`, ou `≤ 0` ⇒ 400 `"Valor previsto deve ser um número positivo"` | Edições do Angular que hoje dão 500 passam a funcionar; sair de `PARCELADO` zera o histórico de parcelas do agendamento (o agendamento deixa de ser parcelado). Três mensagens novas | **Aprovar** |
| **P-005-4** `Receitas Gerais` é receita | `api.py:1836, 2285`: só o grupo `Renda` é `Receita`. Pelo desvio 040, `Receitas Gerais` (grupo `Geral`) é oferecida como categoria de **receita** pelo site; o agendamento criado com ela aparece como `Despesa` e vira despesa no resumo | `tipo_transacao` e o resumo tratam como `Receita` o grupo `Renda` **ou** a macro `Receitas Gerais` do grupo `Geral` (mesmo predicado da 003); avisar a 013 (a transação gerada por esses agendamentos é `Renda`) | Agendamentos nessa categoria mudam de lado no resumo e na lista; hoje não existe nenhum em produção (a categoria só passa a ser oferecida com o desvio 040) | **Aprovar** |
| **P-005-5** Receita fora da reserva | `emergency_reserve_service.py:62`, `api.py:2335-2345`: a reserva soma `incluir_na_reserva = TRUE` sem olhar se é receita; o Angular só impede marcar receita (`bill-modal.component.ts`, `incluirNaReserva: false` para receita), a API não | O filtro da reserva exclui agendamentos de grupo de receita (P-005-4); o POST/PUT **não** recusa a marcação (só não conta) | Reserva nunca soma renda | **Aprovar** (sem efeito hoje: front impede) |

## 10. Decisões aplicadas

| Desvio / proposta | Estado | Onde |
|---|---|---|
| 008 sem vazamento de erro | aprovado | RN-005-14; AC-005-08, 84 |
| 014 reserva única | aprovado | RN-005-11; §7; AC-005-93, 94, 100 a 104 |
| 024 corpo sem Content-Type | aprovado | RN-005-15; AC-005-11, 40 |
| P-005-1 a P-005-5 | **pendentes** | §9; sem aprovação, o .NET reproduz o Flask (ACs marcados "muda com P-005-N") |

## 11. O que o Angular espera × o que o Flask devolve

Lido em `E:\Projetos\Projetos\meusecretariofront\src\app\features\finances\` (`bills.component.ts/.html`, `components/bill-modal.component.ts`, `services/finances.service.ts:279-365`, `models/finances.model.ts` seção "Contas mensais"). É o front mais recente e **alinhado** com o Flask nos nomes de campo (nenhum alias necessário). Conferido:

1. **Contrato de listagem e resumo bate.** O front lê exatamente os campos do GET (`tipo_transacao`, `grupo`, `subcategoria_nome`, `macro_categoria_nome`, `conta_nome`...) e o `summary` (`total_despesas_mensais`, `total_despesas_anuais`, `total_receitas_mensais`, `reserva_emergencia.meta|gasto_mensal_equivalente|meses_configurados`). O POST/PUT devolvem só 16 chaves (sem `tipo_transacao`, `grupo`, `macro_categoria_nome`); o front só emite o evento e recarrega tudo (`onBillSaved` ⇒ `loadAll`), então não há problema.
2. **Editar `ANUAL` para outra periodicidade dá 500.** O modal limpa o mês no formulário mas só envia `mes_execucao` quando `ANUAL` e preenchido; a chave não vai, o mês antigo continua gravado e o `CHECK` falha: `"Erro ao atualizar conta mensal"` (P-005-3).
3. **`ANUAL` sem mês dá 500 na criação.** O formulário não torna o mês obrigatório (P-005-3; alternativa do front: validar).
4. **Sair de `PARCELADO` com parcelas executadas dá 500.** O front manda `total_parcelas: null`, o `CHECK` exige também `parcelas_executadas = 0` (P-005-3).
5. **Não dá para limpar `valor_previsto`** (o front omite a chave quando vazio) nem `notificar_antes_dias: 0` (o front envia `valor || 3`).
6. **Receita nunca entra na reserva** pelo front (`incluir_na_reserva: false` forçado); a API não impede (P-005-5).
7. **Categorias** vêm de `GET /api/categories?tipo=Receita|Despesa` (003): a lista de `Receita` agora inclui `Receitas Gerais` (desvio 040) e o agendamento criado com ela vira `Despesa` na lista (P-005-4).
8. `data_inicio` é sempre enviada (padrão: hoje no navegador); `incluir_na_reserva` sempre booleano; `total_parcelas` `null` fora de `PARCELADO`.
9. Outros clientes: o bot e os atalhos usam `/api/agendamento/<id>/reserva` e `/api/agendamentos/reserva` (011), não estas rotas.

## 12. Fora de escopo

- **Motor de agendamentos** (lançar `FIXO`/`PARCELADO`/lembretes do dia), "já pago no mês", check-in noturno, avisos de vencimento, `schedule_processor`, `bills_service`, `agendamentos_queries` (013; desvios 011, 012 e 013).
- `PATCH /api/agendamento/<id>/reserva` e `GET /api/agendamentos/reserva` (011): usam `IEmergencyReserveService`.
- Consultas e respostas de reserva no WhatsApp (012).
- Criação do agendamento de parcelas da compra parcelada do bot (feature 004, §7.4).
- Potes de gasto, faturas e transações.
- CORS, `HEAD`/`OPTIONS` automáticos e a whitelist de segurança (009).

## 13. Perguntas em aberto

- **Q-005-1** `incluir_na_reserva` ausente no POST grava `false`, enquanto o default do banco é `true` e os agendamentos criados pelo bot (004 §7.4) herdam `true`. Manter o `false` do POST (padrão, é o que o Flask faz)?
- **Q-005-2** Semântica de `PARCELADO` criado pela API: `parcelas_executadas = 0` e `total_parcelas = N` ⇒ N execuções a partir de `data_inicio`; o bot cria com `parcelas_executadas = 1` (a primeira parcela é transação) e `data_inicio` no mês seguinte. A 013 precisa tratar os dois. Confirmar que o motor (013) executa `total_parcelas − parcelas_executadas` parcelas.
- **Q-005-3** `meses_reserva_emergencia` do usuário real e se existe agendamento `PARCELADO` marcado para a reserva em produção (o desvio 014 muda a `meta` só nesse caso).
- **Q-005-4** A soma do resumo depende da ordem do `double`: o Flask somava na ordem que o banco devolvia (indefinida). A spec fixa `ORDER BY periodicidade, nome_grupo`. Diferenças de último dígito contra o golden do Flask são aceitas (comparação semântica com tolerância no teste de contrato)?
- **Q-005-5** `descricao[:255]` corta em silêncio no POST/PUT (o Flask faz); manter (padrão) ou recusar com 400 como em 004/P-004-2?
- **Q-005-6** Texto das mensagens novas dos P-005-1/3 para aprovação do Rafael.
