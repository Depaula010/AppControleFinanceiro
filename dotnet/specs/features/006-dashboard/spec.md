# Spec 006 — Dashboard (resumo, gráficos, transações recentes e alertas)

- **Estado:** aprovada por delegação do Rafael (2026-10-04, "prossiga para a implementação sem esperar"). Implementa **só paridade com o Flask** mais os desvios já aprovados (001, 007, 009 e 008). Os desvios novos 089 a 095 estão **propostos e não implementados**; as dúvidas de fato ambíguo seguem o Flask e estão em §13.
- **Onda:** 3
- **Origem (Python):** `app/routes/api.py:34-90` (`token_required`), `:97-171` (`_get_dashboard_summary_impl`), `:174-178` (`/dashboard/summary`), `:181-280` (`/dashboard/charts`), `:927-952` (`/dashboard/resumo`), `:955-1014` (`_get_transacoes_recentes_impl`, `/transacoes/recentes`), `:1070-1095` (`/dashboard/stats`), `:1098-1127` (`/transactions/recent`), `:1130-1159` (`/dashboard/recent`), `:1162-1298` (`/dashboard/alerts`); `app/services/analytics_service.py:17-290` (`get_spending_analysis`, só os blocos 1, 2 e 3 chegam à rota); `app/services/finance_service.py:782-838` (`get_saldo_contas`).
- **Depende de:** 001a (`RequireUser`, `ICurrentUser`, `IDbConnectionFactory`, `Envelopes`, `IClock`), 002 (tabela `contas`), 003 (macros), 004 (`TransactionTypes`, `MonthDays`; a rota `/api/transactions/recent` convive com `/api/transactions/{id}` do módulo da 004). **Sem migration.**
- **Desvios aplicáveis:** 001 (só `Renda` no resumo; ver 089 para as recentes), 007 (um só saldo, consolidado: o Flask já faz isso no dashboard), 008 (500 com texto fixo e log sem segredo), 009 (nome do dia da semana calculado no código). Propostos: 089 a 095 (§9).
- **Consumidores:** Angular `features/dashboard` (`dashboard.service.ts`: `/api/dashboard/summary`, `/charts`, `/recent`, `/alerts`). Bot e atalhos não usam estas rotas.

## 1. Objetivo

A tela inicial do site mostra o saldo, as receitas e despesas do mês, um gráfico de gastos por mês, as 10 últimas transações e as contas agendadas que vencem nos próximos 7 dias. Tudo vale só para o usuário do token. A feature é **somente leitura**.

Esta feature **não** usa `IEmergencyReserveService` (005) nem `InvoiceCycle` (004): o dashboard do Flask não calcula reserva nem fatura. Reaproveita `PyRound` (arredondamento do Python), `TransactionTypes`, `MonthDays` e `IClock`.

## 2. Rotas

| Método | Caminho | Aliases | Auth | Envelope |
|---|---|---|---|---|
| GET | `/api/dashboard/summary` | `/api/dashboard/resumo`, `/api/dashboard/stats` | JWT | A |
| GET | `/api/dashboard/charts` | — (`?meses=`) | JWT | A |
| GET | `/api/dashboard/recent` | `/api/transactions/recent`, `/api/transacoes/recentes` | JWT | A |
| GET | `/api/dashboard/alerts` | — | JWT | A |

São **8 caminhos** (o inventário conta as 4 operações com aliases). Alias e rota principal têm comportamento idêntico (`api.py:927-952, 1070-1095, 1098-1159`). Como em todo `/api/*`: sem barra final ⇒ 404 do Flask; caminho que não existe (`/api/dashboard/x`) ⇒ 404; método diferente de GET ⇒ **405** (a rota existe só para GET). `HEAD` e `OPTIONS` não são reproduzidos (G-5, rejeitado). Falha de autenticação: as 4 mensagens 401 do `RequireUser`, antes de qualquer validação da rota (inclusive `meses`).

`/api/transactions/recent` é a única rota estática ao lado de `/api/transactions/{id}` (004): o literal vence o `{id}`; `POST|PUT|DELETE /api/transactions/recent` continuam 405 (já coberto pela 004, AC-004-82).

## 3. Contrato por rota

Convenções: dinheiro como número JSON (`double`); chaves na ordem escrita; o 503 `"Banco de dados não configurado"` não existe no .NET (sem `DATABASE_URL` o app nem sobe); "hoje" é a data de São Paulo do `IClock` (o Flask usava `date.today()` do container, constituição §7); `round` é o do Python (`PyRound.Round2`, meio-par sobre o valor binário).

### 3.1 `GET /api/dashboard/summary` (e `/resumo`, `/stats`)

Origem: `api.py:97-178`; saldo em `finance_service.py:782-838`. Sem corpo nem query. **200**
```json
{"status":"success","data":{"saldo_total":5430.5,"receitas_mes":8000.0,"despesas_mes":3245.3,"saldo_mes":4754.7,"mes_referencia":"Outubro/2026"}}
```
- **`saldo_total`** = soma, nas contas do usuário com `ativa = true`, de `saldo_inicial + COALESCE(SUM(valor), 0)` das transações com `consolidada = true` da conta (`LEFT JOIN`, de qualquer tipo e data, inclusive futura). Contas de cartão entram. Ordem das contas `ORDER BY tipo_conta, nome_conta` (e `id`). Arredondado: `round(total, 2)`. Sem contas ativas: `0.0`.
- **`receitas_mes`** = `abs(SUM(valor))` das transações do usuário com `tipo_transacao = 'Renda'`, `consolidada = true` e `data_transacao` entre o **dia 1 do mês de hoje** e **hoje**, inclusive. **`despesas_mes`** = o mesmo para `'Despesa'`. `Transferência` e `Pagamento Fatura` não entram. Sem filtro de conta: transações de conta inativa contam. Cada um arredondado a 2 casas.
- **`saldo_mes`** = `round(receitas_mes − despesas_mes, 2)` (a diferença é feita antes do arredondamento individual); pode ser negativo.
- **`mes_referencia`** = `"<Mês em português>/<ano>"` do mês de hoje (`Janeiro`, `Fevereiro`, `Março`, `Abril`, `Maio`, `Junho`, `Julho`, `Agosto`, `Setembro`, `Outubro`, `Novembro`, `Dezembro`), independente do `lc_time`/cultura do servidor.
- Chaves nesta ordem: `saldo_total, receitas_mes, despesas_mes, saldo_mes, mes_referencia`. Usuário sem dados: `0.0` nos quatro números e o mês de referência.

Erro inesperado: **500** `"Erro ao carregar resumo do dashboard"`.

### 3.2 `GET /api/dashboard/charts`

Origem: `api.py:181-280`; consultas em `analytics_service.py:29-95`. Query: `meses` (opcional).

**Leitura de `meses`:** `request.args.get('meses', 3, type=int)`. Ausente ⇒ 3. Presente: o valor passa por `int()` do Python; **se falhar (texto, vazio, decimal como `2.5`) vale o padrão 3, sem erro**. `int()` aceita espaços nas pontas, sinal `+`/`-` e `_` entre dígitos (`1_0` = 10). Repetido: vale o primeiro. Fora de 1..12 (inclusive negativo e número gigante) ⇒ **400** `"Parâmetro 'meses' deve estar entre 1 e 12"`.

Sucesso: **200**
```json
{"status":"success","data":{"gastos_mensais":[{"mes":"2026-10","total":3200.5}],"gastos_categoria":[{"macro_categoria":"Alimentação","subcategoria":"Supermercado","total":1200.0,"quantidade":45}],"gastos_dia_semana":[{"dia":"Monday","total":450.0,"quantidade":12}]}}
```
Todos os blocos usam só `tipo_transacao = 'Despesa'` e `consolidada = true`, e saem com `abs(total)`.
- **`gastos_mensais`** `{mes, total}`: transações com `data_transacao >= (dia 1 do mês de hoje) − 30 × meses dias` (sem limite superior), agrupadas por `YYYY-MM` (`TO_CHAR`), **ordem decrescente de `mes`** (mais novo primeiro). A janela de 30 dias por mês faz o primeiro mês sair **parcial** (desvio 090 proposto) e a ordem decrescente faz o gráfico do Angular mostrar o mês mais novo à esquerda (091 proposto).
- **`gastos_categoria`** `{macro_categoria, subcategoria, total, quantidade}`: despesas do **mês de hoje** (dia 1 até o fim do mês, qualquer dia do mês), agrupadas por `nome_macro` e `nome_sub`, `quantidade` = `COUNT(*)`. **`ORDER BY total DESC LIMIT 10`** sobre a soma **negativa**: sai primeiro a categoria que menos gastou, e o `LIMIT` leva as 10 de **menor** gasto (desvio 092 proposto; reproduzido). Desempate (sem mudar contrato): `nome_macro`, `nome_sub`.
- **`gastos_dia_semana`** `{dia, total, quantidade}`: despesas com `data_transacao >= hoje − 90 dias` (sem limite superior), agrupadas por dia da semana (`EXTRACT(DOW)`, domingo = 0), ordenadas por esse número. `dia` é o nome **em inglês, sem preenchimento** (`Sunday`, `Monday`, `Tuesday`, `Wednesday`, `Thursday`, `Friday`, `Saturday`): o Flask devolve o `TO_CHAR(..., 'Day')` com `lc_time = C` (confirmado em produção) e `.strip()`. O desvio 009 (aprovado) pede o cálculo no código mantendo essa grafia. Dia sem despesa não aparece.
- `meses` só altera `gastos_mensais`; categoria é sempre o mês de hoje e dia da semana são sempre 90 dias (§13, Q-006-3).
- Bloco sem linhas: `[]`.

Erro inesperado: **500** `"Erro ao carregar dados dos gráficos"`.

### 3.3 `GET /api/dashboard/recent` (e `/api/transactions/recent`, `/api/transacoes/recentes`)

Origem: `api.py:955-1014, 1098-1159`. Sem corpo nem query. **200**
```json
{"status":"success","data":[{"id":330,"descricao":"Mercado","valor":-85.9,"tipo":"Despesa","data":"2026-10-03","categoria":"Alimentação","conta":"Mercado pago"}]}
```
- As **10** últimas transações do usuário: `ORDER BY data_transacao DESC, created_at DESC` (desempate por `id DESC`, sem mudar contrato), `INNER JOIN` com subcategoria, macro e conta (todas as FKs são `NOT NULL`, nenhuma linha some).
- **Sem filtro** de tipo, de `consolidada` nem de conta ativa: entram `Transferência`, `Pagamento Fatura`, não consolidadas e contas inativas.
- `valor` com o sinal gravado (despesa negativa). **`tipo` é o valor do banco**, sem conversão: `Renda`, `Despesa`, `Transferência`, `Pagamento Fatura`. Este é o ponto onde o desvio 001 (aprovado: "`Renda` ⇒ `Receita` na saída") colide com o Angular, que compara `tipo === 'Renda'` (`dashboard.component.ts:289-302`); reproduzimos o Flask e propomos o 089.
- `data` = `YYYY-MM-DD`; `categoria` = nome da **macro**; `conta` = `nome_conta`. Chaves: `id, descricao, valor, tipo, data, categoria, conta`.
- Sem transações: 200 `{"status":"success","data":[]}`.

Erro inesperado: **500** `"Erro ao carregar transações recentes"`.

### 3.4 `GET /api/dashboard/alerts`

Origem: `api.py:1162-1298`. Sem corpo nem query. **200**
```json
{"status":"success","data":[{"id":"42","descricao":"Aluguel","subcategoria":"Aluguel / Condomínio","valor_previsto":1200.0,"dia_execucao":5,"dias_restantes":2,"data_vencimento":"2026-10-06","tipo":"warning","tipo_agendamento":"FIXO"}]}
```
Origem dos dados: `agendamentos` do usuário com `ativo = true` (`LEFT JOIN subcategoria`), `ORDER BY dia_execucao` (desempate `id`). Sem filtro de tipo, de conta ativa nem de receita. Para cada um, o vencimento `due` é calculado (todos os cálculos com `hoje` e `fim = hoje + 7 dias`):
- **`ANUAL`**: sem `dia_execucao` ou sem `mes_execucao` ⇒ ignorado. `due = (ano de hoje, mes, min(dia, último dia do mês))`; se `due < hoje`, o mesmo no **ano seguinte** (também com o corte do último dia). Mês fora de 1..12 ⇒ ignorado (o Python captura `ValueError`).
- **`SEMANAL`, `QUINZENAL`, `DIARIA`** (intervalos 7, 14, 1): sem `data_inicio` ⇒ ignorado. `due = data_inicio`; se `due < hoje`, `due += ceil((hoje − due) / intervalo) × intervalo` dias. `data_inicio` futura vale como está (e só entra se cair na janela). `DIARIA` sempre cai em hoje.
- **`MENSAL`** (e qualquer outro texto; periodicidade nula também é `MENSAL`): sem `dia_execucao` ⇒ ignorado. `due = (mês de hoje, min(dia, último dia do mês))`; se `due < hoje`, `(mês seguinte, min(dia, último dia do mês seguinte))`, virando o ano em dezembro. **`data_inicio` não é considerada** (desvio 093).
- Se `due > hoje + 7`, o agendamento sai. `dias_restantes = due − hoje` (0 a 7). `tipo`: `0` ⇒ `danger`, `1` ou `2` ⇒ `warning`, `3` a `7` ⇒ `info`.
- Campos: `id` **string**, `descricao`, `subcategoria` (`nome_sub` ou `null`), `valor_previsto` (número, ou `null` se NULL **ou zero**: `float(v) if v else None`), `dia_execucao` (o inteiro da coluna, mesmo em `ANUAL`/`SEMANAL`), `dias_restantes`, `data_vencimento` (`YYYY-MM-DD`), `tipo`, `tipo_agendamento` (`FIXO`, `PARCELADO`, `LEMBRETE_VARIAVEL`). `notificar_antes_dias` é lido e ignorado: a janela é sempre 7 dias. Ordem final: `dias_restantes` crescente, **estável** sobre `dia_execucao, id`.
- Nenhum item: 200 `{"status":"success","data":[]}`.

Erro inesperado: **500** `"Erro ao buscar alertas"`.
## 4. Regras de negócio

- **RN-006-01** As 8 rotas exigem JWT; `user_id` vem só do token (`ICurrentUser`). *Origem: `api.py:34-90, 174-1162`.*
- **RN-006-02** Tenant: toda consulta filtra `usuario_id = <token>` (a soma de saldo filtra `c.usuario_id` e junta `t.conta_id = c.id`). Nunca existe dado de outro usuário na resposta. *Origem: `api.py:111-116, 962-972, 1195-1203`; `analytics_service.py`.*
- **RN-006-03** "Hoje" é a data de São Paulo do `IClock`, em **todas** as rotas, inclusive nos filtros que o Flask delegava ao banco (`CURRENT_DATE`, `TO_CHAR(..., 'YYYY-MM') = :mes`). Janela do mês e do dia: datas, nunca `datetime`. Constituição §7.
- **RN-006-04** Só `Renda` conta como receita e só `Despesa` como despesa, no resumo e nos gráficos (desvio 001: o banco nunca tem `Receita`; nada a converter). *Origem: `api.py:131-155`.*
- **RN-006-05** Saldo = `saldo_inicial + SUM(valor)` das transações **consolidadas** da conta, só contas ativas (desvio 007; é o que o Flask já faz). *Origem: `finance_service.py:782-838`.*
- **RN-006-06** Arredondamento a 2 casas (`PyRound.Round2`) só no resumo (4 números). Gráficos e alertas devolvem o `float` do banco (`NUMERIC(15,2)` ⇒ `double`) sem arredondar; recentes idem. *Origem: `api.py:157-163`.*
- **RN-006-07** O sinal: o resumo e os gráficos devolvem módulo (`abs`); as recentes devolvem o valor gravado. *Origem: `api.py:140, 215, 226, 981`.*
- **RN-006-08** `meses`: `int()` do Python com fallback silencioso para 3; faixa 1..12 ⇒ senão 400. *Origem: `api.py:208-215`.*
- **RN-006-09** Nome do dia da semana (desvio 009): calculado no código, inglês, sem preenchimento, ordem domingo a sábado; independe do `lc_time` do banco. *Origem: `analytics_service.py:79-91, 221-227`.*
- **RN-006-10** Cálculo do vencimento dos alertas: §3.4; funções puras (`DashboardAlerts`) sem I/O. `calendar.monthrange` ⇒ `DateTime.DaysInMonth`. *Origem: `api.py:1213-1286`.*
- **RN-006-11** O 500 devolve sempre o texto fixo da rota; a exceção vai ao log só como tipo (e `SqlState`), sem descrição, valor nem corpo (desvio 008). EventIds 6001–6004. O Flask hoje imprime `str(e)` e o traceback. *Origem: `api.py:164-171, 270-280, 1000-1007, 1293-1297`.*
- **RN-006-12** Nenhuma escrita: as rotas só leem; a transação ambiente não é necessária (cada consulta numa conexão curta; a leitura do resumo e dos gráficos pode usar a mesma conexão sem transação, como o Flask).
- **RN-006-13** Desempates determinísticos (id e nomes) onde o Flask não ordenava por completo; não alteram o contrato (a ordem do Flask nesses empates era indefinida).

## 5. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","006-NN")]`); os de integração comparam o corpo como texto exato.

**Autenticação, roteamento e erros (comuns)**
- **AC-006-01** Dado nenhum `Authorization`, `Token abc`, token expirado/adulterado/sem `user_id`, quando qualquer dos 8 caminhos, então as 4 mensagens 401 do `RequireUser` (inclusive com `meses=99` em charts).
- **AC-006-02** Dados os 3 aliases do resumo e os 3 caminhos das recentes, quando chamados com os mesmos dados, então status e corpo idênticos.
- **AC-006-03** Dado `/api/dashboard/summary/`, `/api/dashboard/charts/`, `/api/dashboard/recent/`, `/api/dashboard/xyz`, então 404.
- **AC-006-04** Dado `POST|PUT|DELETE` nos 8 caminhos, então 405.
- **AC-006-05** Dado que o repositório lança exceção (inclusive `PostgresException` com texto interno), então 500 com o texto fixo da rota (§3), sem trecho da exceção; o log leva só o tipo (desvio 008).
- **AC-006-06** Dados dois usuários com dados nas 4 operações, então cada um só vê os seus.

**Resumo**
- **AC-006-10** Dado usuário sem contas nem transações, então `saldo_total`, `receitas_mes`, `despesas_mes` e `saldo_mes` valem `0.0`.
- **AC-006-11** Dadas 2 contas ativas, `saldo_inicial` 100 e 50, transações consolidadas +30 e −10, então `saldo_total = 170.0`.
- **AC-006-12** Dada transação não consolidada, então não entra no saldo, nas receitas nem nas despesas.
- **AC-006-13** Dada conta inativa, então seu `saldo_inicial` e suas transações não entram no `saldo_total`, mas as transações dela do mês entram em `receitas_mes`/`despesas_mes`.
- **AC-006-14** Dada a data de hoje 2026-10-15, então entram transações de 2026-10-01 a 2026-10-15; ficam fora 2026-09-30 e 2026-10-16. Dado o relógio às 23:30 de São Paulo (já é o dia seguinte em UTC), então vale o dia de São Paulo.
- **AC-006-15** Dadas transações `Renda`, `Despesa`, `Transferência` e `Pagamento Fatura` consolidadas no mês, então só as duas primeiras entram; os totais saem em módulo.
- **AC-006-16** Dado `Renda` 100,00 e `Despesa` −250,50, então `saldo_mes = -150.5`.
- **AC-006-17** Dado `receitas 0.30` e `despesas 0.10` (subtração binária `0.19999999999999998`), então `saldo_mes = 0.2` (`PyRound`); os fixtures do Python cobrem os casos de meio-par.
- **AC-006-18** Dados os 12 meses, então `mes_referencia` é `Janeiro/2026` ... `Dezembro/2026` (com acento em `Março`), com a cultura do processo qualquer.
- **AC-006-19** Dado o resumo, então as chaves saem em `saldo_total, receitas_mes, despesas_mes, saldo_mes, mes_referencia`.
- **AC-006-20** Dado conta de cartão com saldo negativo (compras), então entra no `saldo_total` com o sinal.

**Gráficos**
- **AC-006-30** Dado sem `meses`, então vale 3 (janela do `gastos_mensais`: dia 1 do mês de hoje − 90 dias).
- **AC-006-31** Dado `meses=1` e `meses=12`, então 200; dado `0`, `13`, `-1`, `99999999999999999999`, então 400 `"Parâmetro 'meses' deve estar entre 1 e 12"`.
- **AC-006-32** Dado `meses=abc`, `meses=`, `meses=2.5`, então vale 3 (200); `meses=%205`, `+5` e `1_0` valem 5, 5 e 10; `meses=2&meses=9` vale 2.
- **AC-006-33** Dadas despesas consolidadas em vários meses, então `gastos_mensais` traz `{mes, total}` em ordem decrescente de mês, com `total` em módulo, só `Despesa` consolidada, e a fronteira da janela inclusiva (`>=`).
- **AC-006-34** Dadas despesas do mês de hoje, então `gastos_categoria` agrupa por macro e subcategoria, com `quantidade`; despesa de outro mês fica fora.
- **AC-006-35** Dadas 12 categorias com gastos diferentes no mês, então saem 10, **da de menor gasto para a de maior** (comportamento do Flask, desvio 092), com o desempate por nomes.
- **AC-006-36** Dadas despesas dos últimos 90 dias, então `gastos_dia_semana` sai de domingo a sábado, com nomes ingleses sem espaço, `total` em módulo e `quantidade`; despesa com 91 dias fica fora; dia sem despesa não aparece.
- **AC-006-37** Dado 2026-10-04 (domingo), então o dia sai `Sunday`, independentemente do `lc_time` do banco (desvio 009).
- **AC-006-38** Dado `meses=12`, então só `gastos_mensais` muda; `gastos_categoria` e `gastos_dia_semana` iguais aos de `meses=1`.
- **AC-006-39** Dado usuário sem despesas, então os três blocos saem `[]`.

**Recentes**
- **AC-006-40** Dadas 12 transações, então saem as 10 mais novas, ordenadas por data decrescente, depois `created_at` decrescente, depois `id` decrescente.
- **AC-006-41** Dadas transações dos 4 tipos, não consolidada e de conta inativa, então todas entram.
- **AC-006-42** Dada uma `Renda` e uma `Despesa`, então `tipo` sai `Renda`/`Despesa` (sem `Receita`), `valor` com sinal, `data` ISO, `categoria` = macro, `conta` = nome da conta; chaves `id, descricao, valor, tipo, data, categoria, conta`.
- **AC-006-43** Dado usuário sem transações, então `{"status":"success","data":[]}`.

**Alertas**
- **AC-006-50** Dados agendamentos inativos e de outro usuário, então não aparecem; de conta inativa, aparecem.
- **AC-006-51** Dado `MENSAL` com `dia` igual ao de hoje, então `dias_restantes 0` e `danger`; com dia anterior a hoje, então só aparece se o próximo mês cair em até 7 dias; `dia 31` em mês de 30 dias cai no dia 30.
- **AC-006-52** Dado vencimento em hoje + 7, então entra (`info`); em hoje + 8, então não.
- **AC-006-53** Dados `dias_restantes` 0, 1, 2, 3, então `danger`, `warning`, `warning`, `info`.
- **AC-006-54** Dado `ANUAL` com o mês/dia de daqui a 3 dias, então entra; com a data de ontem, então vai para o ano seguinte e não entra; `29/02` em ano comum cai em 28/02; `ANUAL` sem mês ⇒ ignorado.
- **AC-006-55** Dado `SEMANAL` com `data_inicio` 10 dias atrás, então `due = data_inicio + 14`; `QUINZENAL` com `data_inicio` hoje − 1, então `due = hoje + 13` (fora); `DIARIA` ⇒ `dias_restantes 0`; `data_inicio` futura em 3 dias ⇒ `dias_restantes 3`.
- **AC-006-56** Dado `SEMANAL` sem `data_inicio` (teste de domínio), então ignorado.
- **AC-006-57** Dados vários alertas, então ordenados por `dias_restantes` crescente; empate por `dia_execucao`, depois `id`.
- **AC-006-58** Dado `valor_previsto` NULL ou `0`, então `null`; `1200.5` sai número com centavos.
- **AC-006-59** Dado o alerta, então `id` string, `subcategoria` `null` quando não houver (teste de domínio; o banco exige a FK), `dia_execucao` inteiro, `data_vencimento` ISO, chaves na ordem da §3.4.
- **AC-006-60** Dado hoje 2026-12-28 e `MENSAL` dia 3, então `data_vencimento 2027-01-03`, `dias_restantes 6`.
- **AC-006-61** Dado o relógio às 23:30 de São Paulo, então "hoje" é a data de São Paulo.
- **AC-006-62** Dado usuário sem agendamentos ativos, então `{"status":"success","data":[]}`.

## 6. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `contas` | SELECT | `ativa`, `saldo_inicial`, `tipo_conta`, `nome_conta` |
| `transacoes` | SELECT | resumo, gráficos e recentes; nunca escrita |
| `subcategoria`, `macrocategoria` | SELECT | nomes de categoria; `INNER JOIN` nas recentes e nos gráficos |
| `agendamentos` | SELECT | alertas; coluna de ativo é **`ativo`** (em `contas` é `ativa`) |

Índices úteis já existem (`idx_agendamentos_usuario_id`; transações por usuário e data). Sem `INSERT`, `UPDATE` nem `DELETE`. Dados de produção: 330 transações, 4 contas e 28 agendamentos ativos de um usuário; nenhum `Receita`.

## 7. Desvios de paridade aprovados e como se aplicam

| # | Estado | Como se aplica |
|---|---|---|
| 001 | aprovado | o resumo soma `Renda` (já era assim); **recentes: não aplicado**, ver 089 e Q-006-1 |
| 007 | aprovado | `saldo_total` já é consolidado e de contas ativas; nada muda aqui (a 002 alinhou `GET /api/accounts`) |
| 008 | aprovado | os 4 erros 500 usam texto fixo; log só com tipo (AC-006-05) |
| 009 | aprovado | nome do dia da semana calculado no código, em inglês e sem preenchimento (AC-006-36, 37) |
| 024 | n/a | as rotas não leem corpo |

## 8. Decisões de desenho

- Uma classe de caso de uso por rota (`GetDashboardSummary`, `GetDashboardCharts`, `GetRecentTransactions`, `GetDashboardAlerts`); cálculo puro em `Domain/Dashboard`.
- Repositório Dapper próprio (`DashboardRepository`), sem tocar nos repositórios da 002, 004 e 005.
- O vencimento dos alertas é uma função pura recebendo uma linha crua e `hoje`; sem `IClock` no Domain.

## 9. Desvios propostos (não implementados; sem aprovação o .NET reproduz o Flask)

| Id | Atual (arquivo:linha) | Proposta | Recomendação |
|---|---|---|---|
| **089** Recentes: `Renda` × `Receita` | `api.py:981` devolve o `tipo_transacao` cru (`Renda`); o desvio 001 aprovado manda `Renda ⇒ Receita` na saída; o Angular compara `'Renda'` (`dashboard.component.ts:289-302`, `dashboard.model.ts:49`) | decidir: (a) manter `Renda` nas recentes (excluir esta rota do 001) ou (b) mudar para `Receita` junto com o Angular | **(a)** até o front mudar |
| **090** Janela dos gastos mensais | `analytics_service.py:32`: `dia 1 do mês − 30 × meses` dias; para `meses=3` em outubro começa em 3 de julho: o primeiro mês sai parcial | início = dia 1 do mês de `hoje − (meses − 1)` meses ⇒ `meses` meses civis completos incluindo o atual (para `meses=1`: só o mês atual) | **Aprovar** |
| **091** Ordem dos gastos mensais | `analytics_service.py:45`: `ORDER BY mes DESC`; o Angular desenha na ordem recebida (`dashboard.component.ts:199-200`), então o mês mais novo fica à esquerda | ordem crescente | **Aprovar** (ou o front inverte) |
| **092** Top 10 de categorias invertido | `analytics_service.py:69-70`: `ORDER BY total DESC LIMIT 10` com soma negativa devolve as 10 categorias de **menor** gasto | `ORDER BY total ASC` (maior gasto primeiro); nenhum campo muda | **Aprovar** (o Angular de hoje não exibe este bloco) |
| **093** Alertas ignoram `data_inicio` em `MENSAL` e `ANUAL` | `api.py:1252-1269`: agendamento que começa no futuro (ex.: parcela do bot com `data_inicio` no mês seguinte) já alerta neste mês | só considerar vencimentos `>= data_inicio` | **Aprovar** |
| **094** Alerta de conta já paga | `api.py:1212-1286`: não olha se a ocorrência do mês já virou transação; a conta paga continua em `warning`/`danger` até o dia | excluir a ocorrência com transação ligada por `agendamento_id` no mês (regra do desvio 012) | **Aprovar** |
| **095** Receita no alerta de "contas a vencer" | `api.py:1176-1196`: agendamento de grupo `Renda` aparece como alerta vermelho | excluir agendamentos de receita (mesmo predicado da 005/075) | **Aprovar** |

## 10. Como ficou na implementação

1. **Paridade total, sem desvio implementado.** Os desvios 089 a 095 seguem `proposto`; nenhum alterou o código. Recentes devolvem `Renda` (Q-006-1); gráficos reproduzem a janela de 30 dias, a ordem decrescente e o top 10 de menor gasto; alertas ignoram `data_inicio` em `MENSAL`/`ANUAL`, conta paga e receita.
2. **Roteamento:** 8 `MapGet` com `RequireUser`; 405 nos outros métodos vem do roteamento (testado em POST/PUT/DELETE nos 8 caminhos, inclusive `/api/transactions/recent`, que convive com o `{id}` da 004). Caminho desconhecido e barra final dão 404 (sem corpo HTML do Flask garantido pelo `UnmatchedRoute` da 009).
3. **Janela de datas:** todas as janelas usam `IClock.Today`; a de categorias é `[dia 1, dia 1 do mês seguinte)` (equivale ao `TO_CHAR = mes` do Flask). O resumo filtra por `data <= hoje`.
4. **Soma do saldo:** `decimal` exato convertido para `double` antes do `PyRound` (Q-006-7).
5. **Nome do dia da semana:** inglês sem preenchimento (Q-006-6).
6. **Testes:** 80 unitários (Domain, Application, Api) e 65 de integração com PostgreSQL real, tudo com `[Trait("AC","006-NN")]`; fixtures de teste da 004 e 005 reaproveitadas sem edição. Nenhum arquivo compartilhado foi alterado (só `parity-deviations/README.md`, com as linhas 089 a 095).

## 11. O que o Angular espera × o que o Flask devolve

Lido em `meusecretariofront/src/app/features/dashboard/` (`dashboard.service.ts`, `models/dashboard.model.ts`, `dashboard.component.ts`).
1. **Usa 4 rotas:** `/api/dashboard/summary`, `/charts?meses=` (padrão do serviço **6**), `/recent`, `/alerts`; os aliases não são chamados.
2. **Resumo e alertas batem** com os modelos (`DashboardSummary`, `UpcomingAlert`, incluindo `id` string).
3. **Gráfico mensal** usa só `gastos_mensais` e na ordem recebida (mais novo à esquerda, 091); `gastos_categoria` e `gastos_dia_semana` estão declarados no modelo mas **não são exibidos**.
4. **Recentes** comparam `tipo` com `'Renda'` e `'Despesa'` (ícone e cor); com `Receita` todo ícone ficaria neutro (089).
5. O front trata qualquer erro como falha genérica (`handleError`); nenhuma mensagem do backend é exibida em 5xx.

## 12. Fora de escopo

- Reserva de emergência e resumo de contas mensais (`/api/bills/summary`, 005); faturas (004); potes (`PotesDeGastos` é lido por `get_spending_analysis`, mas o resultado **não** chega à rota); insights com IA (`generate_ai_insights`, 012).
- Os blocos 4 a 8 de `get_spending_analysis` (comparação mensal, potes, maiores gastos, delivery, contas fixas): só o WhatsApp os usa (012).
- `HEAD`/`OPTIONS`, CORS e a whitelist de segurança (009).

## 13. Perguntas em aberto

- **Q-006-1** Recentes devolvem `Renda` (Flask) ou `Receita` (desvio 001, aprovado em bloco)? Implementado: **`Renda`** (o front quebraria). Ver 089.
- **Q-006-2** O `meses` padrão do serviço do Angular é 6 e o do Flask é 3; sem efeito no contrato (o front sempre envia o parâmetro). Manter o 3.
- **Q-006-3** `meses` só afeta `gastos_mensais`; `gastos_categoria` (mês de hoje) e `gastos_dia_semana` (90 dias) ignoram o parâmetro. É a intenção? Implementado como o Flask.
- **Q-006-4** `valor_previsto = 0` sai `null` nos alertas (documentado em `api-contract.md` §4). Implementado como o Flask.
- **Q-006-5** `abs()` por grupo nos gráficos transforma o saldo positivo de uma categoria (estorno maior que as compras) em gasto. Sem proposta; só registro.
- **Q-006-6** A comparação por texto do 009 diz "sem remover o padding", mas o Flask faz `TRIM` no SQL e `.strip()` no Python: o `dia` sai sem espaços. Implementado **sem** espaços.
- **Q-006-7** A ordem de soma do `saldo_total` (double) pode diferir do `sum()` do Python (que compensa a soma a partir da 3.12) no último dígito antes do `round`; como as parcelas têm 2 casas, o resultado arredondado é o mesmo. Comparação do contrato semântica.
