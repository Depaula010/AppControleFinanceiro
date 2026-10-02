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
