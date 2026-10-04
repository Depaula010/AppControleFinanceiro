# Spec 004 — Transações (listar, criar, atualizar, remover) e serviço de lançamento

- **Estado:** aprovada em 2026-10-04 (recomendações de foundation/decisoes-onda-2.md; onde a spec diverge, vale esse documento)
- **Onda:** 2
- **Origem (Python):** `app/routes/api.py:728-901` (`GET /api/transactions`), `api.py:1301-1459` (POST), `api.py:1462-1635` (PUT), `api.py:1638-1695` (DELETE); filtro `token_required` em `api.py:34-90`; `app/services/finance/invoice_service.py:13-77` (`get_or_create_fatura`) e `:80-210` (`ensure_current_invoice_exists`); `app/services/finance/transaction_service.py:16-254` (`create_transaction`, `create_transfer_pair`, `create_fatura_payment`); `app/services/finance/installment_service.py:13-77`; usos do Flask que o serviço de lançamento precisa substituir: `routes/webhooks/handlers/whatsapp_handler.py:324-386` (confirmação com parcelamento), `:1080-1110` (pagamento de fatura), `routes/webhooks/handlers/transaction_handler.py:56-150, 284-420` (Automate e `/api/transacao`), `routes/webhooks/intents/transaction_intents.py:194-295, 349-440` (despesa e transferência).
- **Depende de:** 001a (`RequireUser`, `ICurrentUser`, `IDbConnectionFactory`, `Envelopes`), 002 (contas; só a tabela, sem usar `IAccountRepository`), 003 (`ICategoryLookup`), migration baseline `0000` e a nova `0050`.
- **Desvios aplicáveis:** 001, 008, 010, 011 (só na criação do agendamento de parcelas), 024, 027, 030, 039 (todos aprovados); P-004-1 a P-004-7 propostos (§9). Decisões em §10.
- **Consumidores do serviço de lançamento (§7):** 011 (automação), 012 (WhatsApp), 013 (jobs, só `IInvoiceService`).

## 1. Objetivo

O usuário do site lista suas transações com filtros e paginação, cria uma receita ou despesa (com vínculo automático à fatura quando a conta é cartão), edita parcialmente e remove (levando junto o par de uma transferência). Toda operação vale só para os dados do usuário do token.

A feature também entrega **a única porta de domínio para lançar transações** (simples, transferência, pagamento de fatura e compra parcelada), que 011 e 012 passam a usar em vez de reimplementar `create_transaction`, `create_transfer_pair`, `create_fatura_payment` e `create_parcelamento_agendamento` (§7), mais o cálculo do ciclo de fatura (R4).

> **Achado (análise estática, precisa de confirmação em homologação).** As três rotas de escrita **não funcionam em produção hoje**, por dois motivos independentes: (1) `INSERT`/`UPDATE` usam as colunas `observacoes` e `origem`, que não existem (desvio 027); (2) o Flask roda com SQLAlchemy 2.0.30 (`requirements.txt:4`) e as três rotas executam um `SELECT` (que abre a transação implícita) antes de `with conn.begin():` (`api.py:1413`, `1603`, `1672`); no SQLAlchemy 2.0 isso lança `InvalidRequestError`, que o `except` converte em 500 (`"Erro ao criar transação"`, `"Erro ao atualizar transação"`, `"Erro ao deletar transação"`). Consequência: as 330 transações de produção vieram do bot e dos atalhos, e **os corpos de sucesso do POST/PUT/DELETE abaixo são o contrato pretendido pelo código e pelo Angular, nunca observado em produção** (o teste de contrato da Fase 3 só consegue capturar do Flask o GET e os erros). Ver pergunta Q-004-1.

## 2. Rotas

| Método | Caminho | Alias | Auth | Envelope |
|---|---|---|---|---|
| GET | `/api/transactions` | — | JWT (`RequireUser`) | A (com `data`) |
| POST | `/api/transactions` | — | JWT | A |
| PUT | `/api/transactions/<int:id>` | — | JWT | A |
| DELETE | `/api/transactions/<int:id>` | — | JWT | A |

Não há alias em português nesta feature (`/api/transacoes/recentes` e `/api/transactions/recent` são da 006; `/api/transacao` é a 011). Como em todo `/api/*`: sem barra final (404 do Flask), `<int:id>` não numérico ou negativo ⇒ **404 HTML do Flask** antes da autenticação, método não permitido ⇒ 405. Particularidade: `/api/transactions/recent` existe só para GET (006); `PUT|DELETE|POST /api/transactions/recent` respondem **405** (a rota estática existe), e não 404.

Envelope A de erro: `{"status":"error","message":"..."}`. Falha de autenticação: as 4 mensagens 401 do `RequireUser` (spec 001 §4.4), antes de qualquer validação da rota.

## 3. Contrato por rota

Convenções (iguais às da spec 002 §3): "número" é o que o Python converte com `float()` (`PyNumber.TryMoney`, reaproveitado da 002), "inteiro" o que `int()` aceita; chaves de resposta na ordem escrita; dinheiro como número JSON (`100` no .NET, `100.0` no Flask); o 503 `"Banco de dados não configurado"` não existe no .NET. Coluna "Hoje (Flask)" só aparece onde a proposta P-004-N mudaria o comportamento; **sem aprovação o .NET faz o que está na coluna "Flask"**.

### 3.1 `GET /api/transactions`

Origem: `api.py:728-901`. Sem corpo. Query (todas opcionais):

| Parâmetro | Leitura (Flask) | Regra |
|---|---|---|
| `limit` | `request.args.get('limit', 20, type=int)` (`api.py:774`) | ausente, vazio ou texto que `int()` não converte ⇒ **20** (o Werkzeug devolve o padrão); espaços nas pontas e sinal são aceitos (`" 5 "`, `"+5"`); vários `limit` na URL ⇒ vale o primeiro |
| `offset` | idem, padrão 0 (`api.py:775`) | idem |
| `tipo` | `request.args.get('tipo')` | vazio ⇒ ignorado |
| `data_inicio`, `data_fim` | idem | vazio ⇒ ignorado |

Validações, **nesta ordem** (a primeira que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | `limit` < 1 ou > 100 (inclusive número que não cabe em `int`) | 400 `"Parâmetro 'limit' deve estar entre 1 e 100"` |
| 2 | `offset` < 0 | 400 `"Parâmetro 'offset' deve ser maior ou igual a 0"` |
| 3 | `tipo` não vazio e diferente de `Receita`/`Despesa` (exato, com maiúscula; `Renda` e `Transferência` são inválidos) | 400 `"Parâmetro 'tipo' deve ser 'Receita' ou 'Despesa'"` |
| 4 | `data_inicio` ou `data_fim` não vazio e que não é `YYYY-MM-DD` válido (RN-004-06) | 400 `"Datas devem estar no formato YYYY-MM-DD"` |

Filtros aplicados (todos com `usuario_id` do token): `tipo=Receita` ⇒ `tipo_transacao = 'Renda'` (desvio 001); `tipo=Despesa` ⇒ `'Despesa'`; `data_inicio` ⇒ `data_transacao >= `; `data_fim` ⇒ `data_transacao <=` (inclusivos; início maior que fim ⇒ lista vazia, sem erro). Sem `tipo`, a lista traz **os quatro tipos** (`Renda`, `Despesa`, `Transferência`, `Pagamento Fatura`). Qualquer outro parâmetro (o Angular manda `categoria`) é ignorado.

Ordem: `data_transacao DESC, created_at DESC` (e `id DESC` como desempate determinístico, sem mudança de contrato).

Sucesso: **200**
```json
{"status":"success","data":{"total":245,"limit":20,"offset":0,"transactions":[{"id":1234,"descricao":"Supermercado","valor":-150.5,"tipo":"Despesa","data_transacao":"2025-12-11","categoria":"Despesas Gerais","subcategoria":"Outros","conta":"Nubank","consolidada":true}]}}
```
- `total` = `COUNT(*)` com os mesmos filtros (inteiro); `limit` e `offset` ecoam os valores efetivos (depois do padrão).
- `valor` com sinal (despesa negativa); `tipo` é `tipo_transacao` do banco **exceto `Renda` que sai `Receita`** (desvio 001); `Transferência` e `Pagamento Fatura` saem como estão; `categoria` = `macrocategoria.nome_macro`; `subcategoria` = `subcategoria.nome_sub`; `conta` = `contas.nome_conta` (inclusive de conta inativa); `consolidada` booleano.
- Usuário sem transações: 200 com `total: 0` e `transactions: []`.
- Se P-004-3 for aprovado, cada item ganha no final: `conta_id`, `subcategoria_id`, `observacoes`, `data`, `conta_bancaria`, `conta_bancaria_id`.

Erro inesperado: **500** `"Erro ao carregar transações"`. Deslocamento acima de `int64` (o Flask dá 500 do banco): .NET responde lista vazia com o `total` real (diferença aceita, sem cliente que dependa).

### 3.2 `POST /api/transactions`

Origem: `api.py:1301-1459`. Entrada (JSON): `descricao`, `valor`, `tipo`, `data`, `subcategoria_id`, `conta_id` (obrigatórios); `observacoes`, `consolidada` (opcionais). Chaves desconhecidas (`usuario_id`, `origem`, `fatura_id`...) são ignoradas; o dono é sempre o `user_id` do token.

Corpo ausente, vazio, sem `Content-Type`, inválido ou que não seja objeto (desvio 024) é tratado como `{}`: cai na linha 1 (`"Campo 'descricao' é obrigatório"`), nunca 500.

Validações, **nesta ordem**:

| # | Condição | Resposta |
|---|---|---|
| 1 | `descricao` ausente ou vazia depois de `trim` | 400 `"Campo 'descricao' é obrigatório"` |
| 2 | `valor` ausente ou `null` (`0` e `false` **não** são ausentes aqui: o Flask testa `is None`) | 400 `"Campo 'valor' é obrigatório"` |
| 3 | `valor` não conversível por `float()` (texto não numérico, `"1,5"`, lista, objeto) | 400 `"Valor inválido"` |
| 4 | `valor` < 0 (depois de converter; `0` é aceito, `true` vale 1) | 400 `"Valor deve ser positivo"` |
| 5 | `tipo` diferente de `Receita`/`Despesa` (exato; ausente e `null` também) | 400 `"Tipo deve ser 'Receita' ou 'Despesa'"` |
| 6 | `data` ausente, `null`, `""`, `0` ou `false` | 400 `"Campo 'data' é obrigatório"` |
| 7 | `data` texto que não é `YYYY-MM-DD` válido (RN-004-06) | 400 `"Data deve estar no formato YYYY-MM-DD"` |
| 8 | `subcategoria_id` ausente ou falso (`null`, `0`, `""`, `false`) | 400 `"Campo 'subcategoria_id' é obrigatório"` |
| 9 | `conta_id` ausente ou falso | 400 `"Campo 'conta_id' é obrigatório"` |
| 10 | não existe conta com esse id, **do usuário e ativa** (desvio 039; o Flask não filtrava `ativa`) | 404 `"Conta não encontrada ou não pertence ao usuário"` |
| 11 | não existe subcategoria com esse id, global ou do usuário (`usuario_id IS NULL OR = token`) | 404 `"Subcategoria não encontrada"` |

Tratamento dos campos:
- `descricao`: `trim`; vem do texto, não é cortada (acima de 255 caracteres o Flask dá 500 do banco; ver P-004-2).
- `observacoes`: `trim`; vazio ⇒ NULL (`api.py:1352`). No Flask, `observacoes` `null` ou não texto dá 500 **antes** de qualquer validação (a linha é executada no topo); ver P-004-2.
- `consolidada`: ausente ⇒ `true`; booleano JSON é gravado como veio. Outros tipos: ver P-004-2.
- `valor_db` = `+valor` para `Receita`, `-valor` para `Despesa` (R1); `NUMERIC(15,2)` arredonda no banco (`10.005` ⇒ `10.01`).
- `tipo_transacao` gravado: `Renda` para `Receita` (desvio 001), `Despesa` para `Despesa`.
- `fatura_id`: só se a conta é `Cartão de Crédito` **e** `tipo = Despesa`, por `get_or_create_fatura` (RN-004-10 e 11); senão NULL. Receita em cartão não vincula.
- `origem = 'api'` (desvio 027); `usuario_id` = token; `agendamento_id`, `transferencia_par_id` NULL.

Efeito: numa única transação do banco, resolver a fatura (se for o caso) e inserir a linha (o Flask tentava isso e falha, §1).

Sucesso: **201**
```json
{"status":"success","message":"Transação criada com sucesso","data":{"id":1234,"descricao":"Supermercado","valor":-150.5,"tipo":"Despesa","data":"2025-12-11"}}
```
`valor` é o valor com sinal **como foi enviado, sem arredondar** (`-10.005` fica `-10.005` na resposta e `-10.01` no banco, como no Flask); `tipo` repete o tipo da entrada (`Receita`, não `Renda`); `data` repete o **texto recebido** (`"2025-1-5"` volta `"2025-1-5"`); `descricao` já sem as pontas. `data.id` é inteiro.

Erro inesperado: **500** `"Erro ao criar transação"`.

### 3.3 `PUT /api/transactions/<id>`

Origem: `api.py:1462-1635`. Atualização **parcial**: só entram as chaves presentes e válidas.

Validações e leitura de campos, **nesta ordem** (a ordem dos campos é a do código; nada é gravado se alguma falhar):

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido, não-objeto ou `{}` (desvio 024; o Flask testa `if not data`). **Vem antes do 404** | 400 `"Nenhum dado fornecido para atualização"` |
| 2 | não existe transação com esse `id` **do usuário** | 404 `"Transação não encontrada"` |
| 3 | `descricao` presente e **verdadeira** (texto não vazio): `trim` e atualiza. Falsa (`""`, `null`, `0`, `false`) é **ignorada** sem erro, e o tamanho não é validado | — |
| 4 | `valor` presente: não conversível por `float()` (inclusive `null`, lista, objeto) | 400 `"Valor inválido"` |
| 5 | `valor` convertido < 0 | 400 `"Valor deve ser positivo"` |
| 6 | `tipo` presente e diferente de `Receita`/`Despesa` (inclusive `null`) | 400 `"Tipo deve ser 'Receita' ou 'Despesa'"` |
| 7 | `data` presente e que não é `YYYY-MM-DD` válido (`null`/não texto dá 500 no Flask; ver P-004-2) | 400 `"Data deve estar no formato YYYY-MM-DD"` |
| 8 | `subcategoria_id` presente e sem subcategoria global/do usuário com esse id (inclusive `null`) | 404 `"Subcategoria não encontrada"` |
| 9 | `conta_id` presente e sem conta **do usuário e ativa** (desvio 039; inclusive `null`) | 404 `"Conta não encontrada ou não pertence ao usuário"` |
| 10 | `observacoes` presente: `trim`; vazio, `null` e falso ⇒ NULL (limpa); não texto verdadeiro dá 500 (P-004-2) | — |
| 11 | `consolidada` presente: `bool(valor)` do Python (`true`/`false`, `0`/`1`; o texto `"false"` vira `true`; `null` vira `false`) | — |
| 12 | nenhum campo entrou na atualização (`{"foo":1}`, `{"descricao":""}`) | 400 `"Nenhum campo válido para atualização"` |

Sinal do valor (RN-004-13), calculado nas linhas 4 e 6:
- `valor` presente: o tipo considerado é `tipo` do corpo se vier, senão o tipo **atual** da linha; `+valor` se for `Receita` (no banco, `Renda`; desvio 001), `-valor` em qualquer outro caso (inclusive `Transferência` e `Pagamento Fatura`, ver P-004-4).
- `tipo` presente e `valor` ausente: regrava `valor = ±abs(valor_atual)` conforme o novo tipo.
- `tipo` presente grava `tipo_transacao` (`Receita` ⇒ `Renda`).

O PUT **não** recalcula `fatura_id` quando conta, data ou tipo mudam (P-004-5) e não toca no par de uma transferência (P-004-4).

Efeito: `UPDATE transacoes SET <campos> , updated_at = CURRENT_TIMESTAMP WHERE id AND usuario_id RETURNING id, descricao, valor, tipo_transacao, data_transacao`, numa transação.

Sucesso: **200**
```json
{"status":"success","message":"Transação atualizada com sucesso","data":{"id":1234,"descricao":"Supermercado Extra","valor":-180.0,"tipo":"Despesa","data":"2025-12-12"}}
```
Aqui `valor` é o **valor gravado** (arredondado pelo banco, com sinal), `tipo` é o tipo gravado **mapeado** (`Renda` ⇒ `Receita`; `Transferência` e `Pagamento Fatura` como estão) e `data` é ISO.

Erro inesperado: **500** `"Erro ao atualizar transação"`.

### 3.4 `DELETE /api/transactions/<id>`

Origem: `api.py:1638-1695`. Sem corpo.

| # | Condição | Resposta |
|---|---|---|
| 1 | não existe transação com esse `id` do usuário | 404 `"Transação não encontrada"` |

Efeito, numa transação: se `transferencia_par_id` não é nulo, apaga antes a linha par (`WHERE id = par AND usuario_id = token`); depois apaga a linha. Vale para `Transferência` e para `Pagamento Fatura` (as duas pernas são pares). Não altera `faturas` (a fatura continua `Paga`; ver P-004-6) nem saldo de nada além do que a soma das transações já reflete.

Sucesso: **200** `{"status":"success","message":"Transação deletada com sucesso"}` (sem `data`).
Erro inesperado: **500** `"Erro ao deletar transação"`.

### 3.5 Entradas que hoje dão 500 (proposta P-004-2)

| Entrada | Flask | Proposta (só com P-004-2 aprovado) |
|---|---|---|
| POST `descricao` `null`/número/lista; `observacoes` não texto (inclusive `null`); `data` não texto; `consolidada` não booleano; `subcategoria_id`/`conta_id` texto não inteiro ou booleano | 500 (`AttributeError`, `TypeError`, erro do banco) | `descricao` não texto ⇒ linha 1; `observacoes` `null` ⇒ como ausente, não texto ⇒ 400 `"Campo 'observacoes' inválido"`; `data` não texto ⇒ linha 7; `consolidada` não booleano ⇒ 400 `"Campo 'consolidada' inválido"` (`null` ⇒ `true`); ids não inteiros ou fracionários ⇒ 404 das linhas 10 e 11 |
| POST/PUT `descricao` com mais de 255 caracteres | 500 (`varchar(255)`) | 400 `"Campo 'descricao' deve ter no máximo 255 caracteres"` |
| POST/PUT `valor` `"nan"`, `"inf"` ou ≥ 10^13 depois de arredondar | `nan` é gravado como `NaN` (e contamina o saldo, `NaN + x = NaN`); `inf`/estouro dão 500 | 400 `"Valor inválido"` |
| PUT `descricao`/`observacoes` não texto verdadeiro (`5`, lista) | 500 | 400 `"Campo 'descricao' inválido"` / `"Campo 'observacoes' inválido"` |
| PUT `data` `null` ou não texto | 500 | 400 `"Data deve estar no formato YYYY-MM-DD"` |
| PUT `consolidada` não booleano | converte com `bool()` (`"false"` ⇒ `true`) | 400 `"Campo 'consolidada' inválido"` |

## 4. Regras de negócio

- **RN-004-01** As 4 rotas exigem JWT válido; `user_id` vem só do token (`ICurrentUser`), nunca de corpo, query ou URL. *Origem: `api.py:34-90, 728-730, 1301-1303, 1462-1464, 1638-1640`.*
- **RN-004-02** Tenant: toda leitura e escrita filtra `usuario_id = <token>`; transação de outro usuário é indistinguível de inexistente (404 `"Transação não encontrada"`). Conta e subcategoria usadas num lançamento seguem a mesma regra (RN-004-08). *Origem: `api.py:816, 1388, 1397, 1512, 1567, 1579, 1664, 1674, 1677`.*
- **RN-004-03** (R1) Despesa é gravada **negativa**, receita **positiva**; a API recebe `valor ≥ 0` e devolve com sinal (POST: o valor enviado com sinal; PUT e GET: o gravado). `valor = 0` é aceito (resposta `-0.0` para despesa, numericamente igual). *Origem: `api.py:1410, 1535, 1551`; `business-rules.md` R1.*
- **RN-004-04** (R2, desvio 001) O contrato HTTP usa `Receita`; o banco usa `Renda`. Entrada (`tipo` do POST/PUT, filtro `?tipo=`) converte `Receita` ⇒ `Renda`; saída (GET `tipo`, PUT `data.tipo`) converte `Renda` ⇒ `Receita`; o POST repete o texto da entrada. `Transferência` e `Pagamento Fatura` nunca entram por estas rotas (só pelo serviço de lançamento) e saem sem conversão. *Origem: `api.py:796, 824, 1369, 1429, 1535, 1543`; desvio 001.*
- **RN-004-05** (paginação) `limit` 1..100 (padrão 20), `offset ≥ 0` (padrão 0); texto não inteiro ⇒ padrão; validação na ordem da §3.1. *Origem: `api.py:774-788`.*
- **RN-004-06** (data) Formato aceito = `datetime.strptime(x, '%Y-%m-%d')`: exatamente 4 dígitos de ano, 1 ou 2 dígitos de mês e de dia (`2026-1-5` vale), data de calendário válida (`2026-02-30` não). Espaços, `T10:00`, `05/01/2026` e ano de 2 dígitos são inválidos. Datas futuras são aceitas. *Origem: `api.py:805-807, 1376`.*
- **RN-004-07** (GET) Lista os quatro tipos de transação; junta `subcategoria`, `macrocategoria` e `contas` (todas com FK `NOT NULL`, portanto nenhuma linha some); a conta aparece mesmo se inativa. *Origem: `api.py:847-865`.*
- **RN-004-08** (conta e subcategoria do lançamento) A conta precisa ser do usuário **e ativa** (desvio 039, vale também para a conta nova no PUT); a subcategoria precisa ser global (`usuario_id IS NULL`) ou do usuário (o Flask não olha a macro). Conta é verificada antes da subcategoria. *Origem: `api.py:1388-1402, 1565-1583`; desvio 039.*
- **RN-004-09** (fatura no lançamento, R4) Só a **despesa em conta `Cartão de Crédito`** é vinculada a uma fatura; receita, conta que não é cartão e cartão sem `dia_fechamento` ou `dia_vencimento` ficam com `fatura_id` NULL. A fatura **é criada se ainda não existir**, na mesma transação do banco que insere a transação. *Origem: `api.py:1405-1407`; `invoice_service.py:20-24`.*
- **RN-004-10** (ciclo da fatura) Dados `F` = `dia_fechamento`, `V` = `dia_vencimento` e a data `D` da compra, com "dia inexistente no mês cai no último dia" (`31` em fevereiro ⇒ 28/29, também para o fechamento usado na comparação):
  - `D ≤ fechamento do mês de D`: fecha **no mês de D**; vence no mês de D, ou no **seguinte** se `V < F`.
  - `D >` esse fechamento: fecha no **mês seguinte**; vence **no segundo mês seguinte a D** (`V` do mês `m+2`), em qualquer caso. *(É a fórmula do código; para `V ≥ F` ela atrasa o vencimento em um mês: ver P-004-1.)*
  Virada de ano resolvida (`12` ⇒ `1` do ano seguinte). *Origem: `invoice_service.py:27-62`; `business-rules.md` R4.*
- **RN-004-11** (localizar ou criar) A fatura é buscada por `(conta_id, data_vencimento)`; se não existe, `INSERT ... ON CONFLICT (conta_id, data_vencimento) DO NOTHING RETURNING id`, e se o `ON CONFLICT` não devolve linha (corrida) busca de novo. Fatura nova nasce `Aberta`. A busca **não** filtra `status`: a compra pode ser vinculada a fatura `Fechada` ou `Paga` do ciclo. *Origem: `invoice_service.py:64-77`.*
- **RN-004-12** (PUT parcial) Só as chaves presentes e válidas entram; `updated_at` é atualizado; a ordem de validação é a da §3.3. *Origem: `api.py:1519-1611`.*
- **RN-004-13** (PUT, sinal) Ver §3.3: o sinal depende de `tipo` do corpo ou, na falta, do tipo atual; só `Receita`/`Renda` é positivo. *Origem: `api.py:1527-1553`.*
- **RN-004-14** (PUT não relinka) `fatura_id` e o par de transferência não mudam no PUT. *Origem: `api.py:1519-1611` (nenhum `fatura_id` nem `transferencia_par_id`); P-004-5.*
- **RN-004-15** (DELETE, par) Apaga o par e depois a linha, na mesma transação, ambos com `usuario_id`. *Origem: `api.py:1671-1678`; `business-rules.md` R3.*
- **RN-004-16** 500 devolve sempre a mensagem fixa da rota; a exceção vai ao log só como tipo (e `SqlState`), sem `str(e)`, descrição, valor nem corpo (desvio 008). EventIds 4001–4004.
- **RN-004-17** Corpo lido sem exigir `Content-Type` (desvio 024); ver §3.2 e §3.3 para o efeito em cada rota.
- **RN-004-18** `id` da URL: só dígitos casam a rota; número que não cabe em `int` é tratado como transação inexistente (404 JSON `"Transação não encontrada"`), como na 002 (RN-002-18).
- **RN-004-19** (colunas novas, desvio 027) `transacoes.observacoes text NULL` e `transacoes.origem varchar(20) NULL` são criadas pela migration `0050`; transações antigas ficam com NULL. O POST grava `origem = 'api'`.
- **RN-004-20** (atomicidade) POST (fatura + inserção), PUT (checagens + atualização) e DELETE (par + linha) rodam em **uma transação do banco** cada; o Flask pretendia isso com `conn.begin()`.
- **RN-004-21** (valor) `NUMERIC(15,2)`; a conversão para o banco usa a representação mais curta do `double`, como o driver do Python (`PyNumber`, 002 RN-002-09).
- **RN-004-22** (ordem) O GET desempata por `id DESC` (implementação, sem mudança de contrato).

Regras do serviço de lançamento (§7): RN-004-30 a RN-004-39.

## 5. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","004-NN")]`); os de integração comparam o corpo como texto exato.

**Autenticação, roteamento e erros (comuns às 4 rotas)**
- **AC-004-80** Dado nenhum `Authorization`, `Token abc`, token expirado/adulterado ou sem `user_id`, quando qualquer rota, então as 4 mensagens 401 do `RequireUser`, antes de qualquer validação.
- **AC-004-81** Dado `/api/transactions/` (barra final), `/api/transactions/abc`, `/api/transactions/-1`, `/api/transactions/1.5` (PUT ou DELETE), então 404 com o HTML do Flask, **mesmo sem token**.
- **AC-004-82** Dado `GET /api/transactions/1`, `POST /api/transactions/1`, `DELETE /api/transactions`, `PUT /api/transactions` ou qualquer método em `/api/transactions/recent` que não seja GET, então **405**.
- **AC-004-83** Dado que o repositório lança exceção (inclusive `PostgresException` com texto interno), quando cada rota, então 500 com a mensagem fixa da rota e o log só com o tipo (desvio 008).
- **AC-004-84** Dado `DELETE /api/transactions/99999999999999999999`, então 404 JSON `"Transação não encontrada"` (RN-004-18).
- **AC-004-85** Dados dois usuários A e B, então nenhuma das 4 rotas lê ou altera transação, conta ou subcategoria própria do outro (isolamento por rota; ver AC 08, 28, 55, 76).

**GET**
- **AC-004-01** Dado usuário com transações, então 200 com `data` na ordem `total, limit, offset, transactions` e cada item com as chaves `id, descricao, valor, tipo, data_transacao, categoria, subcategoria, conta, consolidada`, nessa ordem; `valor` com sinal e centavos preservados (`-70.55`).
- **AC-004-02** Dado nenhum parâmetro, então `limit: 20` e `offset: 0` no corpo; dado `limit=100`, aceito; `limit=0`, `101`, `-1`, `99999999999999999999` ⇒ 400 `"Parâmetro 'limit' deve estar entre 1 e 100"`; `limit=abc`, `limit=` e `limit=1.5` ⇒ 20 (padrão); `limit=" 5"` ⇒ 5.
- **AC-004-03** Dado `offset=-1`, então 400 `"Parâmetro 'offset' deve ser maior ou igual a 0"`; `offset=abc` ⇒ 0; com `limit` e `offset` inválidos, vale o erro do `limit`.
- **AC-004-04** Dado `tipo=Receita`, então só as transações gravadas como `Renda` (inclusive as do bot) saem, com `tipo: "Receita"`; `tipo=Despesa` só `Despesa`; sem `tipo`, os quatro tipos (`Renda` sai `Receita`); `tipo=Renda`, `receita`, `Transferência` ⇒ 400 `"Parâmetro 'tipo' deve ser 'Receita' ou 'Despesa'"`; `tipo=` ⇒ sem filtro.
- **AC-004-05** Dado `data_inicio=2025-12-01&data_fim=2025-12-31`, então só as de dezembro (limites inclusivos); só `data_inicio` ou só `data_fim` funcionam; início > fim ⇒ lista vazia e `total: 0`.
- **AC-004-06** Dado `data_inicio=2025-13-01`, `2025-02-30`, `01/12/2025`, `2025-12-01T10:00` ou `abc` (ou `data_fim` assim), então 400 `"Datas devem estar no formato YYYY-MM-DD"`; `2025-1-5` é aceito; `data_inicio=` ⇒ sem filtro. A ordem dos erros é limit, offset, tipo, datas.
- **AC-004-07** Dadas 5 transações e `limit=2&offset=2`, então 2 itens (a 3ª e a 4ª na ordem `data_transacao DESC, created_at DESC, id DESC`) e `total: 5`; `offset` além do fim ⇒ `transactions: []` com `total` real.
- **AC-004-08** Dadas transações de outro usuário, então não aparecem e não entram em `total`.
- **AC-004-09** Dado usuário sem transações, então 200 `{"status":"success","data":{"total":0,"limit":20,"offset":0,"transactions":[]}}`. Dado parâmetro `categoria=x`, então ignorado.
- **AC-004-10a** Dada transação em conta inativa, então ela aparece com o nome da conta.

**POST**
- **AC-004-10** Dado `{"descricao":"Mercado","valor":150.5,"tipo":"Despesa","data":"2025-12-11","subcategoria_id":<global>,"conta_id":<corrente>}`, então 201 com o corpo exato da §3.2 (`valor` `-150.5`, `tipo` `"Despesa"`, `data` `"2025-12-11"`); no banco `valor = -150.50`, `tipo_transacao = 'Despesa'`, `origem = 'api'`, `consolidada = true`, `fatura_id` NULL, `usuario_id` do token, `observacoes` NULL.
- **AC-004-11** Dado `tipo: "Receita"` e `valor` 3000, então 201 com `valor: 3000.0` e `tipo: "Receita"`, e no banco `valor = 3000.00` e `tipo_transacao = 'Renda'` (desvio 001); a transação aparece em `GET ?tipo=Receita`.
- **AC-004-12** Dado corpo ausente, sem `Content-Type`, JSON inválido, `null`, `[]`, `"abc"` ou `{}`, então 400 `"Campo 'descricao' é obrigatório"` (desvio 024), nunca 500.
- **AC-004-13** Dada a ordem de validações da §3.2 (linhas 1 a 11), com dois erros no mesmo corpo responde o primeiro: sem `descricao` e sem `valor` ⇒ `descricao`; sem `valor` e `tipo` inválido ⇒ `valor`; `valor` `"abc"` e `tipo` inválido ⇒ `"Valor inválido"`; `valor` `-1` e `tipo` inválido ⇒ `"Valor deve ser positivo"`; `data` ausente e `conta_id` ausente ⇒ `data`; `subcategoria_id` ausente e `conta_id` ausente ⇒ `subcategoria_id`.
- **AC-004-14** Dado `descricao` `"  "`, ausente ou `""`, então 400 de `descricao`; `"  Mercado  "` ⇒ gravado `Mercado` e resposta `descricao: "Mercado"`.
- **AC-004-15** Dado `valor` `null` ou ausente ⇒ 400 `"Campo 'valor' é obrigatório"`; `0` ⇒ 201 (despesa gravada `0.00`, resposta `-0.0`); `"12.5"`, `" 7 "`, `true` aceitos (`12.5`, `7`, `1`); `"1,5"`, `"abc"`, `[]`, `{}` ⇒ 400 `"Valor inválido"`; `-0.01` e `"-5"` ⇒ 400 `"Valor deve ser positivo"`.
- **AC-004-16** Dado `valor` `10.005`, então no banco `10.01` (arredondamento do `numeric`, como no Flask) e na resposta `valor: -10.005`.
- **AC-004-17** Dado `tipo` `"receita"`, `"Renda"`, `"Transferência"`, `null` ou ausente, então 400 `"Tipo deve ser 'Receita' ou 'Despesa'"`.
- **AC-004-18** Dado `data` ausente, `null`, `""`, `0`, `false` ⇒ 400 `"Campo 'data' é obrigatório"`; `"2025-13-01"`, `"2025-02-30"`, `"11/12/2025"`, `"2025-12-11T10:00"` ⇒ 400 `"Data deve estar no formato YYYY-MM-DD"`; `"2025-1-5"` ⇒ 201 gravado `2025-01-05` e **resposta `data: "2025-1-5"`**.
- **AC-004-19** Dado `subcategoria_id` ausente, `null`, `0`, `""`, `false` ⇒ 400 de `subcategoria_id`; `conta_id` idem ⇒ 400 de `conta_id`.
- **AC-004-20** Dado `conta_id` de conta de outro usuário, inexistente ou **inativa** (desvio 039), então 404 `"Conta não encontrada ou não pertence ao usuário"`, nada gravado; dado `subcategoria_id` inexistente ou **subcategoria própria de outro usuário**, então 404 `"Subcategoria não encontrada"`; subcategoria global e a própria são aceitas; com conta e subcategoria inválidas responde a conta.
- **AC-004-21** Dado `observacoes` `"  Compras  "`, então gravado `Compras`; `""`, `"   "` ou ausente ⇒ NULL.
- **AC-004-22** Dado `consolidada: false`, então gravado `false`; ausente ⇒ `true`.
- **AC-004-23** Dado `Despesa` em conta `Cartão de Crédito` com `dia_fechamento` e `dia_vencimento`, então `fatura_id` preenchido com a fatura do ciclo (RN-004-10) e, se ela não existia, ela é criada (`Aberta`, `data_fechamento` e `data_vencimento` da RN-004-10) na mesma transação; uma segunda compra no mesmo ciclo reutiliza a fatura (uma linha em `faturas`).
- **AC-004-24** Dado `Receita` em conta cartão, `Despesa` em conta `Conta Corrente` ou `Dinheiro`, ou `Despesa` em cartão sem dias, então `fatura_id` NULL e nenhuma fatura criada.
- **AC-004-25** Dado erro no INSERT depois de a fatura ter sido criada (conta com CHECK quebrado simulado, ou subcategoria removida entre a checagem e o insert), então a fatura **não** fica no banco (mesma transação, RN-004-20) e a resposta é 500 `"Erro ao criar transação"`.
- **AC-004-26** Dadas duas compras simultâneas no mesmo cartão e ciclo que ainda não tem fatura, então as duas usam a **mesma** fatura e existe só uma linha em `faturas` (`ON CONFLICT`).
- **AC-004-27** Dado corpo com `usuario_id` de outro usuário, `origem: "x"`, `fatura_id: 9` ou `id: 1`, então ignorados (a transação é do token, `origem = 'api'`).
- **AC-004-28** Dado o mesmo `conta_id` com outro usuário logado, então 404 (isolamento); dado `consolidada: true` e `false` alternados, o saldo da conta (002) só soma a consolidada.
- **AC-004-29** Dado o corpo que o Angular envia (`descricao`, `valor`, `tipo`, `data`, `subcategoria_id`, `conta_id`, `consolidada: true`, e `observacoes` omitido quando vazio), então 201.

**PUT**
- **AC-004-40** Dado corpo ausente, `{}`, `null`, `[]`, `0`, JSON inválido, então 400 `"Nenhum dado fornecido para atualização"`, **inclusive quando a transação não existe** (400 antes do 404).
- **AC-004-41** Dado `id` inexistente ou de outro usuário, então 404 `"Transação não encontrada"` com a mesma resposta, e a transação do outro permanece intacta.
- **AC-004-42** Dado `{"descricao":"Novo"}`, então 200 com o corpo da §3.3 (`valor` e `tipo` atuais, `data` ISO), só a descrição muda e `updated_at` avança; `descricao` `""`, `null`, `0` é ignorada.
- **AC-004-43** Dado `{"valor":180}` numa `Despesa`, então gravado `-180.00` e resposta `valor: -180.0`; numa `Receita`, `+180`; numa transação `Renda` criada pelo bot, `+180` (desvio 001: o Flask gravaria `-180`).
- **AC-004-44** Dado `{"valor":10,"tipo":"Receita"}` numa despesa, então `+10` e `tipo` `"Receita"` (gravado `Renda`); `{"tipo":"Despesa"}` numa receita de 50 ⇒ `-50`; `{"tipo":"Receita"}` numa despesa de -50 ⇒ `+50`; o `tipo` do corpo vale mesmo se vier depois no JSON.
- **AC-004-45** Dado `valor` `"abc"`, `null`, `[]` ⇒ 400 `"Valor inválido"`; `-5` ⇒ 400 `"Valor deve ser positivo"`; `0` ⇒ aceito.
- **AC-004-46** Dado `tipo` `"X"`, `null`, `"Renda"` ⇒ 400 `"Tipo deve ser 'Receita' ou 'Despesa'"`; o erro de `valor` vem antes do de `tipo` quando os dois existem.
- **AC-004-47** Dado `data` `"2025-12-12"` ⇒ atualiza; `"12/12/2025"` ⇒ 400 `"Data deve estar no formato YYYY-MM-DD"`.
- **AC-004-48** Dado `subcategoria_id` válida ⇒ atualiza; inexistente, `null` ou de outro usuário ⇒ 404 `"Subcategoria não encontrada"`.
- **AC-004-49** Dado `conta_id` de conta própria ativa ⇒ atualiza; de outro usuário, inexistente, inativa (desvio 039) ou `null` ⇒ 404 `"Conta não encontrada ou não pertence ao usuário"`; a subcategoria é checada antes da conta.
- **AC-004-50** Dado `observacoes` `"  nota  "` ⇒ `nota`; `""` ou `null` ⇒ NULL.
- **AC-004-51** Dado `consolidada: false` ⇒ `false`; `true` ⇒ `true`.
- **AC-004-52** Dado `{"foo":1}`, `{"descricao":""}` ou `{"descricao":null}` ⇒ 400 `"Nenhum campo válido para atualização"`.
- **AC-004-53** Dado corpo com dois erros (`valor` inválido e `conta_id` de outro usuário), então responde o primeiro da ordem da §3.3 (`valor`), e nenhum campo é gravado quando qualquer validação falha.
- **AC-004-54** Dado o corpo que o Angular envia na edição (`descricao`, `valor`, `tipo`, `data`, `subcategoria_id`, `conta_id`, `consolidada: true`, `observacoes` só se preenchida), então 200 e `consolidada` volta a `true`.
- **AC-004-55** Dado token do usuário B e `id` de transação de A, então 404 e nada muda; dado `conta_id` de A no PUT de B, 404.
- **AC-004-56** Dado PUT numa transação de cartão que muda `data` ou `conta_id`, então `fatura_id` **continua o mesmo** (RN-004-14; muda só se P-004-5 for aprovado).

**DELETE**
- **AC-004-70** Dada transação simples, então 200 `{"status":"success","message":"Transação deletada com sucesso"}` e a linha deixa de existir.
- **AC-004-71** Dado `id` inexistente ou de outro usuário, então 404 `"Transação não encontrada"` e a do outro permanece.
- **AC-004-72** Dado o segundo DELETE da mesma transação, então 404.
- **AC-004-73** Dada uma transferência (duas linhas com `transferencia_par_id` cruzado), quando DELETE de qualquer das duas, então as **duas** somem; o mesmo vale para as duas pernas de um `Pagamento Fatura`.
- **AC-004-74** Dada transação com `transferencia_par_id` apontando para uma linha de **outro usuário** (dado anômalo), então só a própria é apagada (o filtro `usuario_id` do par).
- **AC-004-75** Dado erro ao apagar a linha depois de apagar o par (simulado), então o par **não** fica apagado (mesma transação).
- **AC-004-76** Dado token do usuário B e `id` de A, então 404 e a transação de A permanece.
- **AC-004-77** Dada despesa de cartão com `fatura_id`, quando DELETE, então a fatura continua existindo e com o mesmo `status`.

**Ciclo de fatura (R4)** — tabela de casos do Domain (`InvoiceCycle.ForPurchase`), todos com fechamento `F`, vencimento `V` e data `D`. Fórmula do código; o que muda com P-004-1 está na última coluna.

| F | V | D | Fechamento | Vencimento (Flask) | Vencimento (com P-004-1) |
|---|---|---|---|---|---|
| 10 | 15 | 2026-03-10 | 2026-03-10 | 2026-03-15 | igual |
| 10 | 15 | 2026-03-11 | 2026-04-10 | 2026-05-15 | **2026-04-15** |
| 10 | 15 | 2026-12-20 | 2027-01-10 | 2027-02-15 | **2027-01-15** |
| 25 | 5 | 2026-03-20 | 2026-03-25 | 2026-04-05 | igual |
| 25 | 5 | 2026-03-26 | 2026-04-25 | 2026-05-05 | igual |
| 10 | 5 | 2026-12-05 | 2026-12-10 | 2027-01-05 | igual |
| 31 | 10 | 2026-02-28 | 2026-02-28 | 2026-03-10 | igual |
| 31 | 10 | 2026-03-31 | 2026-03-31 | 2026-04-10 | igual |
| 30 | 10 | 2028-02-10 | 2028-02-29 | 2028-03-10 | igual |
| 5 | 31 | 2026-02-03 | 2026-02-05 | 2026-02-28 | igual |
| 5 | 31 | 2026-02-06 | 2026-03-05 | 2026-04-30 | **2026-03-31** |
| 10 | 10 | 2026-03-11 | 2026-04-10 | 2026-05-10 | **2026-04-10** |

- **AC-004-90** Dada cada linha da tabela, então `InvoiceCycle.ForPurchase(F, V, D)` devolve fechamento e vencimento (coluna "Flask" enquanto P-004-1 não for aprovado; coluna "com P-004-1" depois).
- **AC-004-91** Dado `D` igual ao dia de fechamento, então a compra entra na fatura que **fecha nesse dia** (`D ≤ F`); dia seguinte ⇒ próxima fatura.
- **AC-004-92** Dado `V < F` (vencimento no mês seguinte ao fechamento) e `D ≤ F`, então vencimento no mês seguinte (linhas `25/5` e `10/5`).
- **AC-004-93** Dado virada de ano (dezembro) e fevereiro bissexto, então datas corretas (linhas `10/15` dez, `30/10` 2028).
- **AC-004-94** Dado cartão com `F` ou `V` nulo ou zero, então `ForPurchase` não é chamada e `fatura_id` fica NULL (RN-004-09).
- **AC-004-95** Dado `IInvoiceService.EnsureCurrentAsync` (§7.6) para um cartão sem fatura do ciclo de hoje, então cria a fatura `Aberta` do ciclo de hoje e chama `IInvoiceSchedulePopulator` **depois do commit**; se já existe, não cria nem popula; erro do populador é engolido e logado (como o `try/except` do Flask).

## 6. Tabelas e dados tocados

| Tabela | Operação | Observação |
|---|---|---|
| `transacoes` | SELECT, INSERT, UPDATE, DELETE | única tabela de dados; sem soft delete; índices existentes: `usuario_id`, `fatura_id`, `agendamento_id`, `transferencia_par_id` (não há `(usuario_id, data_transacao)`; irrelevante com 330 linhas) |
| `contas` | SELECT | posse, tipo, nome e dias; filtro `ativa = true` nas escritas (desvio 039) |
| `subcategoria` (`macrocategoria`, `grupocategoria`) | SELECT | existência (escritas) e nomes (GET); lookups de transferência e pagamento via `ICategoryLookup` |
| `faturas` | SELECT, INSERT, UPDATE | criada no lançamento; `status` só muda no pagamento de fatura (§7) |
| `agendamentos` | INSERT | só pela compra parcelada (§7.4) |

### 6.1 Migration `0050_transacoes_observacoes_origem.sql` (desvio 027; ADR 0001)
```sql
ALTER TABLE transacoes ADD COLUMN IF NOT EXISTS observacoes text NULL;
ALTER TABLE transacoes ADD COLUMN IF NOT EXISTS origem varchar(20) NULL;
```
Só aditiva e idempotente; sem `CHECK` em `origem` (o `CHECK` do ORM morto aceitava `manual`, `automate`, `whatsapp`, `agendamento`, `api`, mas o código usa também `api_endpoint`; deixar livre evita novo 500). Linhas antigas ficam NULL. Nota para o `cutover-runbook.md`: aplicar antes da imagem nova (o runner da ADR 0001 já faz). Números `0051-0059` reservados.

### 6.2 Constraints que a API encontra (`0000_baseline.sql:307-324, 375-380`)
- `transacoes_tipo_transacao_check`: `Renda`, `Despesa`, `Transferência`, `Pagamento Fatura` (por isso `Receita` é traduzida, desvio 001).
- FKs: `usuario_id` CASCADE; `conta_id` **RESTRICT**; `subcategoria_id` RESTRICT; `fatura_id` SET NULL; `transferencia_par_id` SET NULL (auto-FK); `agendamento_id` SET NULL.
- `descricao varchar(255)`, `valor numeric(15,2)`, `data_transacao date`, `consolidada boolean NOT NULL`. Trigger `trigger_update_transacoes_updated_at`.
- `faturas`: `UNIQUE(conta_id, data_vencimento)`; `status` `Aberta|Fechada|Paga`.

### 6.3 Tenant (`usuario_id` do JWT)
`transacoes` tem `usuario_id` e todas as 4 rotas filtram por ele. `contas` e `subcategoria` são checadas por posse (`usuario_id`; subcategoria global `IS NULL` é de todos). **`faturas` não tem `usuario_id`**: o tenant vem de `faturas.conta_id → contas.usuario_id`; o serviço só abre/cria fatura de conta já validada como do usuário. Pontos do Flask **sem** filtro de usuário avaliados:

| Onde | Consulta | Risco |
|---|---|---|
| `invoice_service.py:64-65` | `SELECT id FROM Faturas WHERE conta_id AND data_vencimento` | nenhum (a conta foi checada antes: `:20-21` filtra `usuario_id`) |
| `transaction_service.py:111-113, 208-210` | `SELECT nome_conta FROM Contas WHERE id` sem `usuario_id` | nenhum hoje (o chamador resolve contas do usuário); o .NET valida posse e `ativa` antes (§7) |
| `api.py:1565-1569` | subcategoria só confere `usuario_id IS NULL OR = uid` | macro de outro usuário não é checada; mantido (Q-004-9) |

## 7. Serviço de lançamento de transações (porta para 011 e 012)

O Flask repete a regra de lançar transação em ~10 lugares (`api.py`, `transaction_handler.py`, `whatsapp_handler.py`, intents). O .NET tem **uma** porta, `ITransactionPosting`, em `MeuSecretario.Application/Transactions/`. A rota `POST /api/transactions` usa a mesma porta (`PostSimpleAsync`, origem `api`). 011 e 012 **não** escrevem SQL de `transacoes`, `faturas` nem de agendamento de parcelas.

### 7.1 Contrato (assinaturas exatas)

```csharp
namespace MeuSecretario.Application.Transactions;

public interface ITransactionPosting
{
    /// Receita ou despesa simples (create_transaction + vínculo de fatura). Uma transação do banco.
    Task<PostingResult<PostedTransaction>> PostSimpleAsync(PostSimpleTransaction command, CancellationToken cancellationToken);

    /// Par Transferência (create_transfer_pair).
    Task<PostingResult<PostedTransfer>> PostTransferAsync(PostTransfer command, CancellationToken cancellationToken);

    /// Par Pagamento Fatura + fatura Paga (create_fatura_payment, desvio 010).
    Task<PostingResult<PostedInvoicePayment>> PostInvoicePaymentAsync(PostInvoicePayment command, CancellationToken cancellationToken);

    /// 1ª parcela como transação + agendamento PARCELADO das demais (create_transaction + create_parcelamento_agendamento).
    Task<PostingResult<PostedInstallmentPurchase>> PostInstallmentPurchaseAsync(PostInstallmentPurchase command, CancellationToken cancellationToken);
}

public enum TransactionKind { Income, Expense }   // Income grava 'Renda'; Expense grava 'Despesa'

public sealed record PostSimpleTransaction(
    int UserId, int AccountId, int SubcategoryId, TransactionKind Kind,
    decimal Amount,                 // positivo ou zero, SEM sinal; o serviço aplica o sinal (R1)
    string Description, DateOnly Date,
    string? Notes = null, bool Consolidated = true,
    string? Origin = null,          // até 20 caracteres; null = NULL no banco
    int? ScheduleId = null);        // agendamento_id (motor de agendamentos, 013)

public sealed record PostTransfer(int UserId, int FromAccountId, int ToAccountId, decimal Amount, DateOnly Date, string? Origin = null);

public sealed record PostInvoicePayment(int UserId, int FromAccountId, int CardAccountId, decimal Amount, DateOnly Date, string? Origin = null);

public sealed record PostInstallmentPurchase(
    int UserId, int AccountId, int SubcategoryId,
    decimal InstallmentAmount,      // valor de CADA parcela, positivo; quem chama divide o total (o Flask faz valor_total / n sem arredondar)
    int TotalInstallments,          // >= 2
    string Description, DateOnly Date, string? Origin = null);

public sealed record PostedTransaction(int TransactionId, decimal StoredAmount, int? InvoiceId, DateOnly? InvoiceDueDate);
public sealed record PostedTransfer(int OutgoingTransactionId, int IncomingTransactionId, string FromAccountName, string ToAccountName);
public sealed record PostedInvoicePayment(int OutgoingTransactionId, int IncomingTransactionId, string FromAccountName, string CardName, int? InvoiceId, DateOnly? InvoiceDueDate, bool InvoiceMarkedPaid);
public sealed record PostedInstallmentPurchase(int TransactionId, int ScheduleId, int? InvoiceId);

public abstract record PostingResult<T>
{
    public sealed record Ok(T Value) : PostingResult<T>;
    public sealed record Failed(PostingError Error, string? Detail = null) : PostingResult<T>;
}

public enum PostingError
{
    AmountInvalid,                 // Amount < 0 (nunca nulo)
    AccountNotFound,               // conta (origem, no caso de transferência/pagamento) inexistente, de outro usuário ou inativa (desvio 039)
    DestinationAccountNotFound,    // conta destino / cartão inexistente, de outro usuário ou inativa
    SubcategoryNotFound,           // só PostSimple e PostInstallmentPurchase
    SameAccount,                   // origem == destino (transferência) ou origem == cartão (pagamento)
    NotACreditCard,                // PostInvoicePayment: a conta do cartão não é 'Cartão de Crédito' (P-004-7)
    InstallmentsInvalid,           // TotalInstallments < 2
    RequiredCategoryMissing,       // 'Investimentos de Curto Prazo' / 'Quitação de Empréstimos (Principal)' ausentes; Detail = nome (só para log)
}

public interface IInvoiceService
{
    /// ensure_current_invoice_exists, só a criação (§7.6). cardAccountId nulo = todos os cartões ativos do usuário.
    Task EnsureCurrentAsync(int userId, int? cardAccountId, CancellationToken cancellationToken);
}

/// Gancho de 013/011: lança na fatura nova os agendamentos ativos do cartão (process_invoice_schedules).
public interface IInvoiceSchedulePopulator
{
    Task PopulateAsync(int invoiceId, int cardAccountId, DateOnly dueDate, CancellationToken cancellationToken);
}
```

### 7.2 `PostSimpleAsync`
Ordem: `Amount < 0` ⇒ `AmountInvalid`; conta (do usuário e ativa) ⇒ `AccountNotFound`; subcategoria global/do usuário ⇒ `SubcategoryNotFound`; fatura (RN-004-09, só `Expense` em `Cartão de Crédito` com os dois dias); `INSERT` com valor `+Amount` (`Income`) ou `-Amount` (`Expense`), tipo `Renda`/`Despesa`, `origem = Origin`, `agendamento_id = ScheduleId`, `observacoes = Notes`, `consolidada`. Tudo numa transação. `StoredAmount` é o `valor` gravado (arredondado). Não verifica pote, saldo nem duplicidade: isso é do chamador (012, desvio 015). Quem chama para `Expense` em cartão **não** precisa criar a fatura antes; se quiser a regra "garantir fatura do ciclo atual" do bot, chama `IInvoiceService.EnsureCurrentAsync` antes (§7.6).

### 7.3 `PostTransferAsync` e `PostInvoicePaymentAsync`
- **Transferência** (R3, `transaction_service.py:63-151`): `FromAccountId == ToAccountId` ⇒ `SameAccount`; origem inexistente/inativa/alheia ⇒ `AccountNotFound`; destino ⇒ `DestinationAccountNotFound`; subcategoria `Investimentos de Curto Prazo` do grupo `Meta Financeira` (`ICategoryLookup.FindShortTermInvestmentAsync`, desvio 030) ausente ⇒ `RequiredCategoryMissing`. Grava duas linhas `Transferência`, `consolidada = true`, sem fatura: saída na origem com `-Amount` e descrição `"Transferência para {nome_destino}"`; entrada no destino com `+Amount` e `"Transferência de {nome_origem}"`; depois liga os dois `transferencia_par_id` em ambos os sentidos. O serviço **não verifica saldo** (a 012 faz a checagem de saldo suficiente com o saldo consolidado da 002).
- **Pagamento de fatura** (R5, `transaction_service.py:154-254`, desvio 010): `FromAccountId == CardAccountId` ⇒ `SameAccount`; origem ⇒ `AccountNotFound`; cartão inexistente/inativo/alheio ⇒ `DestinationAccountNotFound`; cartão que não é `Cartão de Crédito` ⇒ `NotACreditCard` (P-004-7; o Flask aceitaria e não marcaria fatura); `Quitação de Empréstimos (Principal)` ausente ⇒ `RequiredCategoryMissing`. **Fatura paga** (desvio 010): a `Fechada` de menor `data_vencimento` do cartão (`status = 'Fechada'`, desempate por `id`); se não houver, a fatura do ciclo de `Date` (RN-004-10/11, criada se faltar); se o cartão não tem os dois dias, `InvoiceId` é nulo e nada é marcado. Grava duas linhas `Pagamento Fatura`, ambas com o `fatura_id`: saída na origem (`-Amount`, `"Pagamento Fatura {nome_cartao}"`) e entrada no cartão (`+Amount`, `"Pagamento Recebido (de {nome_origem})"`), pares ligados; marca a fatura `Paga` **sem comparar o valor pago** (R5). `InvoiceMarkedPaid` = houve fatura marcada.

### 7.4 `PostInstallmentPurchaseAsync` (R6)
`TotalInstallments < 2` ⇒ `InstallmentsInvalid`; (o limite de 48 do bot é validação da 012). Numa transação: (1) `PostSimple` equivalente de uma `Expense` de `InstallmentAmount`, com a data da compra e o vínculo de fatura da RN-004-09; descrição **como veio** (sem `"(1/n)"`); (2) `INSERT INTO agendamentos` de tipo `PARCELADO`, periodicidade `MENSAL`, `valor_previsto = InstallmentAmount`, `descricao` igual, `data_inicio = Date + 1 mês` (dia inexistente cai no último, como `relativedelta`), `dia_execucao = Date.Day`, `total_parcelas = TotalInstallments`, `parcelas_executadas = 1`, `ativo = true`, `incluir_na_reserva` e `notificar_antes_dias` pelos defaults do banco (`true`, `3`), `mes_execucao` NULL. A 1ª transação **não** recebe `agendamento_id` (paridade; Q-004-4). Quem executa as parcelas seguintes (incremento, `>=`, desativação, idempotência por `agendamento_id` + mês, desvio 011) é o motor de agendamentos (013).

### 7.5 Regras do serviço
- **RN-004-30** Cada método `Post*` é **atômico** (uma conexão, uma transação, `commit` no fim) e **não propaga** efeitos de rede: Redis, bot e Gemini ficam com quem chama. Erro do banco propaga como exceção (quem chama devolve a mensagem de erro da sua rota).
- **RN-004-31** Toda conta citada precisa ser do `UserId` **e ativa** (desvio 039); o serviço nunca resolve conta por nome (isso é da 011/012, com o mesmo filtro `ativa = true`).
- **RN-004-32** O serviço aplica o sinal (R1); `Amount` nunca é negativo; `0` é aceito (o bot valida `≥ 0,01` antes).
- **RN-004-33** `Origin` é gravado como veio (máx. 20 caracteres). Paridade: o Flask só grava `origem` no `POST /api/transactions` (`'api'`); 011/012 passam `null`, salvo decisão em contrário (Q-004-3).
- **RN-004-34** Os dois lançamentos de par (transferência, pagamento) compartilham data, usuário, `consolidada = true` e são criados na ordem saída, entrada, ligações.
- **RN-004-35** Subcategorias fixas por nome via `ICategoryLookup` (G-3): sempre `ORDER BY id LIMIT 1` (desvio 030).
- **RN-004-36** Tenant: o serviço recebe `UserId` explícito; nenhum método lê ou escreve linha de outro usuário (AC-004-113, 116, 122, 124 e 141).
- **RN-004-37** A fatura de um lançamento vem de `InvoiceCycle` (Domain) com `IClock` só no `EnsureCurrentAsync` (hoje em São Paulo).

### 7.6 `IInvoiceService.EnsureCurrentAsync` (`ensure_current_invoice_exists`)
Origem: `invoice_service.py:80-210`. É o que o bot e o Automate chamam antes de vincular compra a fatura. Para cada cartão (o informado, ou todos os `Cartão de Crédito` **ativos** do usuário; desvio 039) com os dois dias:
1. `hoje = clock.Today`; o ciclo de hoje é `InvoiceCycle.ForPurchase(F, V, hoje)` (a mesma função do lançamento: uma definição por regra, constituição §5; com P-004-1 aprovado, a do Flask só diverge hoje porque tem uma segunda fórmula própria, ver P-004-1).
2. Se não existe fatura `Aberta` com esse `(conta_id, data_vencimento)`, `INSERT ... ON CONFLICT DO NOTHING RETURNING id` (`status = 'Aberta'`, `data_fechamento` do ciclo).
3. Se criou, **depois do commit**, chama `IInvoiceSchedulePopulator.PopulateAsync(...)` e engole exceção (log só do tipo). O `AddServices` de 004 registra um populador **vazio** (`TryAddScoped`); a feature 013 (ou 011, `reprocessar`) registra o real antes. Sem o populador real, a fatura nova nasce vazia e o job das 00:01 (013) a completa.

> Hoje o Flask calcula o ciclo "atual" com uma fórmula diferente da usada na compra (uma só acerta `V ≥ F`, a outra `V < F`); ver P-004-1. Sem aprovação do P-004-1 o item 1 reproduz a fórmula própria do `ensure` (tabela AC-004-96, `InvoiceCycle.CurrentFor`) e a compra segue `ForPurchase` literal.

- **AC-004-96** (só se P-004-1 for rejeitado) Dado `CurrentFor(F, V, hoje)` literal do Flask: `hoje ≤ F(mês)`: vence no mês de hoje (`V ≥ F`) ou no seguinte (`V < F`); `hoje > F(mês)`: vence no mês seguinte a hoje em qualquer caso (para `V < F` isso cai no ciclo já fechado, que o Flask recria como `Aberta` se faltar). Tabela com os mesmos 12 cenários.

### 7.7 Critérios do serviço

**Simples**
- **AC-004-110** Dado `Expense` 50 em conta corrente, então `valor = -50.00`, `tipo_transacao = 'Despesa'`, `fatura_id` NULL, `origem` como passada, `consolidada = true`; `PostedTransaction.StoredAmount = -50.00`.
- **AC-004-111** Dado `Income` 3000, então `+3000.00` e `tipo_transacao = 'Renda'`.
- **AC-004-112** Dado `Expense` em cartão com dias, então vincula (ou cria) a fatura do ciclo e `InvoiceId`/`InvoiceDueDate` voltam preenchidos; `Income` em cartão não vincula.
- **AC-004-113** Dado conta inativa, de outro usuário ou inexistente, então `Failed(AccountNotFound)` e nada gravado (nem fatura); subcategoria inválida ⇒ `Failed(SubcategoryNotFound)` e **nem a fatura** foi criada; `Amount < 0` ⇒ `AmountInvalid`; ordem: valor, conta, subcategoria.
- **AC-004-114** Dados `Notes`, `Consolidated = false`, `ScheduleId`, então gravados nas colunas correspondentes.

**Transferência**
- **AC-004-115** Dada transferência de 100 de A (`Carteira`) para B (`Conta Corrente`), então 2 linhas `Transferência`: `-100` em A com `"Transferência para Conta Corrente"` e `+100` em B com `"Transferência de Carteira"`, mesma subcategoria `Investimentos de Curto Prazo`, `transferencia_par_id` cruzado, sem fatura; `PostedTransfer` traz os ids e os nomes.
- **AC-004-116** Dado `FromAccountId == ToAccountId`, ⇒ `SameAccount`; origem inativa/alheia ⇒ `AccountNotFound`; destino ⇒ `DestinationAccountNotFound`; subcategoria `Investimentos de Curto Prazo` removida ⇒ `RequiredCategoryMissing` e nada gravado; duas subcategorias com o mesmo nome ⇒ usa a de menor `id`.
- **AC-004-117** Dado erro na ligação dos pares (simulado), então nenhuma das duas linhas fica no banco.

**Pagamento de fatura**
- **AC-004-118** Dado cartão com uma fatura `Fechada` (venc. 10/mar) e a do ciclo corrente `Aberta` (venc. 10/abr), pagamento em 20/mar, então paga a **`Fechada`** (desvio 010): as duas linhas levam o `fatura_id` dela, que vira `Paga`; a `Aberta` não muda. Com duas `Fechada` (10/fev e 10/mar), paga a de 10/fev.
- **AC-004-119** Dado cartão sem fatura `Fechada`, então paga a fatura do ciclo de `Date` (criada se faltar) e a marca `Paga`.
- **AC-004-120** Dado pagamento de valor diferente do total da fatura, então a fatura vira `Paga` mesmo assim (R5).
- **AC-004-121** Dadas as descrições `"Pagamento Fatura {cartão}"` na saída (valor negativo, conta de origem) e `"Pagamento Recebido (de {origem})"` na entrada (valor positivo, conta do cartão), subcategoria `Quitação de Empréstimos (Principal)`, pares ligados.
- **AC-004-122** Dado origem == cartão ⇒ `SameAccount`; cartão que não é `Cartão de Crédito` ⇒ `NotACreditCard`; cartão ou origem inativos/alheios ⇒ `DestinationAccountNotFound`/`AccountNotFound`; subcategoria ausente ⇒ `RequiredCategoryMissing`; nada gravado nem fatura marcada.
- **AC-004-123** Dado cartão sem `dia_fechamento`/`dia_vencimento` e sem fatura `Fechada`, então as duas linhas saem **sem** `fatura_id`, `InvoiceId` nulo e `InvoiceMarkedPaid = false`.
- **AC-004-124** Dadas as faturas de **outro usuário** (mesmo `conta_id` impossível) e fatura `Fechada` de outro cartão do mesmo usuário, então nenhuma é paga.

**Compra parcelada**
- **AC-004-130** Dada compra de 3 parcelas de 33,33 em 31/01/2026 num cartão, então: uma transação `Despesa -33.33` em 31/01 (com fatura do ciclo, descrição como veio) e um agendamento `PARCELADO`/`MENSAL` com `valor_previsto 33.33`, `data_inicio 2026-02-28` (dia inexistente), `dia_execucao 31`, `total_parcelas 3`, `parcelas_executadas 1`, `ativo`, mesma conta e subcategoria; a transação não tem `agendamento_id`.
- **AC-004-131** Dado `TotalInstallments` 0 ou 1 ⇒ `InstallmentsInvalid` e nada gravado; conta/subcategoria inválidas ⇒ os erros do `PostSimple`.
- **AC-004-132** Dado erro ao criar o agendamento (simulado), então a transação também não fica (mesma transação).
- **AC-004-133** Dada conta que não é cartão, então a transação sai sem fatura e o agendamento é criado igual.
- **AC-004-134** Dada compra em 15/03/2026, então `data_inicio = 2026-04-15`, `dia_execucao = 15`; compra em 29/12/2027 ⇒ `data_inicio = 2028-01-29`.

**Ensure**
- **AC-004-140** Ver AC-004-95 (criação, populador pós-commit, erro engolido) e AC-004-141: dado cartão **inativo** ou de outro usuário, `EnsureCurrentAsync` não cria nada.

## 8. Dados de produção e impacto

330 transações (300 `Despesa`, 30 `Renda`), 12 faturas, 4 contas. Nenhuma transação tem `Receita` nem `observacoes`/`origem`. Efeitos esperados da virada: (a) criar transação pelo site passa a funcionar; (b) `GET ?tipo=Receita` passa a mostrar as 30 `Renda` do bot; (c) o `GET` sem filtro passa a devolver `tipo: "Receita"` para elas (hoje devolveria `"Renda"`). Conferir em homologação com o banco de produção restaurado: as 12 faturas existentes contra a fórmula do ciclo (Q-004-2).

## 9. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask (coluna "Atual"). Recomendação resumida: **aprovar todos**; os mais sensíveis são P-004-1 e P-004-4.

| Id | Atual (arquivo:linha) | Proposta | Impacto | Recomendação |
|---|---|---|---|---|
| **P-004-1** Vencimento da fatura quando a compra vem **depois** do fechamento e `V ≥ F` | `invoice_service.py:50-62`: vence sempre no 2º mês seguinte (`mes_fech + 1`), certo só se `V < F`; para `V ≥ F` (ex.: fecha dia 10, vence dia 15) a compra de 11/mar vai para a fatura de 15/mai em vez de 15/abr. O `ensure_current_invoice_exists` (`:119-185`) usa outra fórmula (certa para `V ≥ F`, errada para `V < F`), então a fatura "populada" e a fatura da compra podem ser **duas diferentes** | Uma só função `InvoiceCycle.ForPurchase`: `D ≤ F` como hoje; `D > F`: fecha no mês seguinte e vence no **mesmo mês do fechamento** se `V ≥ F`, no **seguinte** se `V < F` (mesma regra do ramo `D ≤ F`). `EnsureCurrentAsync` usa a mesma função (constituição §5) | Compras após o fechamento em cartões com `V ≥ F` passam a cair na fatura com vencimento um mês antes (a correta). Faturas já criadas em produção não mudam. Nenhum campo de API muda. Conferir `contas` e `faturas` de produção (Q-004-2) | **Aprovar**. Sem aprovação, o plan implementa as duas fórmulas literais (`ForPurchase`, `CurrentFor`) |
| **P-004-2** Entradas que viram 500 | §3.5 (`api.py:1344, 1352, 1376, 1363, 1525, 1557, 1590, 1594`; colunas do banco) | Tabela §3.5: 400/404 com mensagens da própria rota (3 mensagens novas: `"Campo 'observacoes' inválido"`, `"Campo 'consolidada' inválido"`, `"Campo 'descricao' deve ter no máximo 255 caracteres"`, `"Campo 'descricao' inválido"`); `valor` não finito ou fora de `numeric(15,2)` ⇒ `"Valor inválido"` | 500/dado `NaN` no banco viram 400 | **Aprovar** (mesmo critério do desvio 033 da 002) |
| **P-004-3** GET devolve campos que o Angular precisa | `api.py:871-881`: sem `conta_id`, `subcategoria_id`, `observacoes`; o front lê `data`, `conta_bancaria`, `conta_bancaria_id`, `observacoes` (`finances.model.ts:10-20`) | Acrescentar no fim de cada item: `conta_id`, `subcategoria_id`, `observacoes`, e os nomes que o front já lê: `data` (= `data_transacao`), `conta_bancaria` (= `conta`), `conta_bancaria_id` (= `conta_id`). Só acréscimo (precedente: desvio 038) | Tela de transações passa a mostrar data e conta; edição vem preenchida | **Aprovar** (a correção do front fica opcional) |
| **P-004-4** Editar transferência e pagamento de fatura | `api.py:1527-1553`: PUT em `Transferência`/`Pagamento Fatura` trata o tipo como despesa (`valor` vira negativo mesmo na perna positiva), permite trocar `tipo`/`conta_id` e não mexe no par: desfaz a transferência pela metade | PUT em linha de tipo `Transferência` ou `Pagamento Fatura` ⇒ 400 `"Transferências e pagamentos de fatura não podem ser editados. Exclua e lance novamente."` | O site hoje mostra essas linhas como `Despesa` e deixaria editar; passa a recusar | **Aprovar** (a exclusão do par já existe) |
| **P-004-5** PUT não recalcula a fatura | `api.py:1519-1611`: mudar `conta_id`, `data` ou `tipo` de uma compra de cartão mantém o `fatura_id` antigo (compra aparece na fatura errada ou numa fatura de conta que não é cartão) | Se o PUT muda `conta_id`, `data` ou `tipo`, recalcular `fatura_id` com a regra do lançamento (RN-004-09), numa só transação; `NULL` se deixou de ser despesa de cartão | Fatura sempre coerente com conta e data | **Aprovar** |
| **P-004-6** Apagar pagamento de fatura deixa a fatura `Paga` | `api.py:1671-1678` não mexe em `faturas` | Ao apagar um `Pagamento Fatura`, a fatura ligada volta a `Fechada` (se `data_fechamento < hoje`) ou `Aberta`, desde que não reste outra linha `Pagamento Fatura` nela | Evita fatura "paga" sem pagamento | **Aprovar** (baixo risco; sem cliente que apague hoje, pois o DELETE também falha) |
| **P-004-7** Pagamento de fatura exige cartão | `transaction_service.py:190, 250-252`: conta que não é cartão ⇒ `get_or_create_fatura` devolve None, o par é gravado sem fatura e nada é marcado | `PostInvoicePaymentAsync` recusa conta destino que não é `Cartão de Crédito` (`NotACreditCard`); a 012 mostra a mensagem dela | Só o bot chama; "pagar fatura" de conta corrente deixa de gravar lixo | **Aprovar** |

## 10. Decisões aplicadas

| Desvio | Estado | Como se aplica |
|---|---|---|
| 001 Receita × Renda | aprovado | RN-004-04, AC-004-04, 11, 43, 44 |
| 008 sem vazamento de erro | aprovado | RN-004-16, AC-004-83 |
| 010 qual fatura é paga | aprovado | §7.3, AC-004-118, 119 |
| 011 parcelas | aprovado | só a **criação** do agendamento (§7.4); incremento e desativação são da 013 |
| 024 corpo sem Content-Type | aprovado | §3.2 e §3.3 (corpo inválido = `{}`), AC-004-12, 40 |
| 027 colunas ausentes | aprovado | migration `0050` (§6.1), RN-004-19 |
| 030 `Outros`/duplicatas por nome | aprovado | RN-004-35 (via `ICategoryLookup`, `ORDER BY id`) |
| 039 contas inativas | aprovado | RN-004-08, RN-004-31, AC-004-20, 49, 113 |
| 015 pote | aprovado | **não se aplica** às rotas: o Flask não valida pote em `POST /api/transactions` (o bot sim, na 012) |
| 012 `agendamento_id` em vez de descrição | aprovado | `PostSimple` aceita `ScheduleId` (§7.2); detecção de "já pago" é da 013 |
| 025 cartão sem acento | aprovado | `Cartão de Crédito` é constante única do Domain (`AccountTypes.Card`, da 002); a 011 a usa |

## 11. O que o Angular espera × o que o Flask devolve

Lido em `E:\Projetos\Projetos\meusecretariofront\src\app\features\finances\` (`services/finances.service.ts:52-141`, `models/finances.model.ts:10-62`, `components/transaction-modal.component.ts`, `transactions.component.ts/.html`). O front usa só as 4 rotas desta spec. **Divergências reais (a maioria já afeta produção hoje):**

1. **Criar e editar não funcionam hoje** (§1): `observacoes` e `origem` ausentes e `conn.begin()` depois de `SELECT`. O front mostra `err.message` (`"Erro ao criar transação"`).
2. **A lista mostra data e conta vazias.** O front lê `transaction.data` e `transaction.conta_bancaria` (`transactions.component.html:168, 179`) e o GET devolve `data_transacao` e `conta`. Correção no backend: P-004-3 (acréscimo); a opção do front é ler os nomes do GET.
3. **Valores com sinal.** O GET devolve despesa negativa (contrato, não muda); o componente soma `valor` das despesas e calcula `receitas - despesas` (`transactions.component.ts:90-110`), o que dá número errado; o badge mostra `Despesa` para qualquer `tipo` que não seja `Receita` (inclusive `Transferência`, `Pagamento Fatura` e, enquanto o desvio 001 não estiver ativo, `Renda`). Correção só no front (`Math.abs`, tratar os 4 tipos).
4. **Edição:** o modal preenche `valor` com o valor **negativo** do GET (o formulário exige ≥ 0,01, então o usuário precisa digitar de novo); não preenche `conta_id`/`subcategoria_id` (o GET não os devolvia; P-004-3 resolve) e sempre envia `consolidada: true`, o que **reconsolida** uma transação não consolidada ao editar. `observacoes` vazia é omitida, então não é possível **limpar** uma observação pelo site.
5. **Filtro `categoria`** é enviado e ignorado pelo Flask (e pelo .NET).
6. **Tipo `Renda` do bot** hoje volta `Renda` no GET e some do filtro `Receita`; com o desvio 001 passa a `Receita`.
7. O front tipa `TransactionsResponse.data` como `Transaction[]`, mas o componente lê `data.transactions` e `data.total` (o formato real do Flask, mantido); o tipo do serviço está errado, o código do componente está certo.
8. Outros clientes (bot, atalhos) não usam estas 4 rotas (usam `/api/transacao` e webhooks, spec 011).

## 12. Fora de escopo

- `GET /api/transactions/recent`, `/api/transacoes/recentes`, `/api/dashboard/*` (006): reutilizam o cálculo de sinal e o mapeamento `Renda` ⇒ `Receita`.
- `POST /api/transacao`, `/webhook-automate`, `/webhook-sms-payment`, `/api/fatura/<id>/reprocessar` (011) e intents do WhatsApp (012): usam o serviço do §7; resolução de conta por nome (exata/ILIKE/fallback), categorização por Gemini, pendências no Redis e checagem de pote/saldo ficam lá.
- Job de fechamento de faturas, `process_invoice_schedules`, `get_fatura_valor`, `get_fatura_detalhada`, aviso de vencimento, motor de agendamentos e execução de parcelas (013).
- Validação de pote (`budget_validation_service`, desvio 015): não é chamada pelas rotas REST.
- CRUD de faturas: não há rota.

## 13. Perguntas em aberto

- **Q-004-1** Confirmar em homologação que `POST/PUT/DELETE /api/transactions` dão 500 no Flask atual (colunas ausentes + `conn.begin()` após `SELECT`). Se o site salva transações hoje, a análise do `begin()` está errada e o resto do contrato vale igual.
- **Q-004-2** Rodar em produção (só `SELECT`): `SELECT id, tipo_conta, dia_fechamento, dia_vencimento FROM contas WHERE tipo_conta = 'Cartão de Crédito'` e `SELECT conta_id, data_fechamento, data_vencimento, status FROM faturas ORDER BY conta_id, data_vencimento`. Serve para ver se existe cartão com `V ≥ F` e se as faturas existentes mostram o efeito do P-004-1.
- **Q-004-3** `origem` nas transações do bot, Automate e SMS: grava-se `null` (paridade) ou os valores que o código já monta (`whatsapp`, `automate`, `api_endpoint`)? Padrão da spec: `null`; os valores só com aprovação da 011/012.
- **Q-004-4** Vincular a 1ª parcela ao agendamento criado (`agendamento_id`)? Padrão: **não** (paridade). Vincular melhoraria o desvio 012, mas o detalhe de fatura mostraria `parcela_info` de todas as parcelas com o contador atual.
- **Q-004-5** Compra em fatura `Fechada` ou `Paga` (RN-004-11): manter o vínculo como hoje (padrão) ou mandar para a fatura aberta seguinte?
- **Q-004-6** Decisão sobre o populador de fatura (§7.6): o registro real vem da 013 (jobs) ou da 011 (reprocessar)? A 004 só fornece o gancho e o populador vazio.
- **Q-004-7** O serviço abre a própria transação por método. Se a 011/012 precisar de um lançamento **dentro** de uma transação maior (por exemplo, lançar e gravar o feedback de pote atomicamente), a porta ganha uma sobrecarga com conexão ambiente; hoje o Flask faz `commit` antes do feedback (`whatsapp_handler.py:378`), então não é necessário.
- **Q-004-8** `valor = 0` é aceito no Flask (só `< 0` é recusado, apesar da mensagem "deve ser positivo"). Manter (padrão) ou recusar (o front exige ≥ 0,01)?
- **Q-004-9** Subcategoria: o Flask checa só `subcategoria.usuario_id`, não `macrocategoria.usuario_id` (a lista da 003 checa as duas). Manter (padrão).
- **Q-004-10** Texto do 500 de `valor` não finito etc. depende de P-004-2 (ver §3.5).
