# Estratégia de testes

Estado atual do Python: 170 funções de teste, todas com mocks; nada cobre auth, webhooks, jobs, integrações nem o middleware de segurança; parte está falhando. **Nada disso é referência de cobertura**; é referência de regras (os testes de check-in noturno e de potes descrevem comportamentos reais).

## Pirâmide

### 1. Unitários (a maioria; obrigatórios)
Projeto `MeuSecretario.UnitTests`. Sem I/O, sem relógio real, sem banco.

Alvos prioritários, em ordem:
1. Fatura de cartão: data de fechamento/vencimento, virada de mês, dia inexistente (31 em fevereiro), vencimento anterior ao fechamento.
2. Parcelamento: 1ª parcela imediata, contador, desativação na última, descrição "X (n/total)".
3. Reserva de emergência: multiplicadores por periodicidade.
4. Período do pote (definição única escolhida em `business-rules.md`).
5. Sinal do valor (despesa negativa) e saldo.
6. Hasher Werkzeug (pbkdf2 e scrypt) com hashes gerados pelo Python e commitados como fixtures.
7. Fernet com os vetores oficiais e com um token gerado pelo Python.
8. JWT: claim `user_id`, expiração de 24h, mensagens de erro de cada falha.
9. Validadores de cada rota, com a mensagem exata em português.
10. Normalização de telefone, tipo de pagamento (sem acento), matching fuzzy de conta.

Convenção: um arquivo de teste por classe; nome `Metodo_Cenario_Resultado`; critérios da spec referenciados por ID (`[Trait("AC","002-04")]`).

Metas: **Domain ≥ 90%**, **Application ≥ 80%**, medidas por coverlet no CI. Meta é piso, não objetivo.

### 2. Integração
Projeto `MeuSecretario.IntegrationTests`. `WebApplicationFactory` + Postgres em Testcontainers carregado com `schema-prod.sql` (estrutura real) e as categorias globais. Respawn limpa entre testes. Redis em Testcontainers. Bot, Gemini, Google, clima e rotas substituídos por fakes HTTP.

Cobre: cada endpoint ponta a ponta (status, corpo, efeito no banco), isolamento entre usuários (usuário A nunca vê dados de B), constraints (CHECK de `tipo_transacao`, UNIQUE de conta), idempotência de jobs e do motor de agendamentos.

### 3. Contrato (paridade com o Flask)
- Script `scripts/golden/capture.ps1` roda uma lista de requisições contra o Flask em **homol** e salva status + corpo em `tests/Golden/*.json` (valores voláteis mascarados: ids, datas, tokens).
- O mesmo conjunto roda contra o .NET; a comparação é semântica (nomes de chave, tipos, status, mensagens), com Verify.
- Qualquer diferença só passa se existir um desvio `aprovado` apontando para ela.
- Rotas de webhook são cobertas com os payloads reais do bot, do atalho do iPhone, do Automate e do SMS.

### 4. Manual em homol
Roteiro de frases do WhatsApp em `docs/TESTES_CHATBOT_WHATSAPP.md`, atalho do iPhone, Angular apontando para homol. Ver `cutover-runbook.md`.

## Regras

- Teste de integração nunca aponta para produção. A connection string de produção não existe em nenhum projeto de teste.
- Relógio sempre falso (`FakeTimeProvider`).
- Fakes de HTTP validam o **payload enviado** ao bot e ao Gemini, não só a resposta.
- Teste que falha por flakiness é corrigido ou removido no mesmo PR; não se usa retry.

## CI (GitHub Actions)

Workflow `ci.yml` em todo PR: restore, build com warnings como erro, testes unitários, testes de integração (Docker disponível no runner), cobertura, relatório. O deploy exige este workflow verde.
