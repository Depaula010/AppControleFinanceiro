# Integração com o Coach (agenda somente leitura, spec 015 parte B)

O **Coach** (projeto `amazfit-dados`, no mesmo servidor, rede `rede-global`) lê a agenda do Rafael por **uma rota nova e só de leitura** deste serviço. Nenhuma rota existente muda e não há migration.

## Rota
`GET /internal/coach/agenda?de=AAAA-MM-DD&ate=AAAA-MM-DD` (até 42 dias, datas inclusivas), chamada pela rede interna: `http://meu-secretario-api:8000/internal/coach/agenda`, com o header `X-API-KEY` = `COACH_AGENDA_API_KEY` (**não** vale a `API_SECRET_KEY`).

Respostas: `200` com `{de, ate, geradoEm, eventos[]}`; `400` entrada inválida; `401` chave errada; `503` Google não conectado ou indisponível. Cada evento traz `inicio`, `fim`, `diaInteiro`, `nivel` e, conforme o nível do calendário, `titulo`, `descricao`, `local` e `calendario`. Nunca traz id de evento, e-mail de participante nem link de reunião (mascarado).

## Ligar (desligada por padrão)
Duas variáveis, as duas como **secrets do GitHub** (nunca no chat nem no Git), entregues ao container pelo deploy:

| Secret | O que é |
|---|---|
| `COACH_AGENDA_API_KEY` | chave própria desta rota (gere uma aleatória e longa; o Coach recebe a mesma) |
| `COACH_AGENDA_USER_ID` | o `id` do Rafael na tabela `usuarios` (conferir antes: `SELECT id, email FROM usuarios;`) |

Sem **as duas**, a rota não é registrada (404) e nada mais muda. Depois de criar os secrets, o próximo deploy a liga.

## Nível de detalhe por calendário (sem deploy)
Na tabela `configuracoes` (valem em até 60 s):

```sql
-- padrão para todos os calendários (sem linha, vale 'completo')
INSERT INTO configuracoes (chave, valor) VALUES ('coach_agenda.nivel_padrao', 'completo')
ON CONFLICT (chave) DO UPDATE SET valor = EXCLUDED.valor, atualizado_em = NOW();

-- o calendário do trabalho só com títulos (ou 'ocupado' para só os horários); a chave leva o id do calendário no Google
INSERT INTO configuracoes (chave, valor) VALUES ('coach_agenda.nivel.trabalho@empresa.com', 'ocupado')
ON CONFLICT (chave) DO UPDATE SET valor = EXCLUDED.valor, atualizado_em = NOW();

-- mascaramento de links, e-mails, telefones e códigos (ligado por padrão; só 'false' desliga)
INSERT INTO configuracoes (chave, valor) VALUES ('coach_agenda.mascarar', 'true')
ON CONFLICT (chave) DO UPDATE SET valor = EXCLUDED.valor, atualizado_em = NOW();
```

Valores: `ocupado` (só horários), `titulos` (+ título), `completo` (+ descrição, local e nome do calendário). **Valor desconhecido vale `ocupado`**, e se o banco falhar tudo vale `ocupado` e o mascaramento fica ligado.

**Antes do primeiro uso**, configure o calendário do **trabalho** como `titulos` ou `ocupado`: título e descrição podem ter informação confidencial da empresa que não deve ir a uma IA externa. Para ver os ids dos calendários, use os calendários selecionados da conta Google conectada.

## Privacidade
Título, descrição, local e nome de calendário **nunca vão para log** (o log só tem a contagem de eventos, o intervalo e o motivo de uma falha). A agenda não é guardada aqui: o Coach guarda, já mascarada, o que enviou à IA.
