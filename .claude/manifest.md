# Manifesto de Execução

## Contexto Atual
* **Estado do Projeto:** Em Desenvolvimento (Dashboard)
* **Última Atualização:** 2026-02-26

## Tarefas Concluídas

### Tela de Configurações (commits `4b96e65`, `28563b9`, `0b473de`)
- [x] Endpoints `/api/user/*` — perfil, api-keys, notificações, endereços
- [x] Whitelist de segurança atualizada para todos os novos endpoints
- [x] Fix: endereços incluídos no endpoint agregado `/api/user/settings`

### Dashboard (commit `914241b`)
- [x] Novo `GET /api/dashboard/alerts` — contas a vencer nos próximos 7 dias
  - Consulta `agendamentos` WHERE `ativo = true`
  - Calcula próxima data de vencimento (este mês ou próximo)
  - Classifica: `danger` (hoje), `warning` (≤2 dias), `info` (3–7 dias)
- [x] `/api/dashboard/alerts` adicionado à VALID_ENDPOINTS

## Endpoints Ativos no Dashboard
| Endpoint | Método | Descrição |
|---|---|---|
| `/api/dashboard/summary` | GET | saldo_total, receitas_mes, despesas_mes, mes_referencia |
| `/api/dashboard/charts?meses=N` | GET | gastos_mensais, gastos_categoria, gastos_dia_semana |
| `/api/dashboard/recent` | GET | Últimas 10 transações |
| `/api/dashboard/alerts` | GET | Contas a vencer nos próximos 7 dias |

## Status dos Subagentes
| Agente | Status | Última Saída |
| :--- | :--- | :--- |
| Tech Lead | Ocioso | Endpoints dashboard completos e na whitelist |
| DB Admin | Ocioso | Query agendamentos com cálculo de vencimento em Python |
| QA Engineer | Ocioso | ng build ✅ sem erros (commit `8717ce4`) |
| Security Auditor | Ocioso | /api/dashboard/alerts na VALID_ENDPOINTS |
| DevOps Engineer | Ocioso | Push feito → GitHub Actions rodando |

## Notas Recentes
* **2026-02-26:** Dashboard atualizado com dados reais. KPIs corrigidos (campos saldo_total/receitas_mes/despesas_mes). Alertas reais de agendamentos. Transações recentes visíveis. Gráfico de barras com seletor 3m/6m/12m. Quick actions com rotas reais.
* **Atenção:** 6 testes falhando em `tests/unit/test_nightly_checkin_service.py` (anterior).
