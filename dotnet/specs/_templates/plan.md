# Plan NNN — <Nome da feature>

- **Spec:** `spec.md` (aprovada em AAAA-MM-DD)

## 1. Domain
Entidades, value objects e funções puras novas. Assinaturas.

## 2. Application
Casos de uso (`Nome.ExecuteAsync(...)`), DTOs, validadores, portas novas.

## 3. Infrastructure
Repositórios; para cada acesso: **EF** ou **Dapper**, com o SQL final quando Dapper. Índices ou migrations necessários (somente aditivos).

## 4. Api
Endpoints, filtros de auth, envelope usado por rota, serialização (nomes de campo exatos).

## 5. Testes
| Tipo | O que cobre | Critérios (AC) |
|---|---|---|
| Unitário | | |
| Integração | | |
| Contrato | | |

## 6. Fakes e fixtures
Fakes HTTP, relógio, dados de seed, hashes/tokens gerados pelo Python.

## 7. Riscos e decisões
Pontos delicados e como serão tratados.
