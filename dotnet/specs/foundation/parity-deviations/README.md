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
| [005](005-whitelist-rotas-inalcancaveis.md) | Rotas registradas porém bloqueadas pela whitelist | compatibilidade | 008, 009, 011, 014 | implementado |
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
| [016](016-nome-de-conta-duplicado.md) | Nome de conta duplicado devolve 500 | correção | 002 | implementado |
| [017](017-api-key-texto-puro.md) | api_key_automate em texto puro e coluna pequena demais para Fernet | segurança | 001, 011 | implementado |
| [018](018-enumeracao-de-usuarios.md) | Login distingue usuário inexistente de senha errada | segurança | 001 | aprovado |
| [019](019-validacao-chave-weather.md) | Validação de chave weather usa outro provedor | correção | 007, 010 | implementado |
| [020](020-relatorio-mensal-porta.md) | Relatórios mensais nunca rodam | operação | 013 | aprovado |
| [021](021-oauth-state-assinado.md) | State do OAuth sem assinatura | segurança | 010 | implementado |
| [022](022-weather-https.md) | WeatherAPI por HTTP sem TLS | segurança | 010 | implementado |
| [023](023-rate-limit-proxy.md) | Rate limit de 100/hora por IP do proxy | operação | 009 | aprovado |
| [024](024-corpo-sem-content-type.md) | Corpo sem Content-Type JSON gera 500 | correção | todas | aprovado |
| [025](025-cartao-sem-acento.md) | Tipo 'Cartao de Credito' sem acento no handler do Automate | correção | 011 | implementado |
| [026](026-url-bot-grafico.md) | URL do bot no intent de gráfico | correção | 012 | aprovado |
| [027](027-colunas-ausentes-transacoes.md) | POST/PUT /api/transactions gravam colunas que não existem | correção | 004 | aprovado |
| [028](028-caminhos-com-colunas-ausentes.md) | Outros caminhos que usam colunas ou tabelas inexistentes | correção | 007, 008, 009, 013 | aprovado |
| [029](029-tokens-google-texto-puro.md) | Tokens do Google guardados em texto puro | segurança | 010, 014 | implementado |
| [030](030-subcategoria-global-duplicada.md) | Subcategoria global duplicada: Outros (macro 17) | correção | 003, 004, 011 | aprovado |
| [031](031-validacoes-do-cadastro.md) | Validações do cadastro: WhatsApp sem dígitos, dias não inteiros e cadastro concorrente | correção | 001 | implementado |
| [032](032-reativar-conta-inativa.md) | Reativar conta inativa de mesmo nome | correção | 002 | implementado |
| [033](033-tipos-json-e-numeros-extremos-contas.md) | Contas: tipo JSON errado e números extremos | correção | 002 | implementado |
| [034](034-dias-fora-de-cartao.md) | Contas: dias e limite fora de cartão e troca de tipo | correção | 002 | implementado |
| [035](035-validar-cor-e-icone.md) | Contas: validar cor_hex e icone | correção | 002 | implementado |
| [036](036-cartao-exige-dias.md) | Contas: cartão de crédito exige os dois dias | correção | 002 | implementado |
| [037](037-remocao-conta-agendamentos-e-padrao.md) | Contas: remoção considera agendamentos ativos e conta padrão | correção | 002 | implementado |
| [038](038-contas-get-campos-extras.md) | Contas: GET devolve também os campos que o Angular espera | compatibilidade | 002 | implementado |
| [039](039-contas-inativas-em-outras-features.md) | Contas inativas deixam de ser usadas por outras features | correção | 004, 011, 012 | aprovado |
| [040](040-categorias-receitas-gerais.md) | Categorias: Receita inclui Receitas Gerais, Despesa não | correção | 003 | implementado |
| [041](041-consentimento-upsert.md) | LGPD: consentimento pode ser atualizado e revogado | correção | 008 | implementado |
| [042](042-chave-api-duplicada-409.md) | API keys: chave duplicada responde 409 | correção | 008 | implementado |
| [043](043-usuario-inexistente-404.md) | API keys e LGPD: usuário inexistente responde 404 | correção | 008 | implementado |
| [044](044-deletar-conta-completo.md) | LGPD: deletar-conta apaga de fato | segurança | 008 | implementado |
| [045](045-chave-usuario-id-obrigatorio.md) | API keys: usuario_id obrigatório em PUT/DELETE /chave/<id> | segurança | 008 | implementado |
| [046](046-limite-plano-so-chave-sistema.md) | API keys: limite do plano conta só chamadas com chave do sistema | correção | 008 | implementado |
| [047](047-gravar-log-acesso-chaves.md) | API keys: gravar o log de acesso | correção | 008 | implementado |
| [048](048-tipos-json-api-keys.md) | API keys: tipos JSON inesperados viram 400 | correção | 008 | implementado |
| [049](049-limite-credito-zero.md) | Contas: limite_credito zero respondido como null | compatibilidade | 002 | rejeitado |
| [050](050-esconder-outros-duplicado.md) | Categorias: esconder o Outros global duplicado na API | correção | 003 | rejeitado |
| [051](051-exportar-mascarar-chaves.md) | LGPD: mascarar chaves no exportar | segurança | 008 | rejeitado |
| [052](052-api-keys-desligadas-por-padrao.md) | Rotas /api-keys/* desligadas por padrão em produção | segurança | 008, 009 | implementado |
| [053](053-connect-calendar-link-assinado.md) | Google: link de conexão assinado (/connect-calendar) | segurança | 010, 012 | implementado |
| [054](054-clima-traducao-e-chuva.md) | Clima: tradução em português e chance de chuva | correção | 010 | implementado |
| [055](055-rotas-limite-diario-data.md) | OpenRoute: data de São Paulo e incremento atômico no limite diário | correção | 010, 012 | implementado |
| [056](056-travel-time-codigo-morto.md) | OpenRoute: não portar o código morto e quebrado do travel_time_service | correção | 010 | implementado |
| [057](057-calendar-evento-virada-do-dia.md) | Calendar: evento na virada do dia e atualização de data | correção | 010 | implementado |
| [058](058-google-refresh-com-needs-reconnect.md) | Google: não renovar token quando needs_reconnect está ligado | correção | 010, 014 | implementado |
| [059](059-bot-texto-acima-de-4096.md) | Bot: dividir mensagens com mais de 4096 caracteres | correção | 010, 012, 013 | implementado |
| [060](060-oauth-nome-com-escape-html.md) | OAuth: escapar o nome do usuário na página de sucesso | segurança | 010 | implementado |
| [061](061-drive-escape-barra-invertida.md) | Drive: escapar a barra invertida na busca de pasta | correção | 010 | implementado |
| [062](062-prompt-evento-com-data-de-hoje.md) | Gemini: prompt de criação de evento recebe a data de hoje | correção | 010, 012 | aprovado |
| [063](063-gemini-chamadas-com-usuario.md) | Gemini: três chamadas passam a usar o usuário e a chave dele | correção | 010, 006 | aprovado |
| [064](064-openroute-retry-so-em-5xx.md) | OpenRoute: repetir só em timeout, rede e 5xx | correção | 010 | implementado |
| [065](065-vencimento-fatura-compra-apos-fechamento.md) | Vencimento da fatura de compra após o fechamento | correção | 004 | aprovado |
| [066](066-transacoes-entradas-invalidas-400.md) | Transações: entradas inválidas viram 400 | correção | 004 | aprovado |
| [067](067-transacoes-get-campos-adicionais.md) | GET /api/transactions devolve campos que o Angular lê | compatibilidade | 004 | aprovado |
| [068](068-transacoes-put-transferencia-pagamento.md) | PUT em Transferência ou Pagamento Fatura passa a dar 400 | correção | 004 | aprovado |
| [069](069-transacoes-put-recalcula-fatura.md) | PUT recalcula fatura_id | correção | 004 | aprovado |
| [070](070-transacoes-delete-pagamento-reabre-fatura.md) | Apagar Pagamento Fatura reabre a fatura | correção | 004 | aprovado |
| [071](071-pagamento-fatura-exige-cartao.md) | Pagamento de fatura exige conta de cartão | correção | 004 | aprovado |
| [072](072-contas-mensais-entradas-invalidas.md) | Contas mensais: entradas inválidas viram 400 ou são ignoradas | correção | 005 | aprovado |
| [073](073-contas-mensais-valida-subcategoria.md) | Contas mensais: subcategoria validada | segurança | 005 | aprovado |
| [074](074-contas-mensais-put-consistencia-checks.md) | PUT confere o estado final contra os CHECK | correção | 005 | aprovado |
| [075](075-receitas-gerais-conta-como-receita.md) | Receitas Gerais conta como receita | correção | 005 | aprovado |
| [076](076-receita-fora-da-reserva.md) | Receita fora da reserva de emergência | correção | 005 | aprovado |
| [077](077-gemini-cache-clear-corpo-vazio.md) | gemini-cache-clear com corpo vazio limpa tudo | correção | 009 | aprovado |
| [078](078-admin-valida-ip.md) | Validar e normalizar IP em blacklist e unblock | segurança | 009 | aprovado |
| [079](079-admin-tipos-estritos.md) | Tipos estritos no corpo das rotas admin | correção | 009 | aprovado |
| [080](080-admin-notificacao-usuario-inexistente.md) | Usuário inexistente na configuração de notificação: 404 | correção | 009 | aprovado |
| [081](081-rate-limit-valores.md) | Valores do rate limit (desligado por padrão) | segurança | 009 | implementado |
| [082](082-ip-cliente-forwardedheaders.md) | IP do cliente pelo ForwardedHeaders | segurança | 009 | implementado |
| [083](083-admin-api-key-errada-suspeita.md) | x-api-key errada em /admin conta como tentativa suspeita | segurança | 009 | aprovado |
| [084](084-configuracoes-tipos-json-400.md) | Configurações: tipos JSON inesperados dão 400 | correção | 007 | implementado |
| [085](085-perfil-validacoes.md) | Perfil: e-mail único, limites, fuso e horários válidos | correção | 007 | implementado |
| [086](086-endereco-preserva-coordenadas.md) | Endereço preserva coordenadas quando o texto não muda | correção | 007 | implementado |
| [087](087-endereco-faixa-coordenadas.md) | Faixa de latitude e longitude | correção | 007 | implementado |
| [088](088-api-keys-has-key-sem-preferencia.md) | has_key reflete chave sem linha de preferência | correção | 007 | implementado |
| [089](089-dashboard-recentes-renda-vs-receita.md) | Dashboard: tipo Renda x Receita nas transações recentes | compatibilidade | 006 | proposto |
| [090](090-dashboard-janela-gastos-mensais.md) | Dashboard: janela dos gastos mensais começa no meio de um mês | correção | 006 | proposto |
| [091](091-dashboard-ordem-gastos-mensais.md) | Dashboard: gastos mensais em ordem decrescente de mês | correção | 006 | proposto |
| [092](092-dashboard-top-categorias-invertido.md) | Dashboard: top 10 de categorias devolve as de menor gasto | correção | 006 | proposto |
| [093](093-dashboard-alertas-data-inicio.md) | Dashboard: alertas MENSAL e ANUAL ignoram data_inicio | correção | 006 | proposto |
| [094](094-dashboard-alertas-conta-ja-paga.md) | Dashboard: alerta continua para a conta já paga no mês | correção | 006 | proposto |
| [095](095-dashboard-alertas-receita.md) | Dashboard: receita aparece como conta a vencer | correção | 006 | proposto |
| [096](096-sms-chamada-inutil-gemini.md) | SMS: remover a chamada inútil ao Gemini | correção | 011 | proposto |
| [097](097-automacao-entradas-invalidas-400.md) | Rotas de automação: entradas inválidas viram 400 com mensagem | correção | 011 | proposto |
| [098](098-sms-valor-pago-texto.md) | SMS: valor_pago como texto numérico deve funcionar | correção | 011 | proposto |
| [099](099-quitacao-casamento-sem-caixa.md) | Quitação por SMS: casar descrições sem diferenciar a caixa | correção | 011, 012 | proposto |
| [100](100-confirmacao-emoji-cartao.md) | Confirmação de transação: restaurar o emoji do cartão | correção | 011, 012 | proposto |
| [101](101-reprocessar-isola-erro-por-agendamento.md) | Reprocessar fatura: isolar o erro de cada agendamento | correção | 011 | proposto |
| [102](102-chave-usuario-indice-cego.md) | Chave do atalho: índice cego e cabeçalho | segurança | 011 | proposto |
| [120](120-calendar-alerts-tipos-do-corpo.md) | Alertas de calendário: tipos do corpo de /config e /ativar | correção | 014 | proposto |
| [121](121-calendar-alerts-janela-30s.md) | Alertas de calendário: janela de ±30 s perde o aviso se o ciclo atrasa | correção | 014 | proposto |
| [122](122-calendar-alerts-usuario-inativo.md) | Alertas de calendário: usuário inativo ainda recebe alertas | correção | 014 | proposto |
| [130](130-checkin-fatura-vencida-derruba-mensagem.md) | Check-in: fatura vencida derruba a mensagem do usuário | correção | 013, 012 | proposto |
| [131](131-faturas-fechadas-fora-dos-alertas.md) | Faturas `Fechada` ficam fora dos alertas de vencimento | correção | 013, 012 | proposto |
| [132](132-relatorio-mensal-sinal-das-despesas.md) | Relatório mensal: sinal das despesas | correção | 013, 009 | proposto |
| [133](133-relatorio-mensal-formato-numerico.md) | Relatório mensal: formato numérico em inglês | correção | 013 | proposto |
| [134](134-motor-dia-29-a-31-em-mes-curto.md) | Motor: agendamento de dia 29 a 31 não dispara em mês curto | correção | 013, 005 | proposto |
| [135](135-populador-lembrete-variavel-vira-despesa.md) | Populador de fatura lança `LEMBRETE_VARIAVEL` como despesa | correção | 013, 004, 011 | proposto |
| [136](136-horario-com-minutos-nunca-dispara.md) | Horário com minutos nunca dispara (resumo matinal e check-in) | correção | 013, 007 | proposto |
| [137](137-resumo-sem-google-suprime-alertas-financeiros.md) | Resumo matinal sem Google suprime os alertas financeiros | correção | 013 | proposto |
| [138](138-resumo-matinal-chave-do-usuario.md) | Resumo matinal usa a chave do usuário | correção | 013, 008, 010 | proposto |
| [139](139-checkin-nao-confere-envio.md) | Check-in não confere o envio e deixa a sessão ativa | correção | 013, 012 | proposto |
| [150](150-whatsapp-confirmacao-sem-descricao.md) | Confirmar despesa/renda por WhatsApp falha quando não há agendamento | correção | 012 | proposto |
| [151](151-whatsapp-transferencia-quebrada.md) | Transferência por WhatsApp falha sempre | correção | 012 | proposto |
| [152](152-whatsapp-notificacoes-gemini-500.md) | Configurar Notificações pelo ramo do Gemini grava e responde 500 | correção | 012 | proposto |
| [153](153-whatsapp-api-key-cifrada.md) | Solicitar API Key devolve o valor cru da coluna | correção | 012 | proposto |
| [154](154-whatsapp-extracao-sem-parcelamento.md) | A extração de despesa e renda não pede data, conta, categoria nem parcelamento | correção | 012 | proposto |
| [155](155-whatsapp-evento-pendente-mais-recente.md) | Evento pendente "mais recente" depende da ordem do KEYS | correção | 012 | proposto |
| [156](156-whatsapp-origem-rota-sem-tratamento.md) | Resposta casa/trabalho/outro à pergunta de origem da rota não é tratada | correção | 012 | proposto |
| [157](157-whatsapp-pagamento-fatura-inalcancavel.md) | Pagamento Fatura é inalcançável com "paguei..." | compatibilidade | 012 | proposto |
| [158](158-whatsapp-analise-previsao-valores-negativos-e-erros.md) | Análise e previsão: valores negativos, desempate aleatório e KeyError sem histórico | correção | 012 | proposto |
| [159](159-whatsapp-drive-erro-especifico.md) | Upload ao Drive: o motivo específico da falha nunca é mostrado | correção | 012 | proposto |
| [160](160-whatsapp-quitar-conta-fixa-sem-handler.md) | Quitar Conta Fixa está no prompt do Gemini mas não tem ramo | correção | 012 | proposto |
| [161](161-whatsapp-vencimentos-sem-filtro-de-usuario.md) | Vencimentos comparam a descrição sem filtrar o usuário | correção | 012 | proposto |
| [170](170-whatsapp-feedback-pote-so-uma-subcategoria.md) | Feedback da transação soma só a subcategoria da transação no pote | correção | 012 | proposto |
| [190](190-whatsapp-desativar-notificacao-vira-ativar.md) | Desativar resumo matinal ou check-in por WhatsApp ativa | correção | 012 | proposto |
| [191](191-whatsapp-notificacoes-contas-vencer-metodo-inexistente.md) | Configurar Notificações (Gemini, contas_vencer) chama método que não existe | correção | 012 | proposto |
| [192](192-whatsapp-deletar-evento-quando-qualquer-valor-vira-amanha.md) | Deletar Evento: qualquer valor de `quando` diferente de `hoje` vira amanhã | correção | 012 | proposto |
| [210](210-whatsapp-checkin-resposta-por-trecho.md) | Resposta ao check-in procura "ok"/"sim"/"não" como trecho de qualquer palavra | correção | 012, 013 | proposto |
| [230](230-whatsapp-grafico-periodo-sem-validacao.md) | Gráfico de Gastos: período sem validação do tipo e da faixa | correção | 012 | proposto |
| [231](231-whatsapp-grafico-saldo-e-filtros.md) | Gráficos: saldo da linha ignora o histórico anterior ao período e filtros inconsistentes | correção | 012 | proposto |
| [232](232-whatsapp-comparacao-mensal-sinal-e-formato.md) | Comparação Mensal: totais negativos, variação invertida e números em formato inglês | correção | 012 | proposto |
| [233](233-whatsapp-analise-maiores-gastos-invertidos.md) | Análise Inteligente: "Maiores gastos" lista os menores e os dias da semana saem em inglês | correção | 012 | proposto |
