# CLAUDE.md — Meu Secretário

Este repositório contém o backend **Flask/Python legado** (`app/`) e a **reescrita em .NET 10** (`dotnet/`). A reescrita é guiada por SDD (Spec-Driven Development). Enquanto a virada não acontecer, o Python é a fonte de verdade do comportamento em produção.

## Regras inegociáveis

1. **Nenhuma rota muda.** Método, caminho, nomes de campos JSON, status codes e envelope de resposta devem ser idênticos ao Flask. Angular, bot Baileys e atalho do iPhone já consomem a API.
2. **Spec antes de código.** Não escreva código de uma feature sem `dotnet/specs/features/NNN-nome/spec.md` aprovada. Leia `dotnet/specs/README.md` para o fluxo.
3. **Banco = verdade.** As tabelas reais são minúsculas e sem aspas (`usuarios`, `contas`). Os modelos ORM do Python estão mortos e errados. Consulte `dotnet/specs/foundation/data-model.md`.
4. **Desvio de paridade só com aprovação.** Todo bug corrigido ou comportamento alterado vira um arquivo em `dotnet/specs/foundation/parity-deviations/` e só vale depois de aprovado pelo Rafael.
5. **Teste unitário junto com o código.** Regra financeira sem teste não entra.
6. **Segredos nunca em arquivo versionado**, nem em log, nem em mensagem de erro (o Flask vaza `str(e)` e chaves em log; não reproduzir).
7. **Idioma:** código em inglês; mensagens ao usuário, erros da API e textos de WhatsApp em português, idênticos aos atuais.
8. **Fuso `America/Sao_Paulo` e cultura `pt-BR` explícitos**, nunca dependentes do container. Use `TimeProvider`.

## Comandos (a partir da Fase 1)

```bash
dotnet build dotnet/MeuSecretario.sln
dotnet test dotnet/MeuSecretario.sln
```

## Onde ler

| Assunto | Arquivo |
|---|---|
| Fluxo SDD, como trabalhar | `dotnet/specs/README.md` |
| Princípios | `dotnet/specs/constitution.md` |
| Arquitetura e pastas | `dotnet/specs/foundation/architecture.md` |
| Bibliotecas | `dotnet/specs/foundation/tech-stack.md` |
| Contrato HTTP (110 operações) | `dotnet/specs/foundation/api-contract.md` |
| Esquema do banco | `dotnet/specs/foundation/data-model.md` |
| Regras de negócio | `dotnet/specs/foundation/business-rules.md` |
| Integrações e Redis | `dotnet/specs/foundation/integrations.md` |
| Bugs e desvios | `dotnet/specs/foundation/parity-deviations/` |
| Testes | `dotnet/specs/foundation/testing-strategy.md` |
| Deploy e homol | `dotnet/specs/foundation/infra-deploy.md` |
| Virada | `dotnet/specs/foundation/cutover-runbook.md` |
| Fases e ordem | `dotnet/specs/foundation/roadmap.md` |

## Contexto do legado

- Frontend Angular em `e:/Projetos/Projetos/meusecretariofront/` (proxy `/api/*` para o servidor).
- Segurança: o middleware atual devolve 404 e bloqueia o IP (1h após 5 tentativas) para qualquer caminho fora de uma whitelist. No .NET a whitelist é derivada das rotas registradas.
- Deploy: GitHub Actions → SSH → Docker no Contabo (`/opt/meu-secretario`). Container `meu-secretario-api` na porta 8000 e rede `rede-global` não podem mudar de nome.
