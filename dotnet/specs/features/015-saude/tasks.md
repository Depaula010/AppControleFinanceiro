# Tasks 015 — Saúde e Coach (parte B: agenda para o Coach)

Cada tarefa termina com teste verde. **Concluídas em 2026-10-25** (spec aprovada pelo Rafael).

- [x] T1 — Domain: `MascaradorDeTexto` (link, e-mail, telefone, código) — AC-015-07
- [x] T2 — Domain: `NivelDeDetalhe` e `AgendaParaOCoach.Montar` (campos por nível, limites depois do mascaramento, dia inteiro × hora, sem id) — AC-015-04, 08, 09, 12
- [x] T3 — Application: `ConsultarAgendaDoCoach` (validação, Google não conectado, falha, teto de 10 s, nível por calendário) — AC-015-03, 05, 10
- [x] T4 — Infrastructure: `DbCoachAgendaConfig` (`configuracoes`, cache de 60 s, falha segura) — AC-015-05, 06
- [x] T5 — Api: módulo, filtro da chave própria, registro condicional da rota, textos fixos — AC-015-01, 02
- [x] T6 — Integração: 404, 401 (inclui `API_SECRET_KEY`), 200, 503 e contrato das rotas existentes — AC-015-01, 02, 10, 13
- [x] T7 — Log sem conteúdo de evento (teste sobre o provedor de log) — AC-015-11
- [x] T8 — Compose e workflow de deploy (variáveis opcionais) e `docs/INTEGRACAO_COM_O_COACH.md`
