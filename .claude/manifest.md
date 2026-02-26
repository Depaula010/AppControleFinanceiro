# Manifesto de Execução

## Contexto Atual
* **Estado do Projeto:** Em Desenvolvimento (Tela de Configurações)
* **Última Atualização:** 2026-02-26

## Tarefa em Andamento
* **Objetivo:** Implementar endpoints de configurações do usuário (perfil, api-keys, notificações, endereços)
* **Solicitante:** Rafael

## Plano de Execução
- [x] Plano elaborado e aprovado
- [ ] **Backend** — Adicionar endpoints `/api/user/*` em `app/routes/api.py`
  - [ ] `GET /api/user/settings` (aggregated)
  - [ ] `GET|PUT /api/user/profile` (tabela `usuarios`)
  - [ ] `GET|PUT /api/user/api-keys` (tabelas `preferenciaschaveapi` + `chavesapiusuario`, Fernet encrypt)
  - [ ] `GET|PUT /api/user/notifications` (tabela `notificationconfigs`)
  - [ ] `GET|POST /api/user/addresses` + `DELETE /api/user/addresses/<id>` (tabela `enderecosfavoritos`)
- [ ] **Deploy** — Push para produção via GitHub Actions
- [ ] **QA** — Testar endpoints via curl com JWT real

## Status dos Subagentes
| Agente | Status | Última Saída |
| :--- | :--- | :--- |
| Tech Lead | Ativo | Plano de endpoints aprovado |
| Refactor Specialist | Ocioso | - |
| DB Admin | Ocioso | Tabelas verificadas: usuarios, preferenciaschaveapi, chavesapiusuario, notificationconfigs, enderecosfavoritos |
| QA Engineer | Pendente | - |
| Security Auditor | Ocioso | IDOR mitigado: user_id via JWT apenas |
| DevOps Engineer | Ocioso | Nginx: /api/ já coberto, sem alterações necessárias |

## Notas Recentes
* **Sucesso:** `logic.py` eliminado. Lógica migrada para `handlers/`.
* **Atenção:** 6 testes falhando em `tests/unit/test_nightly_checkin_service.py`.
* **2026-02-26:** Iniciada implementação da tela de configurações. Decisão de segurança: endpoints NÃO usam userId na URL (apenas JWT), prevenindo IDOR. Chaves API criptografadas com Fernet (ENCRYPTION_KEY já no ambiente).