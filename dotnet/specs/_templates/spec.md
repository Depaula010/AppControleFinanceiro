# Spec NNN — <Nome da feature>

- **Estado:** rascunho | em revisão | aprovada
- **Onda:** N
- **Origem (Python):** `arquivo:linha`, ...
- **Depende de:** NNN, NNN

## 1. Objetivo
Uma ou duas frases: o que o usuário/cliente consegue fazer.

## 2. Rotas
| Método | Caminho | Auth | Envelope |
|---|---|---|---|

## 3. Contrato por rota
Para cada rota: corpo de entrada (campos, tipos, obrigatoriedade), validações com a **mensagem exata**, respostas de sucesso e de erro (status + corpo), efeitos no banco.

## 4. Regras de negócio
Numeradas (`RN-NNN-01`...), cada uma com `origem: arquivo:linha` e, se aplicável, referência a `business-rules.md`.

## 5. Critérios de aceite
`AC-NNN-01` — **Dado** ... **Quando** ... **Então** ...
Cada critério vira pelo menos um teste.

## 6. Tabelas e dados tocados
Leitura / escrita, constraints relevantes, tenant (`usuario_id`).

## 7. Desvios de paridade
Lista dos desvios aplicáveis (`parity-deviations/NNN`) e seu estado. Sem `aprovado`, reproduzir o Flask.

## 8. Fora de escopo

## 9. Perguntas em aberto
