# Decisões da onda 2 (specs 004, 005, 007 e 009)

Consolida o que os agentes de spec deixaram para aprovação. Fontes: seções "Desvios propostos" e "Perguntas em aberto" de `features/004-transacoes`, `005-contas-mensais`, `007-configuracoes` e `009-admin-seguranca`.

**Como responder:** "aprovo as recomendações", ou "aprovo as recomendações, exceto X". O que ficar sem resposta **não é implementado**: o .NET reproduz o Flask. Depois da sua resposta eu crio os arquivos de desvio (065 em diante), ajusto as specs e abro a implementação (no máximo 2 agentes ao mesmo tempo).

Legenda: **A** = aprovar, **R** = rejeitar (manter o Flask), **A\*** = aprovar com condição.

---

## 0. Antes de decidir: três coisas para você verificar

| # | O que fazer | Por quê |
|---|---|---|
| V-1 | **No site, tente criar uma transação** (e, se quiser, um cadastro novo) | A spec 004 conclui, pelo código, que `POST/PUT/DELETE /api/transactions` dão 500 hoje (colunas `observacoes`/`origem` ausentes e `conn.begin()` depois de um `SELECT`, que o SQLAlchemy 2.0 rejeita). O mesmo padrão existe em `/auth/register`. Se salvar funcionar hoje, a análise do `begin()` está errada (o resto do contrato vale igual). |
| V-2 | **Rodar `scripts/homol/heidisql/06-conferencia-onda-2.sql`** em produção (só `SELECT`) e me mandar o resultado | Responde Q-004-2, Q-005-3, Q-009-3 e outras. |
| V-3 | **No servidor:** `docker logs meu-secretario-api 2>&1 \| grep -c 429` e `curl -sI http://127.0.0.1:8080/ \| head -20` | Q-009-1 (o rate limit de 100/h realmente barra?) e Q-009-2 (headers reais do Talisman para fixar no .NET). |

---

## 1. Feature 004 — Transações (7 desvios propostos)

Contexto: lançar, editar e excluir transações pelo site não funciona hoje (ver V-1). O contrato de sucesso vem do código e do Angular, nunca foi observado em produção.

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| 065 | P-004-1 | **Vencimento da fatura** quando a compra é depois do fechamento e `V ≥ F` (ex.: fecha dia 10, vence dia 15): hoje cai na fatura de 2 meses à frente em vez de 1; o `ensure_current_invoice_exists` usa outra fórmula, então a fatura "populada" e a da compra podem ser duas diferentes. Passa a existir uma função única | **A** (confira Q-004-2 antes: se o seu cartão tem `V ≥ F`, o efeito é real) |
| 066 | P-004-2 | Entradas absurdas (nulos, `NaN`, descrição > 255) viram 400 com mensagem, em vez de 500 | **A** |
| 067 | P-004-3 | `GET` devolve também `conta_id`, `subcategoria_id`, `observacoes` e os nomes que o Angular lê (`data`, `conta_bancaria`, `conta_bancaria_id`); hoje a lista mostra data e conta vazias | **A** (só acréscimo) |
| 068 | P-004-4 | `PUT` em `Transferência` ou `Pagamento Fatura` passa a dar 400 `"Transferências e pagamentos de fatura não podem ser editados. Exclua e lance novamente."` (hoje desfaz a transferência pela metade) | **A** |
| 069 | P-004-5 | O `PUT` recalcula `fatura_id` quando muda conta, data ou tipo | **A** |
| 070 | P-004-6 | Apagar um `Pagamento Fatura` reabre a fatura (`Fechada` ou `Aberta`) | **A** |
| 071 | P-004-7 | Pagamento de fatura exige conta destino do tipo cartão | **A** |

Perguntas da 004:

| # | Pergunta | Padrão / sugestão |
|---|---|---|
| Q-004-1 | Confirmar o 500 em homol | Ver V-1 |
| Q-004-2 | Cartões e faturas reais | V-2 |
| Q-004-3 | `origem` das transações do bot/Automate/SMS | `null` (paridade) |
| Q-004-4 | Vincular a 1ª parcela ao agendamento | **Não** (paridade) |
| Q-004-5 | Compra em fatura `Fechada`/`Paga`: manter o vínculo como hoje | **Manter** |
| Q-004-6 | De quem é o populador real de faturas (`IInvoiceSchedulePopulator`)? | **013 (jobs)**; a 011 (reprocessar) reutiliza o mesmo serviço |
| Q-004-7 | Sobrecarga com transação ambiente na porta de lançamento | **Não agora** (o Flask também faz commit antes do feedback) |
| Q-004-8 | `valor = 0` aceito | **Manter** (paridade; o front já exige ≥ 0,01) |
| Q-004-9 | Subcategoria: checar só `subcategoria.usuario_id` | **Manter** |

---

## 2. Feature 005 — Contas mensais (5 desvios propostos)

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| 072 | P-005-1 | Entradas que viram 500 ou gravam o texto `"None"` (nulos, tipos errados, `NaN`, datas inválidas, `incluir_na_reserva` como texto) viram 400 ou são ignoradas, conforme a tabela da spec | **A** |
| 073 | P-005-2 | `subcategoria_id` é validado (hoje aceita subcategoria de **outro usuário** e o nome dela vaza na resposta; inexistente dá 500) | **A** |
| 074 | P-005-3 | `PUT` confere o estado **final** contra os `CHECK` (`chk_anual_mes`, `chk_parcelado`); `ANUAL` sem mês no POST dá 400; `mes_execucao`/`total_parcelas` são limpos quando deixam de se aplicar. É o mais importante: o Angular já provoca 500 ao editar `ANUAL`→outra periodicidade e ao sair de `PARCELADO` | **A** |
| 075 | P-005-4 | A macro `Receitas Gerais` conta como **receita** (coerente com o desvio 040) | **A** |
| 076 | P-005-5 | Receita fica fora da reserva de emergência (o front já impede; a API não) | **A** |

Perguntas da 005:

| # | Pergunta | Padrão / sugestão |
|---|---|---|
| Q-005-1 | `incluir_na_reserva` ausente no POST grava `false` | **Manter** (é o que o Flask faz) |
| Q-005-2 | Semântica de `PARCELADO` (API: `parcelas_executadas = 0`; bot: `= 1`) | Confirmar que a 013 executa `total_parcelas − parcelas_executadas` |
| Q-005-3 | `meses_reserva_emergencia` do usuário e agendamentos `PARCELADO` na reserva | V-2 |
| Q-005-4 | Último dígito do resumo pode diferir do Flask (ordem de soma) | **Aceitar** (comparação com tolerância no teste de contrato) |
| Q-005-5 | `descricao` > 255: o Flask corta em silêncio | **Recusar com 400**, igual à 004 (P-004-2), para as duas rotas serem coerentes |

---

## 3. Feature 009 — Admin e segurança (8 desvios propostos)

Divisão proposta pela spec: **009-0** filtro de segurança, **009a** 9 rotas com `x-api-key`, **009b** 13 rotas de setup/diagnóstico, **009c** 8 rotas que dependem das features 013/012 (contrato e portas agora, 503 até haver implementação).

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| 077 | P-009-1 | `gemini-cache-clear` com corpo vazio limpa tudo (hoje dá 500) | **A** |
| 078 | P-009-2 | Validar e normalizar o IP em blacklist/unblock (`IPAddress.TryParse`; 400 `IP inválido`) | **A** |
| 079 | P-009-3 | Tipos estritos no corpo das rotas admin (`ip`/`reason` texto, `usuario_id` inteiro, `ativo` booleano) | **A** |
| 080 | P-009-4 | Usuário inexistente em `get-notification-config`/`config-alertas-financeiros`: 404 em vez de 500 com `str(e)` | **A** |
| 081 | P-009-5 | Limites do rate limit: 300/h geral e 60/h em `/admin`, **desligado por padrão** (`RATE_LIMIT_ENABLED=false`) até você confirmar | **A\*** (ligar só depois do V-3) |
| 082 | P-009-6 | IP do cliente pelo `ForwardedHeaders` da direita para a esquerda (hoje o Flask confia no primeiro IP do `X-Forwarded-For`, que o cliente forja) | **A** |
| 083 | P-009-7 | `x-api-key` errada em `/admin` conta como tentativa suspeita (5 em 10 min ⇒ bloqueio de 1 h) | **A\*** (aviso: quem errar a chave 5 vezes fica bloqueado 1 h; o desbloqueio de emergência é `redis-cli DEL security:blocked:<ip>`) |
| — | P-009-8 | `clear-bot-session` aceitar `session_id` opcional | **R** (manter o literal `'baileys_session'` e logar as linhas apagadas, até a Q-009-3) |

Decisões de desenho (aprovar junto):

| # | Decisão | Recomendação |
|---|---|---|
| G-9 | Whitelist = "tem endpoint registrado"; padrões de URL suspeitos só valem para caminho **sem** endpoint; o User-Agent suspeito vale sempre | **A** |
| G-10 | Fallback de 404 como `UnmatchedRouteMiddleware`, não `MapFallback` (o fallback roubaria o 405) | **A** |
| G-11 | Chamadas internas sem `X-Forwarded-For` passam sem filtro (o bot e os jobs dependem disso, como no Flask) | **A** |
| G-12 | Ordem do pipeline no `Program.cs` (a 009 é a única que edita): `ForwardedHeaders → SecurityHeaders → RateLimiter → SecurityFilter → StrictRouting → UnmatchedRoute`, com teste obrigatório de que o filtro roda depois do routing implícito | **A** |
| G-13 | **Restringir `/admin/` ao que for necessário no nginx nativo**: a superfície admin passa a ser alcançável de fora, protegida só pelo `x-api-key` (mesmo segredo do JWT) | **A** (recomendação operacional sua, fora do código) |

Perguntas da 009:

| # | Pergunta | Padrão / sugestão |
|---|---|---|
| Q-009-1 | O rate limit de 100/h do Flask barra mesmo? | V-3 |
| Q-009-2 | Headers reais do Talisman; o `<script>` inline das páginas OAuth (010d) seria bloqueado pelo CSP? | V-3; a spec tem tarefa para capturar com `curl -I` em homol |
| Q-009-3 | `session_id` reais em `baileys_auth` | V-2 |
| Q-009-4 | Ofelia (`localhost:5000`) e UptimeRobot no cutover | O Ofelia some (o Quartz assume na 013). **Quem chama `trigger-*` de hora em hora hoje?** (UptimeRobot?) |
| Q-009-5 | Padrão de 500 do admin: `Erro ao <ação>. Tente novamente.` | **Sim** (igual à G-7) |
| Q-009-6 | `debug-calendar` aceitar `?usuario_id=` | **Não** (sem pedido) |
| Q-009-7 | `CORS_ENABLED`/`CORS_ORIGINS` ligados no `.env` de produção? | **Verifique no `.env` do servidor**; se sim, abrimos a entrega 009d |
| Q-009-8 | `run-motor-agendamentos`: inline ou em segundo plano? `AlreadyRunning`? | **Inline** (o bot espera 30/60 s, como hoje) e `AlreadyRunning` responde **200** com mensagem, para não mudar o tratamento do bot; detalhe final na 013 |
| Q-009-9 | O desvio 028 diz que `test-notification tipo=agenda` quebra por `agenda_diaria_*`, mas o código não usa essas colunas | Testar em homol antes da 009c e corrigir o desvio |
| Q-009-10 | 33 rotas admin contadas × 31 do cabeçalho | Conferir com `flask routes` em homol |

---

## 4. Feature 007 — Configurações do usuário (6 desvios propostos)

| Novo # | Id | O que muda | Recomendação |
|---|---|---|---|
| — | P-007-1 | `daysBeforeDue` é gravado em `alerta_potes_threshold` (limiar de % do pote) e não faz nada hoje; propõe-se não mudar | **R** (manter a paridade; registrar o risco para a 013) |
| 084 | P-007-2 | Tipos JSON inesperados dão 400 com a mensagem da própria rota, em vez de 500 | **A** |
| 085 | P-007-3 | Perfil: e-mail repetido ⇒ 409 `E-mail já cadastrado`; limites de tamanho; `trim` no nome; fuso IANA válido; validar horários do resumo matinal e do check-in | **A** |
| 086 | P-007-4 | O Angular nunca envia `coordinates`, então toda edição de endereço pelo site **apaga** a latitude e longitude geocodificadas pelo WhatsApp; passa a preservá-las quando o texto do endereço não muda | **A** |
| 087 | P-007-5 | Faixa de latitude e longitude (400 `Coordenadas inválidas`) | **A** |
| 088 | P-007-6 | `has_key` também reflete a chave cadastrada sem linha de preferência (em produção há 3 chaves) | **A** |

Perguntas da 007:

| # | Pergunta | Padrão / sugestão |
|---|---|---|
| Q-007-1 | Repositório próprio para o `PUT /api/user/api-keys` ou estender a 008? | **Próprio** (não mexe em código já entregue) |
| Q-007-2 | Usuário `ativo = false` com JWT válido: perfil 404, o resto responde | **Aceitar** (paridade) |
| Q-007-3 | `GET` de api-keys, notifications e addresses sem cliente conhecido | **Manter** as três (contrato) |
| Q-007-4 | Campos `agenda_diaria_*` no Angular | Já conferido: o front não os usa |

---

## 5. Resposta rápida

> **"Aprovo as recomendações"** equivale a: desvios **065 a 071** (004), **072 a 076** (005), **077 a 083** (009) e **084 a 088** (007) aprovados (081 e 083 com a condição indicada); **P-009-8** e **P-007-1** rejeitados (sem arquivo de desvio: ficam como "manter paridade"); decisões G-9 a G-13 aprovadas; perguntas com "Padrão/sugestão" aceitas como escritas; Q-004-6 = o populador de faturas fica com a 013; Q-005-5 = recusar descrição > 255 com 400 nas duas features.

Depois da sua resposta: crio os arquivos de desvio, atualizo as quatro specs para "aprovada", e a implementação começa em paralelo (máx. 2 agentes), nesta ordem sugerida: **004 e 009-0** (a 004 é a base para 011/012; o filtro de segurança precisa estar pronto antes do admin), depois **005 e 009a**, depois **007 e 009b/009c**.
