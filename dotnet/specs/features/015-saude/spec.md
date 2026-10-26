# Spec 015 — Saúde e Coach (parte B: agenda somente leitura para o Coach)

- **Estado:** rascunho (2026-10-25), **aguarda aprovação do Rafael** (a parte B já foi aprovada por ele em conversa no repositório `amazfit-dados`, "aprovo tudo", 2026-10-25; esta spec formaliza o contrato aqui).
- **Onda:** nova (pós-produção). **Origem:** não há Python: é feature nova e **só aditiva**. Contexto e decisões em `amazfit-dados`: `specs/foundation/coach-visao.md` seções 9 e 11 e `docs/INTEGRACAO_MEU_SECRETARIO.md`.
- **Depende de:** 010 (`ICalendarClient.GetEventsInRangeAsync`, `IGoogleAccountService`), 009 (filtro de segurança: a whitelist é derivada das rotas registradas), desvio 263 (tabela `configuracoes`, migration 0100).
- **Fora desta spec (parte A, adiada):** o bloco "Recuperação" no resumo das 06:00 e as intenções de saúde no WhatsApp (`IHealthDataProvider`, `intent.txt`). Entram numa **emenda** desta spec, com a decisão da Q-005-3 do `amazfit-dados` (o que o resumo vê antes do envio do relógio).
- **Regra inegociável nº 1:** **nenhuma rota existente muda.** Esta feature acrescenta **uma rota** e **nenhuma migration** (a configuração vai nas linhas de `configuracoes`).

## 1. Objetivo

O **Coach** (projeto `amazfit-dados`, no mesmo servidor, rede `rede-global`) precisa enxergar a **agenda** do Rafael para criar a próxima ficha de treino: janelas livres por dia, dias corridos, melhor horário, restrições (ex.: um check-in por dia na academia, aula de boxe numa terça). O Meu Secretário já lê o Google Calendar; esta feature expõe essa leitura, **somente leitura**, para o Coach, com **nível de detalhe configurável por calendário** e **mascaramento** de dados sensíveis, sem copiar a agenda para outro banco.

## 2. Rota

| # | Método | Caminho | Auth | Envelope | Seção |
|---|---|---|---|---|---|
| 1 | GET | `/internal/coach/agenda?de=AAAA-MM-DD&ate=AAAA-MM-DD` | `X-API-KEY` = `COACH_AGENDA_API_KEY` (**chave própria**, diferente de `API_SECRET_KEY`) | próprio (§3) | 3 |

- **Chamada só por rede interna**: o Coach usa `http://meu-secretario-api:8000/internal/coach/agenda` na `rede-global`. A rota **não** é anunciada ao Angular, ao bot nem ao proxy do Render; o nginx do host não ganha regra nova.
- **Desligada por padrão (RN-015-01):** sem `COACH_AGENDA_API_KEY` **ou** sem `COACH_AGENDA_USER_ID` a rota **não é registrada** (404, como qualquer caminho desconhecido). Nada muda para quem não configura.

## 3. Contrato

### 3.1 Entrada
`de` e `ate` (datas `AAAA-MM-DD`, **obrigatórias**), no fuso `America/Sao_Paulo`, ambas inclusivas. `de ≤ ate` e o intervalo tem **no máximo 42 dias** (6 semanas). Qualquer outra coisa: **400** `{"erro":"Informe de e ate (AAAA-MM-DD), com até 42 dias"}`.

### 3.2 Saída (200)
```json
{
  "de": "2026-10-26", "ate": "2026-11-08", "geradoEm": "2026-10-25T21:00:00-03:00",
  "eventos": [
    { "inicio": "2026-10-27T09:00:00-03:00", "fim": "2026-10-27T10:00:00-03:00", "diaInteiro": false, "nivel": "completo",
      "titulo": "Reunião de planejamento", "descricao": "…", "local": "…", "calendario": "Trabalho" }
  ]
}
```
- `inicio` e `fim`: ISO 8601 com o deslocamento (`-03:00`) para evento com hora; **só a data** (`AAAA-MM-DD`, `fim` exclusivo como no Google) para `diaInteiro = true`.
- **Campos por nível** (por calendário, RN-015-03): `ocupado` = `inicio`, `fim`, `diaInteiro`, `nivel`; `titulos` = o anterior + `titulo`; `completo` = o anterior + `descricao`, `local` e `calendario` (nome). Campo que o nível não inclui **não aparece** (nem nulo).
- Ordenado por `inicio`. **Nunca** devolve id de evento, e-mail de participante, link de reunião nem anexo.

### 3.3 Erros
| Situação | Resposta |
|---|---|
| Sem `X-API-KEY` ou chave errada | **401** `{"erro":"Chave inválida"}`; o corpo e a query nem são lidos |
| Google Calendar não conectado para o usuário | **503** `{"erro":"Google Calendar não conectado"}` |
| Falha ao ler o Google (rede, cota, token) | **503** `{"erro":"Agenda indisponível agora"}` (texto fixo; o log leva só `SafeError.Describe`) |
| Entrada inválida | **400** (3.1) |

## 4. Regras
- **RN-015-01 Desligada por padrão:** ver §2.
- **RN-015-02 Usuário:** a agenda lida é a do usuário `COACH_AGENDA_USER_ID` (um inteiro; o Coach é de uma pessoa só). Usa `ICalendarClient.GetEventsInRangeAsync` (calendários **selecionados**, multi-calendário, ordenado por início); calendário com erro é pulado, como já faz hoje.
- **RN-015-03 Nível por calendário (configurável sem deploy):** na tabela `configuracoes`: `coach_agenda.nivel_padrao` (padrão **`completo`**, a escolha do Rafael) e, opcional, `coach_agenda.nivel.<id-do-calendario>` (sobrepõe o padrão), valores `ocupado | titulos | completo`. Lidos com cache de **60 s** (como a `gemini.modelo`). **Falha segura:** valor desconhecido num calendário vale **`ocupado`**; se o **banco** falhar, tudo vale `ocupado`. A sugestão para o calendário do trabalho é `titulos` ou `ocupado` (informação confidencial da empresa não deve ir a uma IA externa).
- **RN-015-04 Mascaramento (ligado por padrão):** `coach_agenda.mascarar` (padrão `true`). Em `titulo`, `descricao` e `local`, antes de sair: links (`http(s)://…`, `www.…`, domínios de reunião como meet.google.com, zoom.us, teams.microsoft.com) viram `[link]`; e-mails viram `[e-mail]`; telefones (10 a 13 dígitos com ou sem `+55`, parênteses, espaços ou hífens) viram `[telefone]`; **códigos de acesso** (o trecho depois de `senha`, `código`, `codigo`, `pin`, `token`, `acesso`, com `:` ou `=`, e sequências de 6 ou mais dígitos soltas) viram `[código]`. Com `mascarar = false`, nada é trocado.
- **RN-015-05 Limites de tamanho:** `titulo` até 120 caracteres, `local` até 120, `descricao` até 600 (corta e acrescenta `…`); o corte vale **depois** do mascaramento.
- **RN-015-06 Nunca em log:** título, descrição, local, nome de calendário e a agenda devolvida **não aparecem em nenhum log** (nem em erro). O log só traz: chamada atendida, número de eventos, intervalo pedido e o motivo do erro.
- **RN-015-07 Somente leitura e sem cópia:** a rota não escreve no Google nem em banco algum; a agenda **não é guardada** aqui (o Coach guarda, já mascarada, o que enviou à IA).
- **RN-015-08 Tempo limite:** a leitura do Google tem teto de **10 s**; estourou = 503 `Agenda indisponível agora`.
- **RN-015-09 Chave em tempo constante:** a comparação da `X-API-KEY` usa a mesma rotina de tempo constante das outras rotas com chave (`IApiKeyValidator`, com a chave própria).

## 5. Critérios de aceite
- **AC-015-01** Dado `COACH_AGENDA_API_KEY` ou `COACH_AGENDA_USER_ID` ausente, então `GET /internal/coach/agenda` responde **404** e o resto da API é idêntico ao de antes (nenhuma outra rota muda).
- **AC-015-02** Dado a chave errada ou ausente, então **401** `Chave inválida`, o Google **não é chamado** e `API_SECRET_KEY` **não** é aceita nesta rota.
- **AC-015-03** Dado `de` ou `ate` ausente, inválido, `de > ate` ou mais de 42 dias, então **400** e o Google não é chamado.
- **AC-015-04** Dado calendário em `ocupado`, então o evento sai **sem** `titulo`, `descricao`, `local` e `calendario`; em `titulos`, só com `titulo`; em `completo`, com os quatro.
- **AC-015-05** Dado `coach_agenda.nivel.<id>` definido, então sobrepõe o padrão só para aquele calendário; sem linha, vale `coach_agenda.nivel_padrao`; sem nenhuma linha, vale `completo`.
- **AC-015-06** Dado valor de nível desconhecido, então vale `ocupado` para aquele calendário; dado banco indisponível, então **todos** valem `ocupado` e a rota responde 200.
- **AC-015-07** Dado `mascarar = true`, então link, e-mail, telefone e código saem como `[link]`, `[e-mail]`, `[telefone]` e `[código]` em título, descrição e local; com `false`, o texto sai igual.
- **AC-015-08** Dado título de 300 caracteres e descrição de 2000, então saem com 120 e 600 (com `…`), medidos depois do mascaramento.
- **AC-015-09** Dado evento de dia inteiro, então `diaInteiro = true` e `inicio`/`fim` são só datas; dado evento com hora, então vêm com `-03:00`.
- **AC-015-10** Dado Google não conectado, então **503** `Google Calendar não conectado`; dado falha ou estouro de 10 s, então **503** `Agenda indisponível agora`.
- **AC-015-11** Dado qualquer chamada, então nenhum log contém título, descrição, local nem nome de calendário (teste sobre o provedor de log).
- **AC-015-12** Dado a resposta, então não há id de evento, e-mail de participante nem link de reunião fora do mascaramento.
- **AC-015-13** Dado `GET /` e as rotas existentes, então status e corpo **inalterados** (teste de contrato existente continua verde).

## 6. Segurança e privacidade
- Chave **própria** e opcional (`COACH_AGENDA_API_KEY`), como secret do GitHub; **nunca** em arquivo, log ou chat. Rota só por rede interna.
- A agenda tem dados de terceiros (colegas, clientes): por isso o **padrão `completo` é do Rafael** (decisão dele, 2026-10-19), mas o calendário do trabalho deve ser configurado como `titulos` ou `ocupado` antes do primeiro uso, e o **mascaramento fica ligado**.
- O Coach (outro projeto) é quem envia, **já mascarada**, a agenda a uma IA externa e guarda a cópia mascarada; este serviço só responde.

## 7. Perguntas em aberto
- **Q-015-1 (Rafael):** quais calendários e que nível para cada um? (sugestão: calendário pessoal `completo`; trabalho `titulos` ou `ocupado`). Sem resposta, vale `completo` para todos, **inclusive o do trabalho**, e a spec **recomenda** conferir isso antes do primeiro uso.
- **Q-015-2:** o `COACH_AGENDA_USER_ID` é o id do Rafael em `usuarios`? (conferir no banco antes do deploy.)
- **Q-015-3:** o Coach precisa de eventos **passados** (últimos dias) para calibrar o ritmo? A spec permite qualquer intervalo de até 42 dias (passado ou futuro); sem mudança.
