# Feature 016 — Tarefas

- [x] T1 — Migration `0110_coach_agenda_chaves` (hash único, índice parcial: uma chave ativa por usuário) — AC-016-01
- [x] T2 — Porta `IChavesDaAgenda` e `DbChavesDaAgenda` (gerar com revogação na mesma transação, resolver, consultar, revogar) — AC-016-01, 03, 04, 05
- [x] T3 — `CoachAgendaApiKey` descobre o dono: chave antiga do ambiente primeiro, depois o banco; falha do banco nega — AC-016-05, 07
- [x] T4 — Rota da agenda sempre registrada (401 sem chave ativa) — AC-016-06
- [x] T5 — Rotas `POST/GET/DELETE /coach/agenda/chave` com login — AC-016-02, 03, 09
- [x] T6 — Testes (banco real, API real, fakes) e `docs/INTEGRACAO_COM_O_COACH.md`
- [x] T7 — Comando "chave do coach" no WhatsApp (`CoachKeyStep`, frases exatas, sem Gemini) — Q-016-1
- [x] T8 — A api key pessoal (`api_key_automate`) também abre a agenda do dono (RN-016-09, decisão "uma chave só")
