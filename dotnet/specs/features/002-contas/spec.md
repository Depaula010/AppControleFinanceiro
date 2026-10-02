# Spec 002 — Contas (listar, criar, atualizar, remover)

- **Estado:** aprovada (2026-10-02)
- **Onda:** 1
- **Origem (Python):** `app/routes/api.py:283-727` (`_get_accounts_impl`, `_create_account_impl`, `_update_account_impl`, `_delete_account_impl` e as rotas `/api/accounts*`) e `api.py:1017-1063` (as rotas-alias `/api/contas*`, que chamam as mesmas `_impl`); filtro `token_required` em `api.py:34-90`. Os helpers de `app/services/queries/account_queries.py` e `app/services/finance/account_service.py` **não são chamados por estas rotas** (são usados por dashboard, bot e transações); ver §6.4.
- **Depende de:** 001a (filtro `RequireUser`, `ICurrentUser`, `IDbConnectionFactory`, `Envelopes`) e da migration baseline `0000`. Não precisa de migration nova.
- **Desvios aplicáveis:** 007, 008, 016 (só no PUT), 024, 032, 033, 034, 035, 036, 037, 038 (todos aprovados); 039 só documentado; 049 rejeitado. Decisões em §11.

## 1. Objetivo

O usuário do site vê suas contas com o saldo atual, cria uma conta, edita campos dela e a remove (remoção lógica, só se não houver transações nem agendamentos ativos). Toda operação vale só para as contas do usuário do token: ninguém vê nem altera conta de outro usuário.

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
- Validações de entrada que no Flask viravam 500 do banco passam a ser 400 com mensagem própria (desvios 033 a 036). O 500 ficou só para erro realmente inesperado.

### 3.1 `GET /api/accounts` e `GET /api/contas`

Sem corpo nem query. Origem: `api.py:283-335, 1017-1042`.

Sucesso: **200**
```json
{"status":"success","data":[{"id":1,"nome":"Carteira","banco":"Dinheiro","tipo":"Dinheiro","saldo":150.5,"nome_conta":"Carteira","tipo_conta":"Dinheiro","saldo_inicial":100.0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}]}
```
- `data` tem uma linha por conta **ativa** do usuário, ordenada por `tipo_conta`, depois `nome_conta` (ordenação do banco, sem `COLLATE`; ordem por bytes na imagem Alpine de produção e dos testes).
- `nome` = `nome_conta`; `tipo` = `tipo_conta`; `banco` = **cópia de `tipo_conta`** (a coluna `banco` não existe; `api.py:289-290, 309`).
- `saldo` = `saldo_inicial + SUM(transacoes.valor)` das transações **consolidadas** da conta; sem transações, `saldo_inicial` (desvio 007).
- Campos extras (desvio 038, só acréscimo, depois dos cinco originais): `nome_conta`, `tipo_conta`, `saldo_inicial` (o saldo **inicial**, não o atual), `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento`. `limite_credito` segue a regra do POST: **`null` quando 0** (decisão 049 rejeitada; ver RN-002-11).
- Usuário sem contas ativas: 200 `{"status":"success","data":[]}`.

Erro inesperado: **500** `"Erro ao carregar contas"` (mensagem fixa; desvio 008).

### 3.2 `POST /api/accounts` e `POST /api/contas`

Origem: `api.py:340-498, 1045-1049`.

Entrada (JSON): `nome_conta`, `tipo_conta` (obrigatórios); `saldo_inicial`, `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento` (opcionais). Chaves desconhecidas (`usuario_id`, `ativa`, `banco`...) são ignoradas. O dono da conta é sempre o `user_id` do token.

Validações, **nesta ordem** (a primeira que falhar responde):

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido, sem `Content-Type` JSON, `{}` ou JSON que não seja objeto (desvio 024) | 400 `"Dados não fornecidos"` |
| 2 | `nome_conta` ausente, não texto (`null`, número, lista; desvio 033) ou vazio depois de `trim` | 400 `"Nome da conta é obrigatório"` |
| 3 | `tipo_conta` ausente, não texto (desvio 033) ou vazio depois de `trim` | 400 `"Tipo da conta é obrigatório"` |
| 4 | `tipo_conta` (com `trim`) fora dos 6 valores, comparação exata (maiúsculas e acentos contam) | 400 `"Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro"` |
| 5 | `saldo_inicial` presente, não nulo e não conversível por `float()` (`"abc"`, lista, objeto), **ou** não finito (`"inf"`, `"nan"`), **ou** fora de `numeric(15,2)` (módulo ≥ 10^13 depois de arredondar a 2 casas) (desvio 033) | 400 `"Saldo inicial deve ser um número válido"` |
| 6 | `cor_hex` presente, não nulo e que não é texto no formato `^#[0-9A-Fa-f]{6}$` (desvio 035) | 400 `"Cor inválida. Use o formato #RRGGBB"` |
| 7 | `icone` presente, não nulo e que não é texto de até 50 caracteres (pontos de código) (desvio 035) | 400 `"Ícone inválido"` |
| 8 | `limite_credito` presente, não nulo e não conversível, não finito ou fora de `numeric(15,2)` (**em qualquer tipo** de conta; desvio 033) | 400 `"Limite de crédito deve ser um número válido"` |
| 9 | `tipo_conta` ≠ `Cartão de Crédito` e `dia_fechamento` **ou** `dia_vencimento` presente e não nulo (desvio 034) | 400 `"Dia de fechamento e vencimento só se aplicam a Cartão de Crédito"` |
| 10 | só cartão: `dia_fechamento` presente, não nulo, não conversível por `int()` ou fora de 1..31 | 400 `"Dia de fechamento deve ser entre 1 e 31"` |
| 11 | só cartão: `dia_vencimento` idem | 400 `"Dia de vencimento deve ser entre 1 e 31"` |
| 12 | só cartão: `dia_fechamento` ou `dia_vencimento` ausente ou `null` (desvio 036) | 400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"` |
| 13 | já existe conta **ativa** do usuário com **o mesmo nome** (comparação exata, já cortado em 100) | 400 `"Já existe uma conta com este nome"` |

Se o nome pertence a uma conta **inativa** do mesmo usuário, o POST **reativa** essa linha (desvio 032): `ativa = true`, `inclui_saldo_total = true` e `tipo_conta`, `saldo_inicial`, `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento` sobrescritos com o corpo recebido (defaults abaixo valem do mesmo jeito); o `id` e o `created_at` antigos permanecem; responde 201 como uma criação. (`faturas` e `agendamentos` antigos ligados à conta reaparecem.)

Tratamento dos campos:
- `nome_conta`: `trim`, validado, **depois** cortado em 100 caracteres (pontos de código, como `[:100]` do Python; `api.py:378`). Não há novo `trim` depois do corte. Maiúsculas/minúsculas diferentes são nomes diferentes.
- `saldo_inicial`: ausente ou `null` ⇒ `0.0`. Gravado em `numeric(15,2)`.
- `cor_hex`: ausente ⇒ `"#3B82F6"`; **`null` explícito grava NULL**. `icone`: ausente ⇒ `"wallet"`; `null` explícito grava NULL.
- `limite_credito`, `dia_fechamento`, `dia_vencimento`: ausentes ou `null` ⇒ NULL (nos dias, só fora de cartão; em cartão são obrigatórios).
- Gravados também: `ativa = true`, `inclui_saldo_total = true`, `created_at = agora`. `ordem` fica NULL.

Efeito: um único `INSERT ... ON CONFLICT (usuario_id, nome_conta) DO UPDATE ... WHERE contas.ativa = false RETURNING ...` (cria, reativa a inativa, ou não devolve linha se a ativa já existe). Sem transação explícita.

Sucesso: **201**
```json
{"status":"success","message":"Conta criada com sucesso","data":{"id":4,"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","saldo_inicial":0.0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":5000.0,"dia_fechamento":5,"dia_vencimento":10}}
```
`limite_credito` sai **`null` quando o valor gravado é 0** (teste de falsidade do Python, `api.py:477`; o banco guarda `0.00`). `saldo_inicial` aqui é o saldo **inicial**, não o saldo atual do GET.

Erro inesperado (exceção qualquer): **500** `"Erro ao criar conta"`. Cadastro simultâneo do mesmo nome: uma resposta 201 e a outra 400 como a linha 13 (o `ON CONFLICT` serializa as duas).

### 3.3 `PUT /api/accounts/<id>` e `PUT /api/contas/<id>`

Origem: `api.py:501-657, 1052-1056`. Atualização **parcial**: só as chaves presentes no corpo são alteradas.

Validações, **nesta ordem**:

| # | Condição | Resposta |
|---|---|---|
| 1 | corpo ausente, vazio, inválido ou não-objeto (desvio 024). **Vem antes do 404**: corpo inválido numa conta inexistente responde 400 | 400 `"Dados não fornecidos"` |
| 2 | não existe conta com esse `id` **ativa e do usuário** (inexistente, inativa e de outro usuário dão a mesma resposta) | 404 `"Conta não encontrada"` |
| 3 | `nome_conta` presente e vazio depois de `trim` e corte em 100, **ou que não é texto** (inclui `null`; desvio 033) | 400 `"Nome da conta não pode ser vazio"` |
| 4 | `nome_conta` presente e igual ao de **outra** conta do usuário (ativa ou inativa; desvio 016). Renomear para o próprio nome é permitido | 400 `"Já existe outra conta com este nome"` |
| 5 | `tipo_conta` presente e fora dos 6 valores (inclui vazio e não texto, desvio 033; não há mensagem de "obrigatório" no PUT) | 400 `"Tipo de conta inválido. Valores permitidos: Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro"` |
| 6 | `saldo_inicial` presente e não conversível por `float()`, **inclusive `null`** (no POST `null` vira 0), ou não finito, ou fora de `numeric(15,2)` | 400 `"Saldo inicial deve ser um número válido"` |
| 7 | `cor_hex` presente, não nulo e fora do formato `#RRGGBB` (desvio 035) | 400 `"Cor inválida. Use o formato #RRGGBB"` |
| 8 | `icone` presente, não nulo e que não é texto de até 50 caracteres (desvio 035) | 400 `"Ícone inválido"` |
| 9 | `limite_credito` presente, não nulo e não conversível, não finito ou fora de `numeric(15,2)` | 400 `"Limite de crédito deve ser um número válido"` |
| 10 | `dia_fechamento` presente, não nulo, não conversível ou fora de 1..31 | 400 `"Dia de fechamento deve ser entre 1 e 31"` |
| 11 | `dia_vencimento` idem | 400 `"Dia de vencimento deve ser entre 1 e 31"` |
| 12 | nenhuma das chaves reconhecidas (`nome_conta`, `tipo_conta`, `saldo_inicial`, `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento`) veio no corpo | 400 `"Nenhum campo para atualizar"` |
| 13 | consistência com a conta **atual** (só quando o corpo traz `tipo_conta`, `dia_fechamento` ou `dia_vencimento`): o tipo resultante (o novo ou o atual) não é cartão e algum dia resultante (o novo ou o atual) não é nulo (desvios 034: gravar dia em conta que não é cartão, ou trocar o tipo de um cartão que tem dias sem limpar os dias no mesmo corpo, é **recusado**; nada é limpo em silêncio) | 400 `"Dia de fechamento e vencimento só se aplicam a Cartão de Crédito"` |
| 14 | mesma condição de toque do 13: o tipo resultante é cartão e algum dia resultante é nulo (desvio 036) | 400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"` |

Diferenças em relação ao POST: aqui o corte em 100 vem **antes** do teste de vazio; `null` em `cor_hex` e `icone` grava NULL; `null` em `limite_credito` **limpa** a coluna; `null` nos dias limpa a coluna (só passa na linha 13/14 se o tipo resultante não for cartão).

Efeito: um `UPDATE contas SET <só os campos presentes> WHERE id AND usuario_id AND ativa = true RETURNING ...` (o trigger do banco atualiza `updated_at`). Não é possível reativar conta inativa por aqui (dá 404; a reativação é só pelo POST).

Sucesso: **200**
```json
{"status":"success","message":"Conta atualizada com sucesso","data":{"id":4,"nome_conta":"...","tipo_conta":"...","saldo_inicial":0.0,"cor_hex":"...","icone":"...","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}}
```
`data` tem o mesmo formato e a mesma regra do `limite_credito` zero ⇒ `null` do POST.

Erro inesperado: **500** `"Erro ao atualizar conta"`. Nome duplicado por corrida: 400 como a linha 4.

### 3.4 `DELETE /api/accounts/<id>` e `DELETE /api/contas/<id>`

Origem: `api.py:660-721, 1059-1063`. Sem corpo.

| # | Condição | Resposta |
|---|---|---|
| 1 | não existe conta com esse `id` ativa e do usuário | 404 `"Conta não encontrada"` |
| 2 | há `N > 0` linhas em `transacoes` com `conta_id` = a conta (qualquer tipo, consolidada ou não, de qualquer data) | 400 `"Não é possível excluir esta conta. Existem N transações vinculadas."` |
| 3 | há `N > 0` linhas em `agendamentos` com `conta_id` = a conta e `ativo = true` (desvio 037) | 400 `"Não é possível excluir esta conta. Existem N agendamentos ativos vinculados."` |

Efeito, numa transação: `UPDATE contas SET ativa = false` e `UPDATE usuarios SET conta_padrao_id/_renda_id/_despesa_id = NULL` onde referenciarem a conta (desvio 037). A linha **não** é apagada; recriar uma conta com o mesmo nome a **reativa** (POST, desvio 032). Não mexe em `faturas` nem em agendamentos inativos.

Sucesso: **200** `{"status":"success","message":"Conta removida com sucesso"}` (sem `data`).
Erro inesperado: **500** `"Erro ao remover conta"`.

## 4. Regras de negócio

- **RN-002-01** Todas as 8 operações exigem JWT válido; o `user_id` vem só do token (`ICurrentUser`), nunca do corpo, da query nem da URL. *Origem: `api.py:34-90, 332-335, 494-498, 653-657, 717-721, 1017-1063`.*
- **RN-002-02** Tenant: toda leitura e escrita filtra `usuario_id = <user_id do token>`. Conta de outro usuário é indistinguível de conta inexistente (404 `"Conta não encontrada"`). *Origem: `api.py:301, 422, 516, 538, 613, 670, 696`.*
- **RN-002-03** Só contas com `ativa = true` aparecem no GET e podem ser alteradas ou removidas; conta inativa responde 404 no PUT e no DELETE. *Origem: `api.py:302, 516, 670`.*
- **RN-002-04** Nome de conta é único por usuário **entre todas as contas, inclusive as inativas** (constraint `UNIQUE(usuario_id, nome_conta)`, `contas_usuario_id_nome_conta_key`); comparação exata, sensível a maiúsculas. No POST, nome de conta inativa **reativa** a linha (desvio 032); nome de conta ativa dá 400. No PUT, nome de qualquer outra conta (ativa ou inativa) dá 400 (desvio 016); a violação da UNIQUE por corrida recebe o mesmo 400. *Origem: `api.py:420-431, 536-542`; `0000_baseline.sql:121`; desvios 016 e 032.*
- **RN-002-05** Saldo da conta = `saldo_inicial + SUM(valor)` das transações **consolidadas** (`consolidada = true`) dela; `valor` já tem sinal (despesa negativa). Um único cálculo para todo o sistema (desvio 007). *Origem: `api.py:293-300`; `business-rules.md` R11.*
- **RN-002-06** O GET devolve `{id, nome, banco, tipo, saldo}` (com `banco` repetindo `tipo_conta`) mais os campos extras do desvio 038; ordem `tipo_conta, nome_conta`. *Origem: `api.py:289-290, 304, 307-314`.*
- **RN-002-07** `tipo_conta` só aceita `Conta Corrente`, `Conta Poupança`, `Investimento`, `Cartão de Crédito`, `Dinheiro`, `Outro`, nessa ordem na mensagem de erro. O banco tem o mesmo CHECK (`contas_tipo_conta_check`). *Origem: `api.py:340-343, 371-375, 549-553`.*
- **RN-002-08** Nome: `trim`, cortado em 100 pontos de código (a coluna é `varchar(100)`). No POST valida-se o nome inteiro e corta-se depois; no PUT corta-se e depois testa-se o vazio. *Origem: `api.py:362, 378, 531-533`.*
- **RN-002-09** Conversões como o Python: **`float()`**: número JSON; booleano (`true`⇒1, `false`⇒0); texto que o `float()` aceita (espaços nas pontas, sinal, expoente, `.5`, `5.`; não aceita vírgula decimal). **`int()`**: inteiro JSON; número com parte decimal (truncado: `10.9`⇒10); texto inteiro com ou sem espaços (`"10"`); booleano (`true`⇒1, `false`⇒0). Lista, objeto e `null` não convertem. Resultado de `float()` não finito (`inf`, `nan`) ou que, arredondado a 2 casas, tenha módulo ≥ 10^13 é **inválido** (400 do campo; desvio 033). Os literais JSON `NaN`/`Infinity` (que o Python aceita) tornam o corpo inválido ⇒ `"Dados não fornecidos"` (diferença aceita). *Origem: `api.py:390, 398, 404, 412, 559, 577, 587, 599`.*
- **RN-002-10** Faixa dos dias: 1..31 (CHECK do banco). Dias só existem em `Cartão de Crédito` (`chk_cartao_credito`) e, em cartão, os **dois** são obrigatórios (desvios 034 e 036). Não há validação "fechamento < vencimento". *Origem: `api.py:394-416`.*
- **RN-002-11** `limite_credito` de valor 0 é gravado como `0.00` mas **respondido** como `null` no POST, no PUT e no GET (decisão: desvio 049 rejeitado). *Origem: `api.py:477, 636`.*
- **RN-002-12** Defaults do POST: `saldo_inicial = 0.0`, `cor_hex = "#3B82F6"`, `icone = "wallet"` só quando a chave está **ausente**; `ativa = true`, `inclui_saldo_total = true`. *Origem: `api.py:381-383, 434-441`.*
- **RN-002-13** PUT parcial: só as chaves presentes entram no `UPDATE`; `null` explícito grava NULL em `cor_hex`, `icone`, `limite_credito`, `dia_fechamento`, `dia_vencimento` e dá 400 em `saldo_inicial`. *Origem: `api.py:526-609`.*
- **RN-002-14** Remoção é lógica (`ativa = false`) e só ocorre se a conta não tiver **nenhuma** linha em `transacoes` nem agendamentos ativos; as mensagens levam a contagem exata. Na remoção, `usuarios.conta_padrao_*` que apontam para a conta viram NULL. *Origem: `api.py:681-704`; `business-rules.md` R11; desvio 037.*
- **RN-002-15** (implementação, sem mudança de contrato) A checagem e a remoção acontecem numa transação que trava a linha da conta (`SELECT ... FOR UPDATE`): uma transação em andamento num insert para a conta (que segura `FOR KEY SHARE` pela FK) faz a remoção esperar e então ver a contagem correta. O Flask tinha janela de corrida entre o `COUNT` e o `UPDATE`.
- **RN-002-16** 500 devolve sempre a mensagem fixa da rota; a exceção vai ao log só como tipo (e `SqlState`), sem `str(e)` e sem nome de conta. *Desvio 008.*
- **RN-002-17** Corpo: lido sem exigir `Content-Type`; ausente, vazio, inválido ou não-objeto ⇒ "Dados não fornecidos" (POST e PUT). *Desvio 024.*
- **RN-002-18** `id` da URL: só dígitos casam a rota; número que não cabe em `int` é tratado como conta inexistente (404 JSON `"Conta não encontrada"`), não como rota inexistente. *Origem: conversor `<int:...>` do Werkzeug.*
- **RN-002-19** Logs não contêm token, nome de conta, saldo nem corpo; só operação, `user_id`, `account_id` e `SafeError.Describe`. *Desvio 008, RN-001-08.*
- **RN-002-20** `cor_hex` é `null` ou `#RRGGBB`; `icone` é `null` ou texto de até 50 pontos de código (desvio 035).

## 5. Critérios de aceite

Cada critério vira pelo menos um teste (`[Trait("AC","002-NN")]`). Os de integração comparam o corpo como texto exato.

**Autenticação, roteamento e erros (comuns às 8 operações)**
- **AC-002-80** Dado nenhum header `Authorization`, quando qualquer das 8 operações, então 401 `{"status":"error","message":"Token de autenticação não fornecido"}`; com `Token abc` ⇒ 401 "Formato de autenticação inválido. Use: Bearer <token>"; com token expirado ou adulterado ⇒ 401 "Token inválido ou expirado"; com token sem `user_id` ⇒ 401 "Token não contém user_id". (Primeira prova de integração do `RequireUser`; cobre também AC-001-40 a 44 numa rota real.)
- **AC-002-81** Dado token válido de um usuário, então a operação enxerga e altera só dados desse usuário (verificado nos AC de isolamento 04, 22, 41, 53 e 72).
- **AC-002-82** Dado cada operação, quando chamada por `/api/accounts` e por `/api/contas`, então status e corpo são idênticos.
- **AC-002-83** Dado `/api/accounts/` (barra final), `/api/accounts/abc`, `/api/accounts/-1` ou `/api/contas/1.5`, então 404 com o HTML do Flask, **mesmo sem token** (o roteamento vem antes da autenticação).
- **AC-002-84** Dado `GET /api/accounts/1`, `DELETE /api/accounts` ou `PATCH /api/accounts`, então 405.
- **AC-002-85** Dado que o repositório lança exceção (inclusive `PostgresException` com texto interno), quando qualquer rota, então 500 com a mensagem fixa da rota (`"Erro ao carregar contas"`, `"Erro ao criar conta"`, `"Erro ao atualizar conta"`, `"Erro ao remover conta"`), sem trecho da exceção no corpo, e o log traz só o tipo (desvio 008).
- **AC-002-86** Dado `DELETE /api/accounts/99999999999999999999`, então 404 JSON `"Conta não encontrada"` (RN-002-18).

**GET**
- **AC-002-01** Dado um usuário com contas ativas, então 200 com `data` contendo exatamente as chaves `id, nome, banco, tipo, saldo, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento` nessa ordem, `banco` igual a `tipo`.
- **AC-002-02** Dadas contas de tipos e nomes variados, então a ordem é `tipo_conta` e depois `nome_conta` (nomes de teste em ASCII).
- **AC-002-03** Dada uma conta com `saldo_inicial` 100, uma transação consolidada de -30 e uma **não** consolidada de -50, então `saldo` = 70 (desvio 007); sem transações, `saldo` = `saldo_inicial`; transações de outra conta não entram.
- **AC-002-04** Dadas contas inativas e contas de outro usuário, então nenhuma aparece.
- **AC-002-05** Dado usuário sem contas ativas, então 200 `{"status":"success","data":[]}`.
- **AC-002-06** `saldo` é número JSON com centavos preservados (`70.55`), não texto.
- **AC-002-07** Dado um cartão com limite, dias, cor e ícone, então o GET devolve esses valores nos campos extras (`saldo_inicial` é o inicial, `saldo` o atual); com `limite_credito` 0 no banco, o campo sai `null` (desvio 038, 049 rejeitado).

**POST**
- **AC-002-10** Dado `{"nome_conta":"Nubank","tipo_conta":"Conta Corrente"}`, então 201 com o corpo exato da §3.2: `saldo_inicial` 0, `cor_hex` `"#3B82F6"`, `icone` `"wallet"`, `limite_credito`/`dia_fechamento`/`dia_vencimento` `null`; no banco `ativa = true`, `inclui_saldo_total = true`, `usuario_id` do token.
- **AC-002-11** Dado cartão com `limite_credito` 5000, `dia_fechamento` 5, `dia_vencimento` 10, então 201 devolvendo esses valores; com `limite_credito: 0`, então resposta `null` e banco `0.00`.
- **AC-002-12** Dado corpo ausente, sem `Content-Type`, JSON inválido, `{}`, `null`, `[]`, `[1]` ou `"abc"`, então 400 `"Dados não fornecidos"` (desvio 024), nunca 500.
- **AC-002-13** Dado `nome_conta` ausente, `""`, `"   "`, `null`, `123` ou `["a"]`, então 400 `"Nome da conta é obrigatório"`, mesmo se `tipo_conta` também estiver errado (desvio 033).
- **AC-002-14** Dado `nome_conta` válido e `tipo_conta` ausente, `""`, `"  "`, `null` ou número, então 400 `"Tipo da conta é obrigatório"`.
- **AC-002-15** Dado `tipo_conta` `"corrente"`, `"conta corrente"` ou `"Conta  Corrente"`, então 400 com a mensagem da §3.2 linha 4 (seis valores, nessa ordem); `" Dinheiro "` é aceito e gravado `Dinheiro`.
- **AC-002-16** Dado nome com espaços nas pontas, então é gravado sem eles; dado nome de 120 caracteres (inclusive com acentos e emoji), então grava os 100 primeiros pontos de código e a resposta traz o nome cortado.
- **AC-002-17** Dado `saldo_inicial` `"12.5"`, `12`, `-3.5`, `true` ou `" 7 "`, então aceito; dado `"abc"`, `"1,5"`, `[]` ou `{}`, então 400 `"Saldo inicial deve ser um número válido"`; dado `null`, então 0.
- **AC-002-18** Dado cartão com `limite_credito` `"abc"` ⇒ 400 de limite; `dia_fechamento` 0, 32, `"abc"` ou `[]` ⇒ 400 `"Dia de fechamento deve ser entre 1 e 31"`; `dia_vencimento` idem com a mensagem de vencimento; com os três inválidos vale a ordem limite, fechamento, vencimento; `dia_fechamento` `10.9` e `"10"` ⇒ gravado 10; `true` ⇒ 1.
- **AC-002-19** Dado `Dinheiro` com `limite_credito` 500, então 201 e gravado; dado `Dinheiro` ou `Conta Corrente` com `dia_fechamento` ou `dia_vencimento` não nulo (qualquer valor, até `"abc"`), então 400 `"Dia de fechamento e vencimento só se aplicam a Cartão de Crédito"` e nada gravado (desvio 034); com os dois dias `null` explícitos, então 201.
- **AC-002-20** Dado nome já existente e **ativo** do mesmo usuário, então 400 `"Já existe uma conta com este nome"`; dado o mesmo nome com outra caixa (`nubank`/`Nubank`), então 201.
- **AC-002-21** Dado nome de uma conta **inativa** do mesmo usuário, então 201 reativando a linha (desvio 032): mesmo `id`, `ativa = true`, e tipo, saldo inicial, cor, ícone, limite e dias sobrescritos pelo corpo (o que o corpo não traz volta ao default/NULL); a conta volta ao GET; nenhuma linha nova em `contas`.
- **AC-002-22** Dado o mesmo nome em outro usuário (ativa ou inativa), então 201 e a conta do outro permanece inalterada.
- **AC-002-23** Dadas duas requisições simultâneas com o mesmo nome novo, então uma responde 201 e a outra 400 com `"Já existe uma conta com este nome"`, nunca 500.
- **AC-002-24** Dado `cor_hex` ausente ⇒ `"#3B82F6"`; `null` ⇒ `null` na resposta e no banco; `icone` idem com `"wallet"`; dado `cor_hex` `"azul"`, `"#12345"`, `"#1234567"`, `"3B82F6"` ou `123`, então 400 `"Cor inválida. Use o formato #RRGGBB"`; `"#aBc123"` é aceito; dado `icone` de 51 caracteres ou número, então 400 `"Ícone inválido"`; 50 caracteres é aceito (desvio 035).
- **AC-002-25** Dado corpo com `usuario_id` de outro usuário, `ativa: false` ou `banco: "x"`, então esses campos são ignorados (conta criada para o usuário do token, ativa).
- **AC-002-27** Ver AC-002-13 e 14 (tipos não texto em nome e tipo ⇒ 400 de obrigatório, nunca 500; desvio 033).
- **AC-002-28** Dado `saldo_inicial` ou `limite_credito` igual a `1e30`, `"inf"`, `"nan"` ou `10000000000000`, então 400 com a mensagem do campo (desvio 033); `9999999999999.99` é aceito.
- **AC-002-29** Dado cartão sem `dia_fechamento`, sem `dia_vencimento`, com um deles `null` ou sem nenhum, então 400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"` (desvio 036); com os dois, 201.

**PUT**
- **AC-002-40** Dado corpo ausente, vazio, inválido ou não-objeto, então 400 `"Dados não fornecidos"`, **inclusive quando a conta não existe** (400 antes do 404).
- **AC-002-41** Dado `id` inexistente, de conta inativa ou de **outro usuário**, então 404 `"Conta não encontrada"` com a mesma resposta nos três casos, e a conta do outro usuário permanece intacta.
- **AC-002-42** Dado `{"nome_conta":"Novo"}`, então 200 com o corpo da §3.3, só o nome muda (os demais campos intactos) e `updated_at` avança.
- **AC-002-43** Dado `nome_conta` `""`, `"   "`, `null` ou número, então 400 `"Nome da conta não pode ser vazio"`; dado nome com espaços e mais de 100 caracteres, então `trim` + corte em 100.
- **AC-002-44** Dado `nome_conta` igual ao de **outra** conta ativa do usuário, então 400 `"Já existe outra conta com este nome"`; igual ao da **própria** conta, então 200; igual ao de outra conta **inativa** dele (desvio 016), então 400 (nunca 500); corrida na UNIQUE ⇒ 400.
- **AC-002-45** Dado `tipo_conta` inválido, `""`, `null` ou número, então 400 com a mensagem de tipo; dado tipo válido numa conta sem dias, então atualiza.
- **AC-002-46** Dado `saldo_inicial` `"abc"`, `null`, `1e30` ou `"nan"`, então 400 `"Saldo inicial deve ser um número válido"`; dado `250.75`, então atualiza.
- **AC-002-47** Dado `cor_hex` e `icone` válidos, então atualiza; com `null`, então grava NULL; `cor_hex` fora do formato ⇒ 400 de cor; `icone` com 51 caracteres ⇒ 400 de ícone (desvio 035).
- **AC-002-48** Dado `limite_credito` `null`, então NULL; `"abc"` ⇒ 400 de limite; `0` ⇒ banco `0.00` e resposta `null`; vale em qualquer tipo de conta.
- **AC-002-49** Dado `dia_fechamento`/`dia_vencimento` 0, 32, `"x"` ⇒ 400 com a mensagem do campo; ordem limite, fechamento, vencimento; `null` nos dias de uma conta que não é cartão é aceito (200).
- **AC-002-50** Dado corpo com dois erros (`tipo_conta` inválido e `nome_conta` duplicado), então responde o primeiro da ordem da §3.3 (duplicidade); e nenhum campo é gravado quando qualquer validação falha.
- **AC-002-51** Dado `{"foo":1}` numa conta válida, então 400 `"Nenhum campo para atualizar"`.
- **AC-002-52** Dado `dia_fechamento: 5` numa conta `Dinheiro`, ou `tipo_conta: "Dinheiro"` numa conta de cartão que tem dias, então 400 `"Dia de fechamento e vencimento só se aplicam a Cartão de Crédito"` e a conta fica inalterada; `tipo_conta: "Dinheiro"` com `dia_fechamento: null` e `dia_vencimento: null` no mesmo corpo é aceito e limpa os dias (desvio 034).
- **AC-002-53** Dado token do usuário B e `id` de conta do usuário A, então 404 e a conta de A não muda.
- **AC-002-54** Dado o corpo que o Angular envia hoje (`nome`, `tipo`, `banco`, `saldo`, `cor`, `limite`, `dia_vencimento: null`, `dia_fechamento: null`): numa conta que não é cartão, 200 e nada muda (os dias já eram NULL); num **cartão** com dias, 400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"` e a conta fica intacta (desvio 036; o Flask zerava os dois dias em silêncio). Registra por que o Angular precisa ser corrigido (decisão G-8).
- **AC-002-55** Dado trocar uma conta sem dias para `Cartão de Crédito` sem informar os dois dias (ou com um só), então 400 `"Cartão de Crédito exige dia de fechamento e dia de vencimento"`; com os dois dias no mesmo corpo, 200 (desvio 036); mudar só o `nome_conta` de um cartão antigo que já não tinha dias é aceito (a regra só vale quando o corpo toca tipo ou dias).
- **AC-002-56** Dado `nome_conta` `null` ou número, então 400 `"Nome da conta não pode ser vazio"`; `tipo_conta` não texto ⇒ 400 de tipo (AC-002-43 e 45; desvio 033).

**DELETE**
- **AC-002-70** Dada conta ativa sem transações nem agendamentos ativos, então 200 `{"status":"success","message":"Conta removida com sucesso"}`, `ativa = false` no banco (a linha continua existindo) e a conta some do GET.
- **AC-002-71** Dada conta com 3 transações (uma delas não consolidada), então 400 `"Não é possível excluir esta conta. Existem 3 transações vinculadas."` e a conta continua ativa.
- **AC-002-72** Dado `id` inexistente, inativo ou de outro usuário, então 404 `"Conta não encontrada"` e a conta do outro permanece ativa.
- **AC-002-73** Dado o segundo DELETE da mesma conta, então 404.
- **AC-002-74** Dada conta removida, quando `POST` com o mesmo nome, então 201 com o **mesmo id** (reativação, desvio 032).
- **AC-002-75** Dada uma transação ainda não confirmada inserindo na conta, quando o DELETE começa e a transação confirma, então o DELETE responde 400 com `N = 1` e a conta continua ativa (RN-002-15).
- **AC-002-76** Dada conta com 2 agendamentos ativos e 1 inativo, então 400 `"Não é possível excluir esta conta. Existem 2 agendamentos ativos vinculados."`; só com agendamentos inativos, então 200. Com transações **e** agendamentos ativos, vale a mensagem das transações (desvio 037).
- **AC-002-77** Dada conta usada como `conta_padrao_id`, `conta_padrao_renda_id` e `conta_padrao_despesa_id` do usuário (e outra conta dele como padrão de outro campo), então após o 200 os três campos que apontavam para ela são NULL e os demais não mudam; num 400 nada muda (desvio 037).

## 6. Tabelas e dados tocados

## 6. Tabelas e dados tocados

### 6.1 Tabelas

| Tabela | Operação | Observação |
|---|---|---|
| `contas` | SELECT, INSERT (com `ON CONFLICT` que reativa), UPDATE | nunca DELETE |
| `transacoes` | SELECT (JOIN no saldo; COUNT na remoção) | só leitura; não há índice em `conta_id` (ver plan §7) |
| `agendamentos` | SELECT (COUNT de ativos na remoção) | desvio 037 |
| `usuarios` | UPDATE (`conta_padrao_*` ⇒ NULL na remoção) | desvio 037 |
| `faturas` | nenhuma | referencia `contas`; a rota não a toca |

### 6.2 Constraints de `contas` que a API encontra (`0000_baseline.sql:103-124`)

- `UNIQUE (usuario_id, nome_conta)` (nome padrão `contas_usuario_id_nome_conta_key`), vale para ativas e inativas.
- `contas_tipo_conta_check`: os 6 tipos. A API valida antes; o banco é a rede de segurança.
- `contas_dia_vencimento_check`, `contas_dia_fechamento_check`: 1..31 (ou NULL).
- `chk_cartao_credito`: `tipo_conta = 'Cartão de Crédito' OR (dia_vencimento IS NULL AND dia_fechamento IS NULL)`.
- `nome_conta varchar(100)`, `cor_hex varchar(7)`, `icone varchar(50)`, `saldo_inicial` e `limite_credito` `numeric(15,2)` (módulo máximo menor que 10^13).
- FKs que **apontam** para `contas`: `transacoes.conta_id` ON DELETE **RESTRICT** (por isso a API bloqueia a remoção, embora a remoção lógica nunca dispare a FK); `faturas.conta_id` e `agendamentos.conta_id` CASCADE; `usuarios.conta_padrao_id/_renda_id/_despesa_id` SET NULL. Como a remoção é `ativa = false`, nenhuma dessas ações dispara; por isso a remoção zera `usuarios.conta_padrao_*` explicitamente (desvio 037).
- Trigger `trigger_update_contas_updated_at` mantém `updated_at`.

### 6.3 Tenant (`usuario_id` do JWT)

Nenhuma das 4 rotas permite acessar ou alterar conta de outro usuário: toda checagem de posse usa `usuario_id`. Pontos do Flask **sem** filtro de usuário, avaliados:

| Onde | Consulta | Risco |
|---|---|---|
| `api.py:622` (PUT, releitura) | `SELECT ... FROM Contas WHERE id = :id` | nenhum hoje (a posse foi checada em `api.py:516`); o .NET usa `RETURNING` no próprio `UPDATE`, com `usuario_id` |
| `api.py:682-683` (DELETE, contagem) | `COUNT(*) FROM Transacoes WHERE conta_id = :id` | nenhum (a conta é do usuário); transações de outro usuário apontando para a conta (dado anômalo) também bloqueariam |
| `api.py:613` (PUT, `UPDATE`) | `WHERE id AND usuario_id` | ok; o .NET acrescenta `AND ativa = true` |
| `account_queries.py:142-160, 185-200` (`get_account_name`, `get_account_balance`) e `account_service.py:53-78` | sem `usuario_id` ou sem `ativa` | **não são rotas desta feature**; usadas por bot/transações. Registrado no desvio 039 |

Resposta de leitura/alteração de conta alheia é sempre a de conta inexistente (404), sem revelar existência. Testes de isolamento: AC-002-04, 22, 41, 53, 72.

### 6.4 Código Python que **não** é desta feature
`AccountQueries` e `account_service` (busca por nome exato/ILIKE, `get_saldo_contas`, contas padrão) servem a bot, dashboard e transações (features 004, 006, 011, 012). A 006 deve reutilizar o cálculo de saldo da RN-002-05 (desvio 007).

## 7. Desvios de paridade aprovados e como se aplicam

| # | Estado | Como se aplica nesta feature |
|---|---|---|
| 007 | aprovado | Saldo do GET soma só transações `consolidada = true`. AC-002-03. A 006 usa o mesmo cálculo. |
| 008 | aprovado | Os 4 erros 500 usam mensagens fixas; o log leva só `SafeError.Describe`. AC-002-85. |
| 016 | aprovado | No PUT, nome igual ao de outra conta (ativa ou inativa) ⇒ 400. No POST, a parte "inativa ⇒ 400" é **substituída** pelo desvio 032 (reativa); o 400 do POST vale só para conta ativa. |
| 024 | aprovado | Corpo ausente, vazio, inválido ou não-objeto ⇒ 400 `"Dados não fornecidos"`. AC-002-12 e 40. |
| 032 | aprovado | POST com nome de conta inativa reativa a linha. AC-002-21, 74. |
| 033 | aprovado | Tipos JSON errados em nome/tipo ⇒ 400 de obrigatório (POST) / vazio e tipo inválido (PUT); números não finitos ou fora de `numeric(15,2)` ⇒ 400 do campo. AC-002-13, 14, 28, 43, 45, 46, 56. |
| 034 | aprovado | Dias fora de cartão e troca de tipo de cartão com dias ⇒ 400 (a troca é recusada). AC-002-19, 52. |
| 035 | aprovado | `cor_hex` `#RRGGBB` e `icone` ≤ 50 validados. AC-002-24, 47. |
| 036 | aprovado | Cartão exige os dois dias (POST) e não aceita `null` neles (PUT). AC-002-29, 54, 55. |
| 037 | aprovado | Remoção bloqueada por agendamentos ativos; zera `usuarios.conta_padrao_*`. Em produção, as contas 2 e 3 ficam sem poder ser excluídas até desativar os agendamentos. AC-002-76, 77. |
| 038 | aprovado | GET devolve também os campos extras. AC-002-01, 07. |
| 039 | aprovado | Não é código desta feature: vale para as specs 004, 011 e 012 (filtrar `ativa = true` ao resolver conta). |
| 049 | **rejeitado** | `limite_credito: 0` continua saindo `null` (POST, PUT e, por consistência, GET). |

Os desvios 027 e 028 não se aplicam (tratam de `transacoes.observacoes/origem` e outros caminhos).

## 8. O que o Angular espera x o que o Flask devolve

Lido em `meusecretariofront/src/app/features/finances/` (`services/finances.service.ts`, `models/finances.model.ts`, `accounts.component.*`, `components/account-modal.component.ts`). O front só usa `/api/accounts` (não usa `/api/contas`). **Divergências reais, já presentes em produção. O backend não aceita aliases de nome (decisão G-8): o Angular é corrigido em outro repositório.** O desvio 038 acrescenta ao GET os campos que o front lê.

1. **Criar conta não funciona hoje.** O modal envia `nome`, `tipo` (código em minúsculas: `corrente`, `poupanca`, `cartao_credito`...), `banco`, `saldo`, `cor`, `limite`, `dia_vencimento`, `dia_fechamento`. O backend lê `nome_conta`, `tipo_conta`, `saldo_inicial`, `cor_hex`, `limite_credito`. Resultado: o POST do Angular sempre cai em 400 `"Nome da conta é obrigatório"`.
2. **Editar conta é quase um no-op.** O PUT do Angular só tem de reconhecido `dia_vencimento` e `dia_fechamento`, sempre enviados (`null` se vazios). No Flask zerava os dois dias; no .NET, num cartão com dias, é recusado (desvio 036; AC-002-54).
3. **A tela não mostra as contas por grupo.** O `tipo` do GET é `"Conta Corrente"`, `"Cartão de Crédito"`... e o componente filtra por `'corrente'`, `'poupanca'`, `'cartao_credito'`, `'investimento'`; nenhuma conta casa. A correção é no front.
4. O front lê `cor`, `limite`, `dia_vencimento`, `dia_fechamento` do GET; o GET passa a devolver `cor_hex`, `limite_credito`, `dia_*` e `saldo_inicial` (desvio 038), mas com esses nomes; o mapeamento fica no front.
5. A resposta de POST/PUT é tipada como `BankAccount` pelo front, mas ele só emite o evento e recarrega a lista.
6. O DELETE do front mostra `err.error.message`; os textos do DELETE (transações e agendamentos) chegam ao usuário como estão. Única integração que funciona de ponta a ponta.
7. Quem mais usa `/api/contas` (bot, atalhos): desconhecido (a rota está na whitelist de segurança, `security.py:145-146, 208-209`); ver §10.

## 9. Fora de escopo

- Criação das 3 contas padrão do cadastro (feature 001, já implementada).
- Busca de conta por nome (exata/ILIKE/fallback) e contas padrão do usuário (`account_queries.py`, `account_service.py`): bot, transações, webhooks (011, 012). O filtro `ativa = true` nelas é o desvio 039 (specs 004, 011 e 012).
- Faturas de cartão (`faturas`): feature 004. Esta feature não cria nem altera faturas.
- Dashboard e seu `saldo_total` (006): só herda a RN-002-05.
- CORS, `OPTIONS`/`HEAD` automáticos do Flask e a whitelist de segurança (feature 009).
- Ordenação personalizada (`ordem`), `inclui_saldo_total` (coluna sem uso).

## 10. Perguntas em aberto (respondidas)

1. **Desvio 016, 400 ou reativar?** Reativar (desvio 032) no POST; 400 no PUT.
2. **Front quebrado (§8).** Corrigir o Angular (G-8); o backend não aceita aliases. O GET ganha os campos extras (038).
3. **Quem chama `POST/PUT /api/contas` fora do Angular?** Sem resposta registrada; a spec segue o contrato lido no código.
4. **`NaN`/`Infinity` como literais JSON.** Aceita a diferença: 400 `"Dados não fornecidos"`.
5. **Collation do banco de produção:** `en_US.utf8` em Alpine (ordenação por bytes); o container de teste é a mesma imagem, então a ordem dos testes representa a produção. Risco: migrar para Debian/glibc muda a ordem de nomes com acento.
6. **`100` x `100.0`:** comparação semântica no teste de contrato (Fase 3).

## 11. Decisões aplicadas

Aprovadas pelo Rafael em 2026-10-02 ("aprovo as recomendações", `foundation/decisoes-onda-1.md`).

| Proposta | Desvio | Decisão | Onde está na spec |
|---|---|---|---|
| P-002-1 Reativar conta inativa homônima | 032 | **aprovado** (substitui o 400 do desvio 016 no POST) | §3.2, RN-002-04, AC-002-21 e 74 |
| P-002-2 Tipo JSON errado e números extremos | 033 | **aprovado** | §3.2 linhas 2, 3, 5, 8; §3.3 linhas 3, 5, 6, 9; AC-002-13, 14, 28 |
| P-002-3 Dias e limite fora de cartão, troca de tipo | 034 | **aprovado**; troca de tipo com dias é recusada, nunca limpa em silêncio. `limite_credito` fora de cartão continua aceito (não viola nenhum CHECK) | §3.2 linha 9; §3.3 linha 13; AC-002-19, 52 |
| P-002-4 Validar `cor_hex` e `icone` | 035 | **aprovado** (formato e tamanho) | §3.2 linhas 6 e 7; §3.3 linhas 7 e 8; AC-002-24, 47 |
| P-002-5 Cartão exige os dois dias | 036 | **aprovado** | §3.2 linha 12; §3.3 linha 14; AC-002-29, 54, 55 |
| P-002-6 Remoção e agendamentos/conta padrão | 037 | **aprovado** (as duas partes) | §3.4; RN-002-14; AC-002-76, 77 |
| P-002-7 GET com campos extras | 038 | **aprovado** (só acréscimo) | §3.1; AC-002-01, 07 |
| P-002-8 Contas inativas em outras features | 039 | **aprovado**, só documentado: vale para 004, 011 e 012 | §7 e §9 |
| P-002-9 `limite_credito: 0` como `0.0` | 049 | **rejeitado**: continua `null` | RN-002-11; AC-002-11 |

Escolhas de detalhe tomadas na implementação do POST, dentro dessas decisões (registradas para revisão): o `limite_credito` é validado como número em **qualquer** tipo de conta (antes só em cartão; texto inválido virava 500 do banco); `cor_hex`/`icone` são validados depois do saldo e antes do limite; o campo `limite_credito` extra do GET sai `null` quando 0, como no POST/PUT.
