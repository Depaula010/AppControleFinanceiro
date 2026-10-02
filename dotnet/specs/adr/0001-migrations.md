# ADR 0001 — Como o schema do banco é alterado

- **Data:** 2026-10-02
- **Estado:** proposto

## Contexto

O banco `bot_whatsapp_prod` é compartilhado com o bot Node e foi criado à mão (a baseline do Alembic é vazia). O Flask altera schema por rotas HTTP `GET /admin/setup-*` (desvio 002). O .NET precisa de algumas mudanças aditivas já na virada: `api_key_automate` mais larga (desvio 017) e `transacoes.observacoes`/`origem` (desvio 027). A constituição (§3) exige migrations versionadas, aditivas, e nunca DDL em rota HTTP.

## Decisão

1. Mudanças de schema são **arquivos SQL idempotentes e versionados** em `dotnet/migrations/NNNN_descricao.sql` (`IF NOT EXISTS`, `ALTER ... TYPE` só para alargar). Só mudanças **aditivas** ou de alargamento; nunca `DROP`, `RENAME` ou estreitamento.
2. Um runner aplica os pendentes e registra em uma tabela própria `dotnet_migrations (versao text primary key, aplicada_em timestamptz)`. A tabela `alembic_version` não é tocada.
3. A aplicação **não** executa DDL no boot. O runner roda (a) nos testes de integração, sobre o container, (b) na homol, manualmente, e (c) em produção por um passo explícito do workflow **antes** do `docker compose up`, com aprovação do Rafael na virada.
4. O `schema-prod.sql` descreve o estado da produção hoje; cada migration nova também é refletida num `schema-atual.sql` gerado para os testes (ou aplicada sobre o `schema-prod.sql` no container de teste).

## Alternativas

- **EF Core Migrations**: exige o `AppDbContext` completo para gerar migrations coerentes e o banco já existe sem histórico; mais cerimônia que o benefício, e conflita com o uso de Dapper.
- **Migrar no boot da aplicação**: arriscado num banco compartilhado e em deploy com troca de container.

## Consequências

- O runner é uma pequena classe de Infrastructure (lê a pasta, ordena, aplica em transação, registra).
- Cada migration precisa de uma nota no `cutover-runbook.md` se for necessária antes da virada.
- Perde-se a geração automática do EF; ganha-se controle explícito e idempotência.
