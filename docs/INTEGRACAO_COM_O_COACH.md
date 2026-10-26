# Integração com o Coach (agenda somente leitura, specs 015 B e 016)

O **Coach** (projeto `amazfit-dados`, no mesmo servidor, rede `rede-global`) lê a agenda do Rafael por **uma rota nova e só de leitura** deste serviço. Nenhuma rota existente muda.

## Rota
`GET /internal/coach/agenda?de=AAAA-MM-DD&ate=AAAA-MM-DD` (até 42 dias, datas inclusivas), chamada pela rede interna: `http://meu-secretario-api:8000/internal/coach/agenda`, com o header `X-API-KEY` = a chave da pessoa (**não** vale a `API_SECRET_KEY`).

Respostas: `200` com `{de, ate, geradoEm, eventos[]}`; `400` entrada inválida; `401` chave errada; `503` Google não conectado ou indisponível. Cada evento traz `inicio`, `fim`, `diaInteiro`, `nivel` e, conforme o nível do calendário, `titulo`, `descricao`, `local` e `calendario`. Nunca traz id de evento, e-mail de participante nem link de reunião (mascarado).

## A chave é a sua api key (a mesma do atalho)
Decisão do Rafael: **uma chave só**. A rota da agenda aceita a **api key pessoal** do usuário (`api_key_automate`, a que o atalho já usa) e descobre o dono por ela. No Coach, em Fichas → Conexões e chaves, cole **essa** chave; não precisa gerar nenhuma outra. Para ver a sua, peça "minha api key" no WhatsApp do Meu Secretário. Ressalva: essa chave também lança transações, por isso no Coach ela fica cifrada no banco e nunca volta para a tela.

A seção abaixo (chave só da agenda, gerada com `POST /coach/agenda/chave`) continua existindo como **opção mais restrita** (só lê a agenda). O comando "chave do coach" no WhatsApp foi **removido** (decisão do Rafael, 2026-10-26): use a api key pessoal.

## Chave só da agenda (opcional, spec 016)
Cada pessoa tem a **própria chave de leitura da agenda**. O banco guarda só o hash (tabela `coach_agenda_chaves`, migration `0110`); a chave aparece **uma única vez**, ao ser gerada. A rota descobre o dono pela chave e lê a agenda **dele**. Não há secret por pessoa nem deploy para incluir alguém.

Rotas com o login normal do app (`Authorization: Bearer <token>`):

| Rota | O que faz |
|---|---|
| `POST /coach/agenda/chave` | gera a chave (`201`, devolve `chave` e `final`) e **revoga a anterior** |
| `GET /coach/agenda/chave` | `existe`, `final`, `criadaEm`, `ultimoUsoEm`; nunca a chave |
| `DELETE /coach/agenda/chave` | revoga a chave |

Depois a pessoa cola a chave no Coach (Fichas → Conexões e chaves) e toca em "Testar conexão". A rota `POST` acima gera a chave só da agenda para quem precisar dela.

A rota da agenda está **sempre registrada**: sem nenhuma chave ativa responde `401`.

## Transição (chave antiga do ambiente)
Se os secrets `COACH_AGENDA_API_KEY` e `COACH_AGENDA_USER_ID` existirem, continuam valendo como uma chave extra daquele usuário. Depois de gerar a chave própria, os dois podem ser apagados. **Não são mais necessários** para ligar a rota.

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
