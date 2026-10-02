# Constituição do projeto

Princípios que valem para toda decisão. Mudar um deles exige um ADR aprovado.

## 1. Compatibilidade antes de elegância
Os clientes (Angular, bot Baileys, atalhos do iPhone e Android) não serão alterados. Rotas, métodos, nomes de campo, tipos JSON, status codes e envelopes de resposta são imutáveis. Os cinco envelopes do Flask (A–E, ver `api-contract.md`) são preservados como estão, inclusive o `sucesso`/`success` misturado.

## 2. Corrigir é decisão explícita
Bugs e furos de segurança do Flask são corrigidos, mas só via `parity-deviations/` e com aprovação. O padrão é reproduzir o comportamento atual.

## 3. O banco existente é intocável sem migration
Mesmo PostgreSQL, mesmas tabelas, compartilhado com o bot Node. Nenhuma coluna é removida ou renomeada. Mudanças de schema são aditivas, vão em migration versionada e nunca em rota HTTP. Dados de produção não são lidos nem alterados em testes.

## 4. Regra de negócio vive no Domain
Cálculo de fatura, parcelas, reserva de emergência, período de pote e sinal do valor ficam em código puro, sem I/O, com teste unitário. Controllers/endpoints só traduzem HTTP.

## 5. Uma definição por regra
Onde o Flask tem versões divergentes da mesma regra (ver `business-rules.md`), a spec escolhe uma, registra a escolha e a justificativa. Código não pode conter duas.

## 6. Segredos
Nunca versionados, nunca logados, nunca devolvidos em erro. Configuração por variáveis de ambiente com os mesmos nomes dos secrets atuais do GitHub.

## 7. Tempo e cultura são dependências
Todo "agora" vem de `TimeProvider`. Fuso `America/Sao_Paulo` e cultura `pt-BR` fixados na aplicação. Testes usam relógio falso.

## 8. Simplicidade
Monólito modular, um container. Sem MediatR, AutoMapper, barramento de mensagens ou abstração sem segundo uso. Biblioteca nova só com justificativa em `tech-stack.md`.

## 9. Testabilidade
Integrações externas (bot, Gemini, Google, clima, rotas) ficam atrás de interfaces. Testes unitários não fazem I/O. Testes de integração usam Postgres em container, nunca o banco de produção.

## 10. Definição de pronto
Uma feature está pronta quando: spec aprovada, critérios de aceite com teste, testes unitários e de integração verdes, teste de contrato sem diferença fora de `parity-deviations/`, cobertura dentro da meta, PR revisado.
