# Decisões da onda 1 (specs 002, 003 e 008)

Consolida o que os agentes de spec deixaram para aprovação. Fontes: seções "Desvios propostos" e "Perguntas em aberto" de `features/002-contas`, `003-categorias` e `008-api-keys-lgpd`. A 010 (Integrações) entra num segundo documento quando sair.

**Como responder:** aprove tudo de uma vez ("aprovo as recomendações") ou liste só as exceções ("aprovo as recomendações, exceto P-002-1 e G-5"). O que ficar sem resposta **não é implementado**: o .NET reproduz o Flask.

Legenda: **A** = aprovar, **R** = rejeitar (manter o Flask), **A\*** = aprovar com condição.

---

## 1. Decisões gerais (valem para mais de uma feature)

| # | Decisão | Recomendação | Por quê |
|---|---|---|---|
| **G-1** | **008 em três entregas:** 008-0 (filtro `RequireApiKey`, formatador de data, porta `IApiKeyProvider`; PR separado e primeiro), 008a (chaves, preferências, uso, logs), 008b (LGPD) | **A** | O filtro é reutilizado pela 009 e pelo webhook do WhatsApp; a 010 só depende da porta. Evita conflito entre agentes. |
| **G-2** | **Migration `0020_categorias_globais.sql`** (seed idempotente, guarda tudo-ou-nada, nunca `DELETE`) + adendo ao ADR 0001 sobre dados de referência | **A** | Em produção não insere nada (as categorias já existem); em banco novo (homol/testes) cria o seed que hoje só existe por rota admin. |
| **G-3** | **Lookups por nome na 003** (`Investimentos de Curto Prazo`, `Quitação de Empréstimos (Principal)`, `Outros`, sempre `ORDER BY id`, desvio 030) | **A** | A 004 e a 011 precisam; senão cada uma reescreve o SQL. |
| **G-4** | **PR de apoio** antes da onda: `PostgresFixture.ResetAsync` passa a reaplicar o seed de categorias (hoje o `TRUNCATE usuarios CASCADE` o apaga) | **A** | Arquivo compartilhado: tem de entrar sozinho, antes das features paralelas. |
| **G-5** | **HEAD e 405 nas rotas GET:** o Flask responde HEAD sozinho e 405 em HTML; o ASP.NET não responde HEAD em `MapGet` | **R (aceitar a diferença)** | Nenhum cliente conhecido usa HEAD. |
| **G-6** | **Exposição das rotas `/api-keys/*`:** (A) todas expostas, protegidas só por `X-API-KEY` (que é o mesmo segredo do JWT); (B) todas **desligadas por padrão** em produção, com `API_KEYS_ROUTES_ENABLED=true` para ligar; (C) restringir no nginx | **B** | Hoje só `/api-keys/preferencias/*` é alcançável de fora e ninguém conhecido usa as demais. Expor 15 rotas novas que leem e apagam dados de qualquer usuário, atrás de um único segredo, é risco sem cliente que precise. (C) é frágil: atrás do nginx nativo o IP de origem não é o do cliente. Este item **refina** o desvio 005 só para `/api-keys/*`. |
| **G-7** | **Texto fixo dos 500 em `/api-keys/*`:** `<título>. Tente novamente.` | **A** | Desvio 008 já exige texto fixo. |
| **G-8** | **Corrigir o Angular** (repositório `meusecretariofront`, fora desta reescrita): o modal de contas envia `nome/tipo/saldo/cor/limite` e o Flask lê `nome_conta/tipo_conta/...`; o filtro de grupos compara `'corrente'` com `"Conta Corrente"` | **A (tarefa à parte)** | Hoje criar e editar conta pelo site não funciona (ver §2.1). O backend não deve aceitar aliases; o front se ajusta. |

---

## 2. Feature 002 — Contas (9 desvios propostos)

### 2.1 O que a leitura do front revelou
O Angular de contas está desalinhado do Flask hoje: **criar conta dá 400** (`"Nome da conta é obrigatório"`), **editar só zera** `dia_vencimento` e `dia_fechamento`, e **nenhuma conta aparece nos grupos** da tela. É a mesma família do problema das transações (`observacoes`, desvio 027). Confirme abrindo a tela de contas no site; se estiver diferente do descrito, avise.

### 2.2 Desvios

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| 032 | P-002-1 | POST com nome de conta **inativa** do mesmo usuário reativa a linha (hoje: 500; com o desvio 016, 400 enganoso, porque excluir e recriar "Nubank" falha) | **A** |
| 033 | P-002-2 | Tipo JSON errado (nome/tipo não texto) e números extremos (`NaN`, `1e30`): 400 em vez de 500 | **A** |
| 034 | P-002-3 | Dias/limite em conta que **não** é cartão, ou trocar tipo de cartão com dias: 400 em vez de 500 (`chk_cartao_credito`); na troca, **recusar**, não limpar em silêncio | **A** |
| 035 | P-002-4 | `cor_hex` (`#RRGGBB`) e `icone` (≤ 50) validados: 400 em vez de 500 no tamanho | **A** (comprimento e formato; o modal só envia 10 cores fixas válidas) |
| 036 | P-002-5 | Cartão exige `dia_fechamento` e `dia_vencimento` (hoje aceita sem e fica sem fatura) | **A** |
| 037 | P-002-6 | Excluir conta com **agendamentos ativos** vira 400; ao excluir, zera `usuarios.conta_padrao_*` que a referenciem | **A** |
| 038 | P-002-7 | `GET` passa a devolver também `nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento` (só acréscimo; `POST/PUT` não mudam) | **A** (junto com G-8) |
| 039 | P-002-8 | Contas inativas deixam de ser usáveis por outras features (bot, transações): 004, 011 e 012 filtram `ativa = true` | **A** (decidido nas specs delas) |
| — | P-002-9 | `limite_credito: 0` sai como `0.0` em vez de `null` | **R** (sem ganho; nenhum cliente distingue) |

### 2.3 Perguntas da 002

| # | Pergunta | Padrão da spec / minha sugestão |
|---|---|---|
| 1 | Quem além do Angular chama `/api/contas`? (bot, atalhos) | Só o Angular. **Confirme se o bot ou os atalhos usam essas rotas.** |
| 2 | `NaN`/`Infinity` como literais JSON: o Python aceita, o .NET responde 400 | Aceitar a diferença |
| 3 | Collation de produção (ordem do `GET`) | Rode o script do §5; só importa se houver nomes com acento ou caixa mista |
| 4 | Teste de contrato: `100` × `100.0` | Comparação semântica (nada a decidir) |

---

## 3. Feature 003 — Categorias (2 desvios propostos)

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| 040 | P-003-1 | `Receita` passa a incluir a macro `Receitas Gerais` (hoje o `Outros` de renda, fallback do bot, nunca é oferecido ao lançar receita); `Despesa` deixa de listá-la. `Receita` 4 → 5 macros; `Despesa` 14 → 13 | **A** |
| — | P-003-2 | Esconder na API o `Outros` global duplicado (desvio 030) | **R**: resolver no dado depois (você escolhe qual manter e reaponta), não esconder na API. Até lá o dropdown mostra dois `Outros`, como hoje. |

Decisões G-2, G-3 e G-4 acima completam a 003. Perguntas da 003: collation e conferência do seed estão no §5. O 503 de "Banco de dados não configurado" não é reproduzido (sem `DATABASE_URL` o app nem sobe).

---

## 4. Feature 008 — API keys e LGPD (9 desvios propostos)

Contexto: o Flask quebra em parte das rotas LGPD (colunas inexistentes, desvio 028): `POST /lgpd/consentimento` e `GET /lgpd/exportar` dão 500; `consentimentos` devolve lista vazia e `verificar-consentimento` devolve `false` porque o erro é engolido; `deletar-conta` diz que apagou tudo mas só apaga 6 tabelas e **não apaga o usuário**.

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| 041 | P-008-1 | Consentimento passa a poder ser atualizado/revogado (`ON CONFLICT DO UPDATE`); hoje o 2º registro do mesmo tipo e versão dá 500 e `revogado_em` nunca é gravado | **A** |
| 042 | P-008-2 | Chave duplicada: 409 em vez de 500 | **A** |
| 043 | P-008-3 | Usuário inexistente (FK): 404 em vez de 500 | **A** |
| 044 | P-008-4 | `deletar-conta` apaga de fato (transações, agendamentos, contas, potes, categorias do usuário e a linha de `usuarios`, em ordem explícita por causa dos `RESTRICT`); usuário inexistente = 404 | **A\*** só com **backup antes da virada** e com a G-6 em B (rota desligada em produção). Hoje é inofensiva; passaria a ser irreversível. |
| 045 | P-008-5 | `usuario_id` **obrigatório** em `PUT/DELETE /chave/<id>` (implementa o dono exigido pelo desvio 006; hoje o caminho nem tem usuário) | **A** |
| — | P-008-6 | `exportar` devolve chaves em claro; propõe `?mascarar=true` | **R** (manter até haver cliente real; o risco é o segredo único, tratado na G-6) |
| 046 | P-008-7 | Limite do plano soma só chamadas com chave do **sistema** (hoje soma também as da chave própria, contra a mensagem "sem limites") | **A** (nenhum usuário tem plano hoje) |
| 047 | P-008-8 | Gravar o log de acesso de chaves (hoje nada grava em `logacessochaveapi`, que fica sempre vazia) | **A** (auditoria LGPD) |
| 048 | P-008-9 | Tipos JSON inesperados (`"true"`, `usuario_id: "5"`) viram 400 `"Dados incompletos"` | **A** |

### Perguntas da 008

| # | Pergunta | Resposta / sugestão |
|---|---|---|
| 1 | Exposição externa | Ver **G-6** |
| 2 | Dono da chave em `PUT/DELETE /chave/<id>` | `usuario_id` obrigatório (P-008-5) |
| 3 | Texto fixo dos 500 | `<título>. Tente novamente.` (G-7) |
| 4 | `SHOW timezone` da produção | **Já respondido:** `UTC` (consulta 1 da inspeção). A spec assume `+00:00`: correto. |
| 5 | `deletar-conta`: revogar tokens do Google e apagar chaves Redis do usuário? | **Sim.** Os tokens do Google caem sozinhos pela FK `CASCADE` ao apagar o usuário; as chaves Redis (`pending_*`, `nightly_checkin*`) são apagadas pelo código. |
| 6 | `assinatura.ativo` no export derivado de `status` e `data_fim` | **Confirmar:** `ativo = (status = 'ativo' E (data_fim é nulo ou futura))` |
| 7 | Token com formato Fernet que não decifra: falhar de forma explícita em vez de devolver o texto cifrado como chave | **A** (devolver cifrado como se fosse chave é erro silencioso) |

---

## 5. O que depende de consulta no banco

Rode `scripts/homol/heidisql/05-conferencia-onda-1.sql` conectado em `bot_whatsapp_prod` (só `SELECT`) e me mande os resultados:
- **collation** do banco (002 e 003, ordem dos nomes);
- **conferência do seed** (003): esperado 5 / 18 / 61, nenhuma categoria de usuário, macro 17 = `Despesas Gerais`;
- **nome real da UNIQUE** de `contas` (002 supõe `contas_usuario_id_nome_conta_key`);
- **dados de teste das contas** (002): quantas contas inativas e se existe alguma não-cartão com dias.

---

## 6. Resposta rápida

> **"Aprovo as recomendações"** equivale a: G-1 a G-4, G-6 (B), G-7 e G-8 aprovados; G-5 rejeitado (sem HEAD); desvios 032 a 048 aprovados (048 incluso; 044 com a condição acima); P-002-9, P-003-2 e P-008-6 rejeitados; 008-Q5, Q6 e Q7 aprovadas como estão escritas.

Depois da sua resposta eu: (1) crio os arquivos de desvio 032–048 com o estado decidido, (2) ajusto as três specs ao que ficou, (3) abro a implementação em paralelo, começando pelos PRs de apoio (G-4 e a 008-0) e liberando 002, 003 e 008a/008b em seguida.
