# SDD — Como trabalhamos

Spec-Driven Development: a **spec é a fonte de verdade**; o código é consequência dela. Aqui a spec também registra o comportamento do Flask que precisa ser preservado.

## Ciclo de uma feature

Cada feature vive em `features/NNN-nome/` e passa por cinco etapas, **com aprovação do Rafael entre elas**:

| # | Artefato | Responde | Conteúdo |
|---|---|---|---|
| 1 | `spec.md` | **O quê** | Rotas, payloads, envelopes, regras, critérios de aceite (Dado/Quando/Então), cada regra com `origem: arquivo:linha` do Python. Sem código. |
| 2 | `plan.md` | **Como** | Classes e camadas, tabelas tocadas, queries (EF ou Dapper), fakes necessários, lista de testes. |
| 3 | `tasks.md` | **Em que ordem** | Tarefas pequenas, cada uma terminando com teste verde. |
| 4 | Implementação | — | Teste primeiro, código depois, teste de contrato por último. |
| 5 | Fechamento | — | Desvios registrados, spec ajustada se a implementação revelou algo, PR aberto. |

Se durante a implementação a spec se mostrar errada, **pare, corrija a spec, peça aprovação** e só então continue.

## Regras da spec

- Escreva critérios que um teste consegue verificar. "Deve ser rápido" não é critério.
- Cada critério de aceite vira pelo menos um teste; cite o ID (`AC-001-03`) no nome do teste.
- Registre a origem no Python. Se o comportamento do Flask for um bug, não corrija em silêncio: crie um desvio.
- Campos e mensagens em português são copiados **literalmente** do código atual.

## Desvios de paridade

Um arquivo por desvio em `foundation/parity-deviations/NNN-titulo.md`, usando `_templates/deviation.md`. Estados: `proposto` → `aprovado` | `rejeitado` → `implementado`. Sem `aprovado`, o .NET reproduz o comportamento do Flask.

## Estrutura

```
specs/
  README.md                 este arquivo
  constitution.md           princípios inegociáveis
  foundation/               decisões e contratos que valem para todas as features
  features/NNN-nome/        spec.md, plan.md, tasks.md
  adr/                      decisões pontuais (NNNN-titulo.md)
  _templates/               modelos de spec, plan, tasks, desvio, ADR
```

## Fluxo com o Claude Code

1. "Escreva a spec da feature 002 (Contas) a partir de `api.py:283-727`" → revise → aprove.
2. "Escreva plan e tasks da 002" → revise → aprove.
3. "Implemente a 002" → o agente segue `tasks.md` e abre PR.

Para várias features em paralelo, veja a seção de ondas em `foundation/roadmap.md`.
