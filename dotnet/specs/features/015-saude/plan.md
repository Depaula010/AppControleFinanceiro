# Plano 015 — Saúde e Coach (parte B: agenda para o Coach)

- **Estado:** rascunho (acompanha a spec; aprovação junto).

## Camadas
- **Domain** (puro, sem I/O): `MascaradorDeTexto` (links, e-mails, telefones, códigos; funções puras e testáveis), `NivelDeDetalhe` (`Ocupado | Titulos | Completo`, `Parse` que cai em `Ocupado` para valor desconhecido) e `AgendaParaOCoach.Montar(eventos, niveis, mascarar)` (aplica nível, mascaramento e limites de tamanho; **não** vê id de evento).
- **Application:** `ConsultarAgendaDoCoach` (valida `de`/`ate`, lê `ICalendarClient.GetEventsInRangeAsync` com teto de 10 s, pede os níveis ao `ICoachAgendaConfig` e devolve `AgendaDoCoachDto`); porta `ICoachAgendaConfig` (`NivelDe(calendarId)`, `Mascarar`).
- **Infrastructure:** `DbCoachAgendaConfig` (lê `configuracoes` com cache de 60 s, mesmo padrão do `DbGeminiEndpointProvider`; falha do banco = `Ocupado` para tudo).
- **Api:** módulo `Api/CoachAgenda/CoachAgendaModule.cs` (`IFeatureModule`): só registra a rota se `COACH_AGENDA_API_KEY` **e** `COACH_AGENDA_USER_ID` existirem; filtro de chave própria `RequireCoachAgendaKeyFilter` (tempo constante); erros com texto fixo; log só com contagens.
- **Sem migration.** Configuração por SQL em `configuracoes` (chaves da RN-015-03 e 04).
- **Deploy:** `docker-compose.yml` e `deploy.yml` passam `COACH_AGENDA_API_KEY` e `COACH_AGENDA_USER_ID` ao container (opcionais; sem elas, nada muda). O deploy derruba e recria os containers (~3 min); por isso **a implementação só entra em `main` com aviso do Rafael**.

## Testes (primeiro)
- Domain: máscaras (cada tipo, vários formatos, texto sem nada para mascarar, texto vazio), níveis, limites de tamanho depois do mascaramento, campos por nível, dia inteiro × com hora.
- Application com fakes: validação de entrada (3.1), Google não conectado, falha, estouro de tempo, ordenação, calendário com nível próprio × padrão.
- Integração: `404` sem as variáveis; `401` com `API_SECRET_KEY`; `200` com a chave própria; `503`; **nenhum log com conteúdo de evento**; `GET /` e uma rota existente inalteradas.
- Arquitetura: o módulo novo respeita as regras de dependência (`ArchitectureTests`).

## Ordem
1. Domain (máscara e níveis) → 2. Application → 3. Infrastructure (`DbCoachAgendaConfig`) → 4. Módulo, filtro e DI → 5. Integração e log → 6. Deploy opcional (compose e workflow) → 7. Doc em `docs/INTEGRACAO_COM_O_COACH.md` (como configurar níveis por SQL).
