# Desvios de paridade

Cada arquivo é um comportamento do Flask que o .NET **não** reproduz literalmente (bug, furo de segurança ou inconsistência). Sem estado `aprovado`, o .NET copia o comportamento atual. Use `../../_templates/deviation.md` para novos.

Estados: `proposto` → `aprovado` | `rejeitado` → `implementado`. Em 2026-10-02 o Rafael aprovou todos em bloco; as **condições** abaixo continuam valendo e precisam ser verificadas antes de implementar o desvio correspondente.

Os desvios 002, 003, 005, 006 e 008 alteram o que está exposto/autenticado e precisam de atenção especial: dependem de saber quem chama cada rota hoje (Angular, bot, scripts).

| # | Título | Severidade | Features | Estado |
|---|---|---|---|---|
| [001](001-receita-vs-renda.md) | Receita x Renda em transações | correção | 004, 006, 011, 012 | aprovado |
| [002](002-admin-setup-sem-auth.md) | Rotas /admin/setup-* sem autenticação e com DDL via GET | segurança | 009 | aprovado |
| [003](003-rotas-sem-auth.md) | Rotas de escrita sem autenticação | segurança | 009, 011, 014 | aprovado |
| [004](004-rotas-que-falham.md) | Rotas que hoje respondem 500 | correção | 009, 014 | aprovado |
| [005](005-whitelist-rotas-inalcancaveis.md) | Rotas registradas porém bloqueadas pela whitelist | compatibilidade | 008, 009, 011, 014 | aprovado |
| [006](006-api-keys-idor-segredo-unico.md) | IDOR em /api-keys e segredo único para JWT e admin | segurança | 001, 008, 009 | aprovado |
| [007](007-saldo-consolidado.md) | Dois cálculos de saldo | correção | 002, 006 | aprovado |
| [008](008-vazamento-erros-e-logs.md) | Vazamento de str(e) e de segredos em log | segurança | todas | aprovado |
| [009](009-dia-da-semana-locale.md) | Dia da semana depende do lc_time do banco | correção | 006 | aprovado |
| [010](010-pagamento-de-fatura.md) | Qual fatura é paga | correção | 004 | aprovado |
| [011](011-parcelas-unificadas.md) | Incremento de parcelas em três lugares | correção | 004, 005, 013 | aprovado |
| [012](012-ja-pago-no-mes.md) | Detecção de pagamento do mês por descrição | correção | 004, 005, 013 | aprovado |
| [013](013-motor-agendamentos.md) | Motor de agendamentos sem idempotência e lembrete variável | correção | 009, 013 | aprovado |
| [014](014-reserva-unica.md) | Reserva de emergência calculada em três lugares | correção | 005, 011, 012 | aprovado |
| [015](015-periodo-do-pote.md) | Três definições de período de pote | correção | 004, 013 | aprovado |
| [016](016-nome-de-conta-duplicado.md) | Nome de conta duplicado devolve 500 | correção | 002 | aprovado |
| [017](017-api-key-texto-puro.md) | api_key_automate em texto puro e coluna pequena demais para Fernet | segurança | 001, 011 | implementado |
| [018](018-enumeracao-de-usuarios.md) | Login distingue usuário inexistente de senha errada | segurança | 001 | aprovado |
| [019](019-validacao-chave-weather.md) | Validação de chave weather usa outro provedor | correção | 007, 010 | aprovado |
| [020](020-relatorio-mensal-porta.md) | Relatórios mensais nunca rodam | operação | 013 | aprovado |
| [021](021-oauth-state-assinado.md) | State do OAuth sem assinatura | segurança | 010 | aprovado |
| [022](022-weather-https.md) | WeatherAPI por HTTP sem TLS | segurança | 010 | aprovado |
| [023](023-rate-limit-proxy.md) | Rate limit de 100/hora por IP do proxy | operação | 009 | aprovado |
| [024](024-corpo-sem-content-type.md) | Corpo sem Content-Type JSON gera 500 | correção | todas | aprovado |
| [025](025-cartao-sem-acento.md) | Tipo 'Cartao de Credito' sem acento no handler do Automate | correção | 011 | aprovado |
| [026](026-url-bot-grafico.md) | URL do bot no intent de gráfico | correção | 012 | aprovado |
| [027](027-colunas-ausentes-transacoes.md) | POST/PUT /api/transactions gravam colunas que não existem | correção | 004 | aprovado |
| [028](028-caminhos-com-colunas-ausentes.md) | Outros caminhos que usam colunas ou tabelas inexistentes | correção | 007, 008, 009, 013 | aprovado |
| [029](029-tokens-google-texto-puro.md) | Tokens do Google guardados em texto puro | segurança | 010, 014 | aprovado |
| [030](030-subcategoria-global-duplicada.md) | Subcategoria global duplicada: Outros (macro 17) | correção | 003, 004, 011 | aprovado |
| [031](031-validacoes-do-cadastro.md) | Validações do cadastro: WhatsApp sem dígitos, dias não inteiros e cadastro concorrente | correção | 001 | implementado |
| [032](032-reativar-conta-inativa.md) | Reativar conta inativa de mesmo nome | correção | 002 | aprovado |
| [033](033-tipos-json-e-numeros-extremos-contas.md) | Contas: tipo JSON errado e números extremos | correção | 002 | aprovado |
| [034](034-dias-fora-de-cartao.md) | Contas: dias e limite fora de cartão e troca de tipo | correção | 002 | aprovado |
| [035](035-validar-cor-e-icone.md) | Contas: validar cor_hex e icone | correção | 002 | aprovado |
| [036](036-cartao-exige-dias.md) | Contas: cartão de crédito exige os dois dias | correção | 002 | aprovado |
| [037](037-remocao-conta-agendamentos-e-padrao.md) | Contas: remoção considera agendamentos ativos e conta padrão | correção | 002 | aprovado |
| [038](038-contas-get-campos-extras.md) | Contas: GET devolve também os campos que o Angular espera | compatibilidade | 002 | aprovado |
| [039](039-contas-inativas-em-outras-features.md) | Contas inativas deixam de ser usadas por outras features | correção | 004, 011, 012 | aprovado |
| [040](040-categorias-receitas-gerais.md) | Categorias: Receita inclui Receitas Gerais, Despesa não | correção | 003 | aprovado |
| [041](041-consentimento-upsert.md) | LGPD: consentimento pode ser atualizado e revogado | correção | 008 | aprovado |
| [042](042-chave-api-duplicada-409.md) | API keys: chave duplicada responde 409 | correção | 008 | aprovado |
| [043](043-usuario-inexistente-404.md) | API keys e LGPD: usuário inexistente responde 404 | correção | 008 | aprovado |
| [044](044-deletar-conta-completo.md) | LGPD: deletar-conta apaga de fato | segurança | 008 | aprovado |
| [045](045-chave-usuario-id-obrigatorio.md) | API keys: usuario_id obrigatório em PUT/DELETE /chave/<id> | segurança | 008 | aprovado |
| [046](046-limite-plano-so-chave-sistema.md) | API keys: limite do plano conta só chamadas com chave do sistema | correção | 008 | aprovado |
| [047](047-gravar-log-acesso-chaves.md) | API keys: gravar o log de acesso | correção | 008 | aprovado |
| [048](048-tipos-json-api-keys.md) | API keys: tipos JSON inesperados viram 400 | correção | 008 | aprovado |
| [049](049-limite-credito-zero.md) | Contas: limite_credito zero respondido como null | compatibilidade | 002 | rejeitado |
| [050](050-esconder-outros-duplicado.md) | Categorias: esconder o Outros global duplicado na API | correção | 003 | rejeitado |
| [051](051-exportar-mascarar-chaves.md) | LGPD: mascarar chaves no exportar | segurança | 008 | rejeitado |
| [052](052-api-keys-desligadas-por-padrao.md) | Rotas /api-keys/* desligadas por padrão em produção | segurança | 008, 009 | aprovado |
| [053](053-connect-calendar-link-assinado.md) | Google: link de conexão assinado (/connect-calendar) | segurança | 010, 012 | aprovado |
| [054](054-clima-traducao-e-chuva.md) | Clima: tradução em português e chance de chuva | correção | 010 | aprovado |
| [055](055-rotas-limite-diario-data.md) | OpenRoute: data de São Paulo e incremento atômico no limite diário | correção | 010, 012 | aprovado |
| [056](056-travel-time-codigo-morto.md) | OpenRoute: não portar o código morto e quebrado do travel_time_service | correção | 010 | aprovado |
| [057](057-calendar-evento-virada-do-dia.md) | Calendar: evento na virada do dia e atualização de data | correção | 010 | aprovado |
| [058](058-google-refresh-com-needs-reconnect.md) | Google: não renovar token quando needs_reconnect está ligado | correção | 010, 014 | aprovado |
| [059](059-bot-texto-acima-de-4096.md) | Bot: dividir mensagens com mais de 4096 caracteres | correção | 010, 012, 013 | aprovado |
| [060](060-oauth-nome-com-escape-html.md) | OAuth: escapar o nome do usuário na página de sucesso | segurança | 010 | aprovado |
| [061](061-drive-escape-barra-invertida.md) | Drive: escapar a barra invertida na busca de pasta | correção | 010 | aprovado |
| [062](062-prompt-evento-com-data-de-hoje.md) | Gemini: prompt de criação de evento recebe a data de hoje | correção | 010, 012 | aprovado |
| [063](063-gemini-chamadas-com-usuario.md) | Gemini: três chamadas passam a usar o usuário e a chave dele | correção | 010, 006 | aprovado |
| [064](064-openroute-retry-so-em-5xx.md) | OpenRoute: repetir só em timeout, rede e 5xx | correção | 010 | aprovado |
