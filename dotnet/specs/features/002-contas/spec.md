# Spec 002 — Contas (listar, criar, atualizar, remover)

- **Estado:** em revisão
- **Onda:** 1
- **Origem (Python):** `app/routes/api.py:283-727` (`_get_accounts_impl`, `_create_account_impl`, `_update_account_impl`, `_delete_account_impl` e as rotas `/api/accounts*`) e `api.py:1017-1063` (as rotas-alias `/api/contas*`, que chamam as mesmas `_impl`); filtro `token_required` em `api.py:34-90`. Os helpers de `app/services/queries/account_queries.py` e `app/services/finance/account_service.py` **não são chamados por estas rotas** (são usados por dashboard, bot e transações); ver §6.4.
- **Depende de:** 001a (filtro `RequireUser`, `ICurrentUser`, `IDbConnectionFactory`, `Envelopes`) e da migration baseline `0000`. Não precisa de migration nova.
- **Desvios aplicáveis:** 007 (aprovado), 016 (aprovado), 024 (aprovado), 008 (aprovado). Propostas novas em §11.

## 1. Objetivo

O usuário do site vê suas contas com o saldo atual, cria uma conta, edita campos dela e a remove (remoção lógica, só se não houver transações). Toda operação vale só para as contas do usuário do token: ninguém vê nem altera conta de outro usuário.

Esta feature também é a **primeira prova do filtro `RequireUser` numa rota real** (a 001a o testou só por invocação direta, ver `001-auth/tasks.md` T6).

## 2. Rotas

| Método | Caminho | Alias (mesma lógica) | Auth | Envelope |
|---|---|---|---|---|
| GET | `/api/accounts` | `/api/contas` | JWT (`RequireUser`) | A (com `data`) |
| POST | `/api/accounts` | `/api/contas` | JWT | A |
| PUT | `/api/accounts/<int:id>` | `/api/contas/<int:id>` | JWT | A |
| DELETE | `/api/accounts/<int:id>` | `/api/contas/<int:id>` | JWT | A |

São 8 operações. Alias e rota principal têm comportamento idêntico (`api.py:1017-1063`). Como em todo o `/api/*`: sem barra final (`/api/accounts/` dá 404 do Flask, já garantido pelo `StrictRoutingMiddleware`); `<int:id>` com valor não numérico ou negativo dá **404 HTML do Flask** (a rota nem casa), antes da autenticação; método não permitido dá 405 (`GET /api/accounts/5`, `DELETE /api/accounts`, `PATCH ...`).

Envelope A de erro: `{"status":"error","message":"..."}`. Falha de autenticação: as 4 mensagens 401 do `RequireUser` (spec 001 §4.4), antes de qualquer validação da rota.

## 3. Contrato por rota

Convenções desta seção:
- "Texto" é um JSON string. "Número" é o que o Python converte com `float()`; "inteiro" é o que ele converte com `int()` (ver RN-002-09).
- As chaves de resposta saem na ordem escrita aqui (o Flask as ordena alfabeticamente no fio; os clientes não dependem da ordem).
- Valores monetários saem como número JSON (o Flask usa `float`). Um valor inteiro sai como `100` no .NET e `100.0` no Flask: mesmo número para qualquer cliente JSON.
- O 503 `"Banco de dados não configurado"` do Flask (`api.py:285, 352, 503, 662`, quando `db_engine` é nulo) **não existe no .NET**: sem banco configurado o processo nem sobe (`AppSettings` falha no boot).

### 3.1 `GET /api/accounts` e `GET /api/contas`

Sem corpo nem query. Origem: `api.py:283-335, 1017-1042`.

Sucesso: **200**
```json
{"status":"success","data":[{"id":1,"nome":"Carteira","banco":"Dinheiro","tipo":"Dinheiro","saldo":150.5}]}
```
- `data` tem uma linha por conta **ativa** do usuário, ordenada por `tipo_conta`, depois `nome_conta` (ordenação do banco, sem `COLLATE`).
- `nome` = `nome_conta`; `tipo` = `tipo_conta`; `banco` = **cópia de `tipo_conta`** (a coluna `banco` não existe; `api.py:289-290, 309`).
- `saldo` = `saldo_inicial + SUM(transacoes.valor)` das transações **consolidadas** da conta; sem transações, `saldo_inicial` (desvio 007; o Flask somava também as não consolidadas, `api.py:300`).
- Usuário sem contas ativas: 200 `{"status":"success","data":[]}`.
- O GET **não** devolve `cor_hex`, `icone`, `limite_credito`, `dia_*`, `saldo_inicial` (ver §8).

Erro inesperado: **500** `"Erro ao carregar contas"` (mensagem fixa; desvio 008).

### 3.2 `POST /api/accounts` e `POST /api/contas`

Origem: `api.py:340-498, 1045-1049`.

Entrada (JSON): `nome_conta`, `tipo_conta` (obrigatórios); `saldo_inicial`, `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento` (opcionais). Chaves desconhecidas (`usuario_id`, `ativa`, `banco`...) são ignoradas. O dono da conta é sempre o `user_id` do token.

Validações, **nesta ordem** (a primeira que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido, sem `Content-Type` JSON, `{}` ou JSON que não seja objeto (desvio 024; o Flask dava 400 só para `null`/`{}`/`[]`/`0`/`""`/`false` e 500 nos demais) | 400 `"Dados não fornecidos"` |
| 2 | `nome_conta` ou `tipo_conta` presente e que não é texto (`null`, número, lista...). O Flask lança `AttributeError` em `.strip()` e cai no 500 (`api.py:362-363`). Reproduzido; ver P-002-2 | 500 `"Erro ao criar conta"` |
| 3 | `nome_conta` ausente ou vazio depois de `trim` | 400 `"Nome da conta é obrigatório"` |
| 4 | `tipo_conta` ausente ou vazio depois de `trim` | 400 `"Tipo da conta é obrigatório"` |
| 5 | `tipo_conta` (com `trim`) fora dos 6 valores, comparação exata (maiúsculas e acentos contam) | 400 `"Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro"` |
| 6 | `saldo_inicial` presente, não nulo e não conversível por `float()` (`"abc"`, lista, objeto) | 400 `"Saldo inicial deve ser um número válido"` |
| 7 | só se `tipo_conta` = `Cartão de Crédito`: `limite_credito` não nulo e não conversível | 400 `"Limite de crédito deve ser um número válido"` |
| 8 | só cartão: `dia_fechamento` não nulo, não conversível por `int()` ou fora de 1..31 | 400 `"Dia de fechamento deve ser entre 1 e 31"` |
| 9 | só cartão: `dia_vencimento` idem | 400 `"Dia de vencimento deve ser entre 1 e 31"` |
| 10 | já existe conta do usuário com **o mesmo nome** (comparação exata, já cortado em 100), **ativa ou inativa** (desvio 016; o Flask só olhava as ativas, `api.py:420-431`, e a inativa estourava a UNIQUE em 500) | 400 `"Já existe uma conta com este nome"` |

Tratamento dos campos:
- `nome_conta`: `trim`, validado, **depois** cortado em 100 caracteres (pontos de código, como `[:100]` do Python; `api.py:378`). Não há novo `trim` depois do corte. Maiúsculas/minúsculas diferentes são nomes diferentes.
- `saldo_inicial`: ausente ou `null` ⇒ `0.0`. Gravado em `numeric(15,2)`.
- `cor_hex`: ausente ⇒ `"#3B82F6"`; **`null` explícito grava NULL**. `icone`: ausente ⇒ `"wallet"`; `null` explícito grava NULL. Nenhum dos dois é validado (formato nem tamanho): texto acima de 7 (`cor_hex`) ou 50 (`icone`) caracteres falha no banco e vira o 500 abaixo (P-002-4).
- `limite_credito`, `dia_fechamento`, `dia_vencimento`: ausentes ⇒ NULL. **Só são validados se o tipo for cartão.** Em outros tipos entram como vieram: `dia_*` não nulo viola `chk_cartao_credito` ⇒ 500; `limite_credito` numérico é gravado (P-002-3). Cartão sem dias é aceito (as faturas não serão calculadas; P-002-5).
- Gravados também: `ativa = true`, `inclui_saldo_total = true`, `created_at = agora`. `ordem` fica NULL.

Efeito: um `INSERT` em `contas` (uma única instrução; sem transação explícita).

Sucesso: **201**
```json
{"status":"success","message":"Conta criada com sucesso","data":{"id":4,"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","saldo_inicial":0.0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":5000.0,"dia_fechamento":5,"dia_vencimento":10}}
```
`limite_credito` sai **`null` quando o valor gravado é 0** (teste de falsidade do Python, `api.py:477`); o banco guarda `0.00`. `saldo_inicial` aqui é o saldo **inicial**, não o saldo atual do GET.

Erro inesperado (violação de CHECK, texto longo demais, número fora de `numeric(15,2)`, exceção qualquer): **500** `"Erro ao criar conta"`. Cadastro simultâneo do mesmo nome (a UNIQUE estoura entre a checagem e o insert): **400** como a linha 10 (mesma abordagem do desvio 031).

### 3.3 `PUT /api/accounts/<id>` e `PUT /api/contas/<id>`

Origem: `api.py:501-657, 1052-1056`. Atualização **parcial**: só as chaves presentes no corpo são alteradas.

Validações, **nesta ordem**:

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido ou não-objeto (desvio 024). **Vem antes do 404**: corpo inválido numa conta inexistente responde 400 | 400 `"Dados não fornecidos"` |
| 2 | não existe conta com esse `id` **ativa e do usuário** (inexistente, inativa e de outro usuário dão a mesma resposta) | 404 `"Conta não encontrada"` |
| 3 | `nome_conta` presente e não é texto (inclui `null`): `AttributeError` no Flask (`api.py:531`); reproduzido (P-002-2) | 500 `"Erro ao atualizar conta"` |
| 4 | `nome_conta` presente e vazio depois de `trim` e corte em 100 | 400 `"Nome da conta não pode ser vazio"` |
| 5 | `nome_conta` presente e igual ao de **outra** conta do usuário (ativa ou inativa; desvio 016). Renomear para o próprio nome é permitido | 400 `"Já existe outra conta com este nome"` |
| 6 | `tipo_conta` presente e não é texto | 500 `"Erro ao atualizar conta"` |
| 7 | `tipo_conta` (com `trim`) fora dos 6 valores (inclui vazio; não há mensagem de "obrigatório" no PUT) | 400 `"Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro"` |
| 8 | `saldo_inicial` presente e não conversível por `float()`, **inclusive `null`** (no POST `null` vira 0) | 400 `"Saldo inicial deve ser um número válido"` |
| 9 | `limite_credito` presente, não nulo e não conversível | 400 `"Limite de crédito deve ser um número válido"` |
| 10 | `dia_fechamento` presente, não nulo, não conversível ou fora de 1..31 | 400 `"Dia de fechamento deve ser entre 1 e 31"` |
| 11 | `dia_vencimento` idem | 400 `"Dia de vencimento deve ser entre 1 e 31"` |
| 12 | nenhuma das chaves reconhecidas (`nome_conta`, `tipo_conta`, `saldo_inicial`, `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento`) veio no corpo | 400 `"Nenhum campo para atualizar"` |

Diferenças em relação ao POST: aqui o corte em 100 vem **antes** do teste de vazio; `limite_credito`, `dia_*` são validados **em qualquer tipo** (no POST só em cartão); `cor_hex` e `icone` aceitam qualquer valor, inclusive `null` (grava NULL); `null` em `limite_credito`, `dia_fechamento` e `dia_vencimento` **limpa** a coluna. Nada é validado entre campos: mudar o tipo de uma conta que tem dias para algo que não é cartão, ou gravar dia numa conta que não é cartão, viola `chk_cartao_credito` ⇒ 500 e **nada** é alterado (P-002-3).

Efeito: um `UPDATE contas SET <só os campos presentes> WHERE id AND usuario_id` (o trigger do banco atualiza `updated_at`). Não é possível reativar conta inativa por aqui (dá 404).

Sucesso: **200**
```json
{"status":"success","message":"Conta atualizada com sucesso","data":{"id":4,"nome_conta":"...","tipo_conta":"...","saldo_inicial":0.0,"cor_hex":"...","icone":"...","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}}
```
`data` tem o mesmo formato e a mesma regra do `limite_credito` zero ⇒ `null` do POST.

Erro inesperado: **500** `"Erro ao atualizar conta"`. Nome duplicado por corrida: 400 como a linha 5.

### 3.4 `DELETE /api/accounts/<id>` e `DELETE /api/contas/<id>`

Origem: `api.py:660-721, 1059-1063`. Sem corpo.

| # | Condição | Resposta |
|---|---|---|
| 1 | não existe conta com esse `id` ativa e do usuário | 404 `"Conta não encontrada"` |
| 2 | há `N > 0` linhas em `transacoes` com `conta_id` = a conta (qualquer tipo, consolidada ou não, de qualquer data) | 400 `"Não é possível excluir esta conta. Existem N transações vinculadas."` |

Efeito: `UPDATE contas SET ativa = false`. A linha **não** é apagada e o nome continua reservado pela UNIQUE (criar de novo o mesmo nome dá o 400 da linha 10 do POST; ver P-002-1). Não mexe em `faturas`, `agendamentos`, `usuarios.conta_padrao_*` (P-002-6).

Sucesso: **200** `{"status":"success","message":"Conta removida com sucesso"}` (sem `data`).
Erro inesperado: **500** `"Erro ao remover conta"`.

## 4. Regras de negócio

- **RN-002-01** Todas as 8 operações exigem JWT válido; o `user_id` vem só do token (`ICurrentUser`), nunca do corpo, da query nem da URL. *Origem: `api.py:34-90, 332-335, 494-498, 653-657, 717-721, 1017-1063`.*
- **RN-002-02** Tenant: toda leitura e escrita filtra `usuario_id = <user_id do token>`. Conta de outro usuário é indistinguível de conta inexistente (404 `"Conta não encontrada"`). *Origem: `api.py:301, 422, 516, 538, 613, 670, 696`.*
- **RN-002-03** Só contas com `ativa = true` aparecem no GET e podem ser alteradas ou removidas; conta inativa responde 404 no PUT e no DELETE. *Origem: `api.py:302, 516, 670`.*
- **RN-002-04** Nome de conta é único por usuário **entre todas as contas, inclusive as inativas** (constraint `UNIQUE(usuario_id, nome_conta)`); comparação exata, sensível a maiúsculas. A API checa antes de gravar (desvio 016) e também trata a violação da UNIQUE (corrida) com o mesmo 400. *Origem: `api.py:420-431, 536-542`; `0000_baseline.sql:121`; desvio 016.*
- **RN-002-05** Saldo da conta = `saldo_inicial + SUM(valor)` das transações **consolidadas** (`consolidada = true`) dela; `valor` já tem sinal (despesa negativa). Um único cálculo para todo o sistema (desvio 007). *Origem: `api.py:293-300`; `business-rules.md` R11.*
- **RN-002-06** O GET devolve `{id, nome, banco, tipo, saldo}` com `banco` repetindo `tipo_conta`; ordem `tipo_conta, nome_conta`. *Origem: `api.py:289-290, 304, 307-314`.*
- **RN-002-07** `tipo_conta` só aceita `Conta Corrente`, `Conta Poupança`, `Investimento`, `Cartão de Crédito`, `Dinheiro`, `Outro`, nessa ordem na mensagem de erro. O banco tem o mesmo CHECK (`contas_tipo_conta_check`). *Origem: `api.py:340-343, 371-375, 549-553`.*
- **RN-002-08** Nome: `trim`, cortado em 100 pontos de código (a coluna é `varchar(100)`). No POST valida-se o nome inteiro e corta-se depois; no PUT corta-se e depois testa-se o vazio. *Origem: `api.py:362, 378, 531-533`.*
- **RN-002-09** Conversões como o Python: **`float()`**: número JSON; booleano (`true`⇒1, `false`⇒0); texto que o `float()` aceita (espaços nas pontas, sinal, expoente, `.5`, `5.`; não aceita vírgula decimal). **`int()`**: inteiro JSON; número com parte decimal (truncado: `10.9`⇒10); texto inteiro com ou sem espaços (`"10"`); booleano (`true`⇒1, `false`⇒0). Lista, objeto e `null` não convertem (e `null` nem chega à conversão onde é permitido). `NaN`/`Infinity`: ver P-002-2. *Origem: `api.py:390, 398, 404, 412, 559, 577, 587, 599`.*
- **RN-002-10** Em cartão (POST) valida-se `limite_credito`, `dia_fechamento`, `dia_vencimento`, nessa ordem, e só quando não nulos; em outros tipos não se valida nada disso (o banco decide, ver P-002-3). Faixa dos dias: 1..31 (CHECK do banco). Não há validação "fechamento < vencimento". *Origem: `api.py:394-416`.*
- **RN-002-11** `limite_credito` de valor 0 é gravado como `0.00` mas **respondido** como `null` no POST e no PUT. *Origem: `api.py:477, 636`.*
- **RN-002-12** Defaults do POST: `saldo_inicial = 0.0`, `cor_hex = "#3B82F6"`, `icone = "wallet"` só quando a chave está **ausente**; `ativa = true`, `inclui_saldo_total = true`. *Origem: `api.py:381-383, 434-441`.*
- **RN-002-13** PUT parcial: só as chaves presentes entram no `UPDATE`; `null` explícito grava NULL em `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento` e dá 400 em `saldo_inicial`. *Origem: `api.py:526-609`.*
- **RN-002-14** Remoção é lógica (`ativa = false`) e só ocorre se a conta não tiver **nenhuma** linha em `transacoes`; a mensagem leva a contagem exata. *Origem: `api.py:681-704`; `business-rules.md` R11.*
- **RN-002-15** (implementação, sem mudança de contrato) A checagem e a remoção acontecem numa transação que trava a linha da conta (`SELECT ... FOR UPDATE`): uma transação em andamento num insert para a conta (que segura `FOR KEY SHARE` pela FK) faz a remoção esperar e então ver a contagem correta. O Flask tinha janela de corrida entre o `COUNT` e o `UPDATE`.
- **RN-002-16** 500 devolve sempre a mensagem fixa da rota; a exceção vai ao log só como tipo (e `SqlState`), sem `str(e)` e sem nome de conta. *Desvio 008; `api.py:322-329, 484-491, 643-650, 707-714` (o Flask já usava mensagem fixa nestas rotas; mantém).*
- **RN-002-17** Corpo: lido sem exigir `Content-Type` (`JsonBody`-like); ausente, vazio, inválido ou não-objeto ⇒ "Dados não fornecidos" (POST e PUT). *Desvio 024.*
- **RN-002-18** `id` da URL: só dígitos casam a rota; número que não cabe em `int` é tratado como conta inexistente (404 JSON `"Conta não encontrada"`, como o Postgres comparando com o literal grande), não como rota inexistente. *Origem: conversor `<int:...>` do Werkzeug.*
- **RN-002-19** Logs não contêm token, nome de conta, saldo nem corpo; só operação, `user_id`, `account_id` e `SafeError.Describe`. *Desvio 008, RN-001-08.*

## 5. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","002-NN")]`). Os de integração comparam o corpo como texto exato.

**Autenticação, roteamento e erros (comuns às 8 operações)**
- **AC-002-80** Dado nenhum header `Authorization`, quando qualquer das 8 operações, então 401 `{"status":"error","message":"Token de autenticação não fornecido"}`; com `Token abc` ⇒ 401 "Formato de autenticação inválido. Use: Bearer <token>"; com token expirado ou adulterado ⇒ 401 "Token inválido ou expirado"; com token sem `user_id` ⇒ 401 "Token não contém user_id". (Primeira prova de integração do `RequireUser`.)
- **AC-002-81** Dado token válido de um usuário, então a operação enxerga e altera só dados desse usuário (verificado nos AC de isolamento 04, 22, 41, 53 e 72).
- **AC-002-82** Dado cada operação, quando chamada por `/api/accounts` e por `/api/contas`, então status e corpo são idênticos.
- **AC-002-83** Dado `/api/accounts/` (barra final), `/api/accounts/abc`, `/api/accounts/-1` ou `/api/contas/1.5`, então 404 com o HTML do Flask, **mesmo sem token** (o roteamento vem antes da autenticação).
- **AC-002-84** Dado `GET /api/accounts/1`, `DELETE /api/accounts` ou `PATCH /api/accounts`, então 405.
- **AC-002-85** Dado que o repositório lança exceção (inclusive `PostgresException` com texto interno), quando qualquer rota, então 500 com a mensagem fixa da rota (`"Erro ao carregar contas"`, `"Erro ao criar conta"`, `"Erro ao atualizar conta"`, `"Erro ao remover conta"`), sem trecho da exceção no corpo, e o log traz só o tipo (desvio 008).
- **AC-002-86** Dado `DELETE /api/accounts/99999999999999999999`, então 404 JSON `"Conta não encontrada"` (RN-002-18).

**GET**
- **AC-002-01** Dado um usuário com contas ativas, então 200 com `data` contendo exatamente as chaves `id, nome, banco, tipo, saldo` nessa ordem, `banco` igual a `tipo`.
- **AC-002-02** Dadas contas de tipos e nomes variados, então a ordem é `tipo_conta` e depois `nome_conta` (nomes de teste em ASCII para não depender da collation).
- **AC-002-03** Dada uma conta com `saldo_inicial` 100, uma transação consolidada de -30 e uma **não** consolidada de -50, então `saldo` = 70 (desvio 007); sem transações, `saldo` = `saldo_inicial`; transações de outra conta não entram.
- **AC-002-04** Dadas contas inativas e contas de outro usuário, então nenhuma aparece.
- **AC-002-05** Dado usuário sem contas ativas, então 200 `{"status":"success","data":[]}`.
- **AC-002-06** `saldo` é número JSON com centavos preservados (`70.55`), não texto.

**POST**
- **AC-002-10** Dado `{"nome_conta":"Nubank","tipo_conta":"Conta Corrente"}`, então 201 com o corpo exato da §3.2: `saldo_inicial` 0, `cor_hex` `"#3B82F6"`, `icone` `"wallet"`, `limite_credito`/`dia_fechamento`/`dia_vencimento` `null`; no banco `ativa = true`, `inclui_saldo_total = true`, `usuario_id` do token.
- **AC-002-11** Dado cartão com `limite_credito` 5000, `dia_fechamento` 5, `dia_vencimento` 10, então 201 devolvendo esses valores; com `limite_credito: 0`, então resposta `null` e banco `0.00`.
- **AC-002-12** Dado corpo ausente, sem `Content-Type`, JSON inválido, `{}`, `null`, `[]`, `[1]` ou `"abc"`, então 400 `"Dados não fornecidos"` (desvio 024), nunca 500.
- **AC-002-13** Dado `nome_conta` ausente, `""` ou `"   "`, então 400 `"Nome da conta é obrigatório"`, mesmo se `tipo_conta` também estiver errado.
- **AC-002-14** Dado `nome_conta` válido e `tipo_conta` ausente, `""` ou `"  "`, então 400 `"Tipo da conta é obrigatório"`.
- **AC-002-15** Dado `tipo_conta` `"corrente"`, `"conta corrente"` ou `"Conta  Corrente"`, então 400 com a mensagem da §3.2 linha 5 (seis valores, nessa ordem); `" Dinheiro "` é aceito e gravado `Dinheiro`.
- **AC-002-16** Dado nome com espaços nas pontas, então é gravado sem eles; dado nome de 120 caracteres (inclusive com acentos e emoji), então grava os 100 primeiros pontos de código e a resposta traz o nome cortado.
- **AC-002-17** Dado `saldo_inicial` `"12.5"`, `12`, `-3.5`, `true` ou `" 7 "`, então aceito; dado `"abc"`, `"1,5"`, `[]` ou `{}`, então 400 `"Saldo inicial deve ser um número válido"`; dado `null`, então 0.
- **AC-002-18** Dado cartão com `limite_credito` `"abc"` ⇒ 400 de limite; `dia_fechamento` 0, 32, `"abc"` ou `[]` ⇒ 400 `"Dia de fechamento deve ser entre 1 e 31"`; `dia_vencimento` idem com a mensagem de vencimento; com os três inválidos vale a ordem limite, fechamento, vencimento; `dia_fechamento` `10.9` e `"10"` ⇒ gravado 10; `true` ⇒ 1.
- **AC-002-19** Dado `Dinheiro` com `limite_credito` 500, então 201 e gravado; dado `Dinheiro` ou `Conta Corrente` com `dia_fechamento` 5, então 500 `"Erro ao criar conta"` e nada gravado (CHECK `chk_cartao_credito`).
- **AC-002-20** Dado nome já existente e ativo do mesmo usuário, então 400 `"Já existe uma conta com este nome"`; dado o mesmo nome com outra caixa (`nubank`/`Nubank`), então 201.
- **AC-002-21** Dado nome de uma conta **inativa** do mesmo usuário, então 400 `"Já existe uma conta com este nome"` (desvio 016), nada gravado, nunca 500.
- **AC-002-22** Dado o mesmo nome em outro usuário (ativa ou inativa), então 201.
- **AC-002-23** Dadas duas requisições simultâneas com o mesmo nome novo, então uma responde 201 e a outra 400 com a mesma mensagem (violação 23505 traduzida), nunca 500.
- **AC-002-24** Dado `cor_hex` ausente ⇒ `"#3B82F6"`; `null` ⇒ `null` na resposta e no banco; `icone` idem com `"wallet"`; dado `cor_hex` de 8 caracteres, então 500 `"Erro ao criar conta"`.
- **AC-002-25** Dado corpo com `usuario_id` de outro usuário, `ativa: false` ou `banco: "x"`, então esses campos são ignorados (conta criada para o usuário do token, ativa).
- **AC-002-27** Dado `nome_conta` `null`, `123` ou `["a"]`, ou `tipo_conta` não texto, então 500 `"Erro ao criar conta"` (reprodução do Flask; P-002-2).

**PUT**
- **AC-002-40** Dado corpo ausente, vazio, inválido ou não-objeto, então 400 `"Dados não fornecidos"`, **inclusive quando a conta não existe** (400 antes do 404).
- **AC-002-41** Dado `id` inexistente, de conta inativa ou de **outro usuário**, então 404 `"Conta não encontrada"` com a mesma resposta nos três casos, e a conta do outro usuário permanece intacta.
- **AC-002-42** Dado `{"nome_conta":"Novo"}`, então 200 com o corpo da §3.3, só o nome muda (os demais campos intactos) e `updated_at` avança.
- **AC-002-43** Dado `nome_conta` `""` ou `"   "`, então 400 `"Nome da conta não pode ser vazio"`; dado nome com espaços e mais de 100 caracteres, então `trim` + corte em 100.
- **AC-002-44** Dado `nome_conta` igual ao de **outra** conta ativa do usuário, então 400 `"Já existe outra conta com este nome"`; igual ao da **própria** conta, então 200; igual ao de outra conta **inativa** dele (desvio 016), então 400 (nunca 500); corrida na UNIQUE ⇒ 400.
- **AC-002-45** Dado `tipo_conta` inválido ou `""`, então 400 com a mensagem de tipo; dado tipo válido numa conta sem dias, então atualiza.
- **AC-002-46** Dado `saldo_inicial` `"abc"` ou `null`, então 400 `"Saldo inicial deve ser um número válido"`; dado `250.75`, então atualiza.
- **AC-002-47** Dado `cor_hex` e `icone` com texto, então atualiza; com `null`, então grava NULL.
- **AC-002-48** Dado `limite_credito` `null`, então NULL; `"abc"` ⇒ 400 de limite; `0` ⇒ banco `0.00` e resposta `null`; vale em qualquer tipo de conta.
- **AC-002-49** Dado `dia_fechamento`/`dia_vencimento` `null`, então limpa; 0, 32, `"x"` ⇒ 400 com a mensagem do campo; ordem limite, fechamento, vencimento.
- **AC-002-50** Dado corpo com dois erros (`tipo_conta` inválido e `nome_conta` duplicado), então responde o primeiro da ordem da §3.3 (duplicidade); e nenhum campo é gravado quando qualquer validação falha.
- **AC-002-51** Dado `{"foo":1}` numa conta válida, então 400 `"Nenhum campo para atualizar"`.
- **AC-002-52** Dado `dia_fechamento: 5` numa conta `Dinheiro`, ou `tipo_conta: "Dinheiro"` numa conta de cartão que tem dias, então 500 `"Erro ao atualizar conta"` e a conta fica inalterada (P-002-3).
- **AC-002-53** Dado token do usuário B e `id` de conta do usuário A, então 404 e a conta de A não muda.
- **AC-002-54** Dado o corpo que o Angular envia hoje (`nome`, `tipo`, `banco`, `saldo`, `cor`, `limite`, `dia_vencimento: null`, `dia_fechamento: null`), então 200 e **só** `dia_vencimento` e `dia_fechamento` são alterados (viram NULL); registra o comportamento real do Flask (§8).
- **AC-002-56** Dado `nome_conta` `null` ou número, então 500 `"Erro ao atualizar conta"`.

**DELETE**
- **AC-002-70** Dada conta ativa sem transações, então 200 `{"status":"success","message":"Conta removida com sucesso"}`, `ativa = false` no banco (a linha continua existindo) e a conta some do GET.
- **AC-002-71** Dada conta com 3 transações (uma delas não consolidada), então 400 `"Não é possível excluir esta conta. Existem 3 transações vinculadas."` e a conta continua ativa.
- **AC-002-72** Dado `id` inexistente, inativo ou de outro usuário, então 404 `"Conta não encontrada"` e a conta do outro permanece ativa.
- **AC-002-73** Dado o segundo DELETE da mesma conta, então 404.
- **AC-002-74** Dada conta removida, quando `POST` com o mesmo nome, então 400 `"Já existe uma conta com este nome"` (o nome segue reservado; desvio 016).
- **AC-002-75** Dada uma transação ainda não confirmada inserindo na conta, quando o DELETE começa e a transação confirma, então o DELETE responde 400 com `N = 1` e a conta continua ativa (RN-002-15).

## 6. Tabelas e dados tocados

### 6.1 Tabelas

| Tabela | Operação | Observação |
|---|---|---|
| `contas` | SELECT, INSERT, UPDATE | nunca DELETE |
| `transacoes` | SELECT (JOIN no saldo; COUNT na remoção) | só leitura; não há índice em `conta_id` (ver plan §7) |
| `usuarios`, `faturas`, `agendamentos` | nenhuma | referenciam `contas` (ver §6.3); a rota não as toca |

### 6.2 Constraints de `contas` que a API encontra (`0000_baseline.sql:103-124`)

- `UNIQUE (usuario_id, nome_conta)` (nome padrão `contas_usuario_id_nome_conta_key`), vale para ativas e inativas.
- `contas_tipo_conta_check`: os 6 tipos. A API valida antes; o banco é a rede de segurança.
- `contas_dia_vencimento_check`, `contas_dia_fechamento_check`: 1..31 (ou NULL).
- `chk_cartao_credito`: `tipo_conta = 'Cartão de Crédito' OR (dia_vencimento IS NULL AND dia_fechamento IS NULL)`.
- `nome_conta varchar(100)`, `cor_hex varchar(7)`, `icone varchar(50)`, `saldo_inicial` e `limite_credito` `numeric(15,2)` (módulo máximo menor que 10^13).
- FKs que **apontam** para `contas`: `transacoes.conta_id` ON DELETE **RESTRICT** (por isso a API bloqueia a remoção, embora a remoção lógica nunca dispare a FK); `faturas.conta_id` e `agendamentos.conta_id` CASCADE; `usuarios.conta_padrao_id/_renda_id/_despesa_id` SET NULL. Como a remoção é `ativa = false`, nenhuma dessas ações dispara.
- Trigger `trigger_update_contas_updated_at` mantém `updated_at`.

### 6.3 Tenant (`usuario_id` do JWT)

Nenhuma das 4 rotas permite acessar ou alterar conta de outro usuário: toda checagem de posse usa `usuario_id`. Pontos do Flask **sem** filtro de usuário, avaliados:

| Onde | Consulta | Risco |
|---|---|---|
| `api.py:622` (PUT, releitura) | `SELECT ... FROM Contas WHERE id = :id` | nenhum hoje (a posse foi checada em `api.py:516`); o .NET usa `RETURNING` no próprio `UPDATE`, com `usuario_id` |
| `api.py:682-683` (DELETE, contagem) | `COUNT(*) FROM Transacoes WHERE conta_id = :id` | nenhum (a conta é do usuário); transações de outro usuário apontando para a conta (dado anômalo) também bloqueariam |
| `api.py:613` (PUT, `UPDATE`) | `WHERE id AND usuario_id` | ok; o .NET acrescenta `AND ativa = true` |
| `account_queries.py:142-160, 185-200` (`get_account_name`, `get_account_balance`) e `account_service.py:53-78` | sem `usuario_id` ou sem `ativa` | **não são rotas desta feature**; usadas por bot/transações. Registrado em P-002-8 |

Resposta de leitura/alteração de conta alheia é sempre a de conta inexistente (404), sem revelar existência. Testes de isolamento: AC-002-04, 22, 41, 53, 72.

### 6.4 Código Python que **não** é desta feature
`AccountQueries` e `account_service` (busca por nome exato/ILIKE, `get_saldo_contas`, contas padrão) servem a bot, dashboard e transações (features 004, 006, 011, 012). A 006 deve reutilizar o cálculo de saldo da RN-002-05 (desvio 007).

## 7. Desvios de paridade aprovados e como se aplicam

| # | Estado | Como se aplica nesta feature |
|---|---|---|
| 007 | aprovado | O saldo do GET passa a somar só transações `consolidada = true` (`LEFT JOIN ... AND t.consolidada = true`). Efeito visível: conta com transação não consolidada mostra saldo diferente do Flask. Testado em AC-002-03. A 006 usa o mesmo cálculo. |
| 016 | aprovado | A checagem de nome duplicado no POST e no PUT olha **todas** as contas do usuário (sem `ativa = true`); nome de conta inativa responde 400 com a **mesma mensagem** da duplicidade ativa (`"Já existe uma conta com este nome"` / `"Já existe outra conta com este nome"`). Violação da UNIQUE por corrida (`SqlState 23505`, constraint da conta) recebe o mesmo 400. O desvio deixou "reativar" para decidir na spec: **decisão desta spec: 400**; a alternativa está em P-002-1. AC-002-21, 23, 44, 74. |
| 024 | aprovado | Corpo ausente, vazio, inválido, sem `Content-Type` JSON ou que não seja objeto ⇒ 400 `"Dados não fornecidos"` (a mensagem de campos obrigatórios desta rota, que no Flask já saía para corpo vazio) em POST e PUT; nunca 500. No PUT o 400 vem antes do 404, como no Flask. AC-002-12 e 40. |
| 008 | aprovado | Os 4 erros 500 usam as mensagens fixas acima; o log leva só `SafeError.Describe` (tipo e `SqlState`), sem nome de conta, sem corpo, sem token. AC-002-85. (Estas rotas do Flask já não devolviam `str(e)`; o desvio vale pelos logs e para não regredir.) |

Os desvios 027 e 028 não se aplicam (tratam de `transacoes.observacoes/origem` e outros caminhos).

## 8. O que o Angular espera x o que o Flask devolve

Lido em `meusecretariofront/src/app/features/finances/` (`services/finances.service.ts`, `models/finances.model.ts`, `accounts.component.*`, `components/account-modal.component.ts`). O front só usa `/api/accounts` (não usa `/api/contas`). **Divergências reais, já presentes em produção; o .NET reproduz o Flask (regra 1 do CLAUDE.md) e nenhuma delas é "corrigida" aqui:**

1. **Criar conta não funciona hoje.** O modal envia `nome`, `tipo` (código em minúsculas: `corrente`, `poupanca`, `cartao_credito`...), `banco`, `saldo`, `cor`, `limite`, `dia_vencimento`, `dia_fechamento`. O Flask lê `nome_conta`, `tipo_conta`, `saldo_inicial`, `cor_hex`, `limite_credito`. Resultado: o POST do Angular sempre cai em 400 `"Nome da conta é obrigatório"`.
2. **Editar conta é quase um no-op.** Pelas mesmas chaves erradas, o PUT do Angular só tem de reconhecido `dia_vencimento` e `dia_fechamento`, sempre enviados (`null` se vazios): responde 200 e **zera os dois dias** da conta (AC-002-54).
3. **A tela não mostra as contas por grupo.** O GET devolve `tipo` como `"Conta Corrente"`, `"Cartão de Crédito"`... e o componente filtra por `'corrente'`, `'poupanca'`, `'cartao_credito'`, `'investimento'`; nenhuma conta casa. Só o total (`totalBalance`) usa todas. `Dinheiro` e `Outro` nunca têm grupo.
4. O front lê `cor`, `limite`, `dia_vencimento`, `dia_fechamento` do GET; o Flask não os devolve (só `id, nome, banco, tipo, saldo`). Por isso o modal de edição abre sem cor, limite e dias, e preenche `saldo_inicial` com o **saldo atual** (`account.saldo`), que não é o saldo inicial.
5. A resposta de POST/PUT (`nome_conta`, `tipo_conta`, ...) é tipada como `BankAccount` pelo front, mas ele só emite o evento e recarrega a lista; sem efeito prático.
6. O DELETE do front mostra `err.error.message`, e o texto do Flask (`"Não é possível excluir esta conta. Existem N transações vinculadas."`) chega ao usuário como está. Esta é a única integração que funciona de ponta a ponta.
7. Quem mais usa `/api/contas` (bot, atalhos): desconhecido (a rota está na whitelist de segurança, `security.py:145-146, 208-209`); ver §10.

Recomendação: corrigir no Angular (outro repositório) o mapeamento de nomes e de tipo, ou aprovar P-002-7.

## 9. Fora de escopo

- Criação das 3 contas padrão do cadastro (feature 001, já implementada).
- Busca de conta por nome (exata/ILIKE/fallback) e contas padrão do usuário (`account_queries.py`, `account_service.py`): bot, transações, webhooks (011, 012).
- Faturas de cartão (`faturas`): feature 004. Esta feature não cria nem altera faturas.
- Dashboard e seu `saldo_total` (006): só herda a RN-002-05.
- CORS, `OPTIONS`/`HEAD` automáticos do Flask e a whitelist de segurança (feature 009).
- Reativação de conta, ordenação personalizada (`ordem`), `inclui_saldo_total` (coluna sem uso).

## 10. Perguntas em aberto

1. **Desvio 016, 400 ou reativar?** A spec adota 400 (o texto aprovado do 016 prevê 400 com a mensagem de duplicidade). O efeito colateral: excluir "Nubank" e recriar "Nubank" falha com uma mensagem que o usuário não entende (a conta antiga está invisível). Ver P-002-1. Padrão desta spec: 400.
2. **Front quebrado (§8).** Corrigir o Angular (recomendado) ou o backend aceitar também os nomes do front (P-002-7)? Padrão: não mudar o backend.
3. **Quem chama `POST/PUT /api/contas` fora do Angular?** (bot Baileys, atalhos). Se algum cliente usa outros nomes de campo, esta spec precisa dele. Padrão: só o contrato lido no código.
4. **`NaN`/`Infinity` como literais JSON.** O Python aceita (`json.loads`); o `System.Text.Json` rejeita ⇒ 400 `"Dados não fornecidos"`. Padrão: aceitar essa diferença.
5. **Collation do banco de produção** (`SHOW lc_collate`): define a ordem do GET quando há acentos/caixa mista. O .NET deixa a ordenação ao banco (igual ao Flask), mas os testes (container sem a mesma collation) usam só ASCII. Confirmar apenas se importar.
6. **Resposta numérica `100` x `100.0`.** Sem impacto em cliente JSON; se o teste de contrato (Fase 3) comparar texto, precisa de comparação semântica.

## 11. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask. Ids temporários; não existe arquivo em `parity-deviations/` para eles.

### P-002-1 — Reativar conta inativa homônima
- **Atual:** nome de conta inativa bloqueia o POST (hoje 500; com o desvio 016, 400 `"Já existe uma conta com este nome"`); `api.py:420-431`; UNIQUE em `0000_baseline.sql:121`. Remover e recriar a mesma conta é impossível.
- **Proposta:** o POST com nome de conta **inativa do mesmo usuário** reativa a linha (`ativa = true`) e sobrescreve tipo, saldo inicial, cor, ícone, limite e dias com o corpo recebido; responde 201 normal. Seguro porque a remoção só ocorre sem transações.
- **Impacto:** o 400 enganoso some; `id` da conta recriada é o antigo. `faturas`/`agendamentos` antigos ligados à conta reaparecem.
- **Recomendação:** aprovar.

### P-002-2 — Tipo JSON errado e números extremos viram 500
- **Atual:** `nome_conta`/`tipo_conta` não texto (`null`, número, lista) ⇒ `AttributeError` ⇒ 500 (`api.py:362-363, 531, 548`); `saldo_inicial`/`limite_credito` de módulo ≥ 10^13 ou `"inf"` ⇒ erro de overflow do banco ⇒ 500; `"nan"` é gravado como NaN e a resposta sai com `NaN` (JSON inválido).
- **Proposta:** não-texto em nome/tipo ⇒ tratado como ausente (400 `"Nome da conta é obrigatório"` / `"Tipo da conta é obrigatório"` no POST; 400 `"Nome da conta não pode ser vazio"` / `"Tipo de conta inválido..."` no PUT); número não finito ou fora de `numeric(15,2)` ⇒ 400 `"Saldo inicial deve ser um número válido"` / `"Limite de crédito deve ser um número válido"`.
- **Impacto:** 400 no lugar de 500 para entradas absurdas. Nenhum cliente conhecido envia isso.
- **Recomendação:** aprovar. (Ponto de atenção até lá: o .NET trata `"nan"` como `"inf"`, 500, para não emitir JSON inválido.)

### P-002-3 — Dias e limite em conta que não é cartão, e mudança de tipo, estouram CHECK (500)
- **Atual:** no POST só o cartão valida `limite_credito` e `dia_*`; em outro tipo, `dia_*` não nulo ⇒ `chk_cartao_credito` ⇒ 500 `"Erro ao criar conta"`; `dia_*` texto vira erro de tipo do banco ⇒ 500. No PUT, gravar dia em conta não cartão, ou trocar o tipo de um cartão que tem dias, dá 500 `"Erro ao atualizar conta"` (`api.py:394-416, 584-605`).
- **Proposta:** 400 com mensagem clara, por exemplo `"Dia de fechamento e vencimento só se aplicam a Cartão de Crédito"`; na troca de tipo de cartão para outro, limpar os dias automaticamente **ou** recusar com 400 (decidir).
- **Impacto:** 400 no lugar de 500; mensagem nova.
- **Recomendação:** aprovar 400 e recusar a troca de tipo com dias (sem limpar em silêncio).

### P-002-4 — `cor_hex` e `icone` sem validação
- **Atual:** aceitam qualquer valor; mais de 7 / 50 caracteres ⇒ erro do banco ⇒ 500; não há checagem de formato `#RRGGBB` (`api.py:382-383, 564-571`); tipos não texto dependem do driver.
- **Proposta:** `cor_hex` `null` ou `^#[0-9A-Fa-f]{6}$`; `icone` `null` ou até 50 caracteres; senão 400 (`"Cor inválida. Use o formato #RRGGBB"`, `"Ícone inválido"`).
- **Impacto:** 400 no lugar de 500 para tamanho; recusa cores fora do padrão que hoje seriam gravadas (o modal só envia as 10 cores fixas).
- **Recomendação:** aprovar (comprimento sempre; formato opcional).

### P-002-5 — Cartão de crédito sem `dia_fechamento`/`dia_vencimento`
- **Atual:** o POST aceita cartão sem os dias (`api.py:394-416`); o cadastro (`auth.py`) os exige; `get_or_create_fatura` devolve `None` sem os dias (`invoice_service.py:23-24`), então compras no cartão ficam sem fatura, sem erro.
- **Proposta:** no POST, cartão exige os dois dias (400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"`); no PUT, não aceitar `null` em cartão.
- **Impacto:** o Angular (quando corrigido) já tem os campos do cartão; clientes que criam cartão sem dias passam a receber 400.
- **Recomendação:** aprovar.

### P-002-6 — Remoção ignora agendamentos, faturas e conta padrão
- **Atual:** o DELETE só conta `transacoes` (`api.py:681-691`). Conta com `agendamentos` ativos (`agendamentos.conta_id`, motor 013) ou usada como `usuarios.conta_padrao_*` é inativada sem aviso; o motor continua lançando nela. Faturas ficam órfãs.
- **Proposta:** bloquear com 400 `"Não é possível excluir esta conta. Existem N agendamentos ativos vinculados."` e, ao remover, zerar `usuarios.conta_padrao_id/_renda_id/_despesa_id` que a referenciem (na mesma transação).
- **Impacto:** novo 400; o usuário precisa desativar o agendamento antes.
- **Recomendação:** aprovar (as duas partes).

### P-002-7 — Contrato do GET e nomes de campo para o Angular
- **Atual:** §8: GET sem `cor`, `limite`, `dia_*`, `saldo_inicial`; `tipo` com o texto completo; POST/PUT exigem `nome_conta`/`tipo_conta`/`saldo_inicial`, o front usa outros.
- **Proposta (aditiva):** o GET passa a devolver também `nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento` (mantendo `id, nome, banco, tipo, saldo`); POST/PUT **não** mudam. O mapeamento de nomes e tipos é corrigido no Angular.
- **Impacto:** campos novos no JSON (clientes ignoram). O Angular precisa de ajuste de código (repositório separado) para funcionar.
- **Recomendação:** aprovar só o acréscimo no GET e corrigir o Angular; não aceitar aliases no backend.

### P-002-8 — Contas inativas continuam usáveis por outras features
- **Atual:** `account_queries.py:36-40, 59-64, 86-91, 110-115, 133-138` e `account_service.py:23, 53-78, 104-111, 318` buscam conta por nome/tipo **sem** `ativa = true`; o bot e as transações podem lançar numa conta "removida", e depois ela não pode mais ser reativada nem vista.
- **Proposta:** as features que resolvem conta (004, 011, 012) filtram `ativa = true`. Decisão a tomar nas specs delas.
- **Impacto:** compras no bot deixam de cair em conta removida (caem na próxima conta/fallback).
- **Recomendação:** aprovar e levar para as specs 004/011/012.

### P-002-9 — `limite_credito: 0` respondido como `null`
- **Atual:** o teste de falsidade (`if conta.limite_credito else None`, `api.py:477, 636`) devolve `null` para 0; o banco guarda `0.00`.
- **Proposta:** devolver `0.0`.
- **Impacto:** nenhum cliente conhecido distingue (o front usa `if (account.limite)`).
- **Recomendação:** não aprovar (sem ganho; manter paridade).
