# -*- coding: utf-8 -*-
"""
Secao 012e (calendario) do gerador de fixtures: executada por generate_whatsapp_fixtures.py (usa `source`, `load`, `stub_module`, `out`).
Roda os servicos REAIS do Flask (calendar_query_service, calendar_management_service, event_confirmation_service,
free_time_finder_service, user_schedule_pattern_service e as classes de calendar_intents.py) com o relogio congelado, um Google Calendar
falso (efh_support.FakeGoogle), Redis em memoria e Gemini roteirizado, e grava a saida de cada cenario.
"""
import copy
import json
import os
import sys
import types

sys.path.insert(0, HERE)
import efh_support as S  # noqa: E402

# ------------------------------------------------------------------ estado controlavel pelos cenarios
OAUTH = {"connected": True, "service": None}
GEMINI_SCRIPT = {}
MODEL = types.SimpleNamespace(prompts=[], reply="Sugiro amanha as 9h porque e cedo.", fail=False)


class _OAuth:
    @staticmethod
    def is_user_connected(usuario_id):
        return OAUTH["connected"]

    @staticmethod
    def get_calendar_service(usuario_id):
        if not OAUTH["connected"] or OAUTH["service"] is None:
            raise Exception("Usuário não conectou Google Calendar")
        return OAUTH["service"]


class _Model:
    @staticmethod
    def generate_content(prompt):
        MODEL.prompts.append(prompt)
        if MODEL.fail:
            raise Exception("falha do modelo")
        return types.SimpleNamespace(text="  " + MODEL.reply + "  ")


def _gemini(name):
    def call(texto, usuario_id=None):
        return copy.deepcopy(GEMINI_SCRIPT[name])
    return call


FAKE_GEMINI = types.SimpleNamespace(**{n: _gemini(n) for n in [
    "extract_event_creation_details", "extract_event_deletion_query", "extract_calendar_query", "extract_time_filter_query",
    "extract_free_time_query"]})

ADDRESS_ROWS = {"rows": []}


def _address_db(sql, params):
    if "FROM EnderecosFavoritos" in sql:
        return list(ADDRESS_ROWS["rows"])
    raise AssertionError("SQL inesperado: " + sql)


ADDRESS_DB = S.FakeDb(_address_db)
TOKEN_DB = {"has_token": True}


def _token_db(sql, params):
    if "FROM GoogleCalendarTokens" in sql:
        return [S.Row(usuario_id=1)] if TOKEN_DB["has_token"] else []
    raise AssertionError("SQL inesperado: " + sql)


PATTERN_DB = S.FakeDb(_token_db)

# ------------------------------------------------------------------ modulos do Flask
_saved_app_attrs = {k: getattr(sys.modules["app"], k, None) for k in ("db_engine", "gemini_model")}
sys.modules["app"].db_engine = ADDRESS_DB
sys.modules["app"].gemini_model = _Model
sys.modules["app.utils"].formatar_mes_ano_pt = lambda d: "mes/%d" % d.month
sys.modules["app.services"].gemini_service = FAKE_GEMINI
sys.modules.setdefault("sqlalchemy.engine", types.ModuleType("sqlalchemy.engine")).Connection = object
stub_module("app.services.google_calendar_oauth_service", GoogleCalendarOAuthService=_OAuth)
stub_module("app.services.travel_time_service", TravelTimeService=types.SimpleNamespace(geocode_address=lambda a: (None, None, None)))


def load_frozen(name, text, extra=None):
    with S.frozen_time(S.sp(2026, 10, 8, 14, 20)):
        return load(name, text, extra)


GCS = load_frozen("google_calendar_service.py", source("services", "google_calendar_service.py"))["GoogleCalendarService"]
stub_module("app.services.google_calendar_service", GoogleCalendarService=GCS)
CMS = load_frozen("calendar_management_service.py", source("services", "calendar_management_service.py"))["CalendarManagementService"]
stub_module("app.services.calendar_management_service", CalendarManagementService=CMS)


def load_cqs(redirect_uri):
    stub_module("app.config", GOOGLE_REDIRECT_URI=redirect_uri, API_SECRET_KEY="x", BOT_WHATSAPP_URL="http://bot", WEBHOOK_SIGNATURE_KEY="y")
    return load_frozen("calendar_query_service.py", source("services", "calendar_query_service.py"))["CalendarQueryService"]


CQS = load_cqs("https://proxy.example/oauth/callback")
CQS_NO_URI = load_cqs("")
stub_module("app.services.calendar_query_service", CalendarQueryService=CQS)

_ev_ns = load_frozen("event_confirmation_service.py", source("services", "event_confirmation_service.py"))
_ev_ns["uuid"] = types.SimpleNamespace(uuid4=lambda: "ab12cd34-0000-0000-0000-000000000000")
_ev_ns["redis_service"] = fake_redis
ECS = _ev_ns["EventConfirmationService"]
stub_module("app.services.event_confirmation_service", EventConfirmationService=ECS)

UAS = load_frozen("user_address_service.py", source("services", "user_address_service.py"))["UserAddressService"]
stub_module("app.services.user_address_service", UserAddressService=UAS)

PATTERNS = load_frozen("user_schedule_pattern_service.py", source("services", "user_schedule_pattern_service.py"))["UserSchedulePatternService"]
stub_module("app.services.user_schedule_pattern_service", UserSchedulePatternService=PATTERNS)
FTF = load_frozen("free_time_finder_service.py", source("services", "free_time_finder_service.py"))["FreeTimeFinderService"]
stub_module("app.services.free_time_finder_service", FreeTimeFinderService=FTF)

_base_ns = load_frozen("base_intent.py", source("routes", "webhooks", "intents", "base_intent.py"))
stub_module("app.routes.webhooks.intents.base_intent", BaseIntent=_base_ns["BaseIntent"], ConfirmationRequiredIntent=_base_ns["ConfirmationRequiredIntent"])
_cal_src = source("routes", "webhooks", "intents", "calendar_intents.py").replace(
    "from .base_intent import", "from app.routes.webhooks.intents.base_intent import")
_cal_ns = load_frozen("calendar_intents.py", _cal_src)
CriarEvento, DeletarEvento, ConsultarAgenda, HorariosLivres = (
    _cal_ns["CriarEventoIntent"], _cal_ns["DeletarEventoIntent"], _cal_ns["ConsultarAgendaIntent"], _cal_ns["HorariosLivresIntent"])

for _k, _v in _saved_app_attrs.items():
    setattr(sys.modules["app"], _k, _v)
sys.modules["app"].db_engine = None


# ------------------------------------------------------------------ utilidades
def iso(value):
    return value.isoformat() if value is not None else None


def rows_for(addresses):
    return [(a["label"], a["endereco"], a.get("lat"), a.get("lon")) for a in addresses]


def with_addresses(addresses):
    ADDRESS_ROWS["rows"] = [S.Row(label=a["label"], endereco=a["endereco"], lat=a.get("lat"), lon=a.get("lon")) for a in addresses]


NOW = S.sp(2026, 10, 8, 14, 20)  # quinta-feira

# ------------------------------------------------------------------ A. mensagem de confirmacao do evento
confirmation_events = [
    {"usuario_id": 7, "titulo": "Reunião", "data_evento": "2026-10-09", "hora_inicio": "14:00", "hora_fim": "15:30", "descricao": "Pauta", "localizacao": "Av Paulista 1000"},
    {"usuario_id": 7, "titulo": "Dentista", "data_evento": "2026-10-09", "hora_inicio": "09:00", "hora_fim": None, "descricao": None, "localizacao": None},
    {"usuario_id": 7, "titulo": "Aniversário", "data_evento": "2026-12-15", "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": None},
    {"usuario_id": 7, "titulo": "Academia", "data_evento": "2026-10-09", "hora_inicio": "07:00", "hora_fim": None, "descricao": None, "localizacao": "Smart Fit"},
    {"usuario_id": None, "titulo": "Sem usuário", "data_evento": "2026-10-09", "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": "Algum lugar"},
    {"usuario_id": 7, "titulo": "Data ruim", "data_evento": "09/10", "hora_inicio": None, "hora_fim": None, "descricao": "", "localizacao": ""},
    {"usuario_id": 7, "titulo": "Com *marcação* e 'aspas'", "data_evento": "2026-02-28", "hora_inicio": "", "hora_fim": "10:00", "descricao": "linha1\nlinha2", "localizacao": "Rua X, 1"},
]
address_options = {
    "nenhum": [],
    "um": [{"label": "casa", "endereco": "Rua A, 10", "lat": -19.9, "lon": -43.9}],
}
cases = []
for event in confirmation_events:
    for name, addresses in address_options.items():
        with_addresses(addresses)
        cases.append({"event": event, "addresses": len(addresses), "text": ECS.format_confirmation_message(event)})
# falha ao buscar enderecos: o Flask devolve lista vazia
ADDRESS_ROWS["rows"] = None  # list(None) levanta TypeError dentro do get_user_addresses? nao: o erro ocorre na consulta e vira []
cases.append({"event": confirmation_events[0], "addresses": "falha", "text": ECS.format_confirmation_message(confirmation_events[0])})
out["event_confirmation_cases"] = cases

# ------------------------------------------------------------------ B. periodos da agenda e dos horarios livres
period_types = ["hoje", "amanha", "final_de_semana", "esta_semana", "proxima_semana", "este_mes", "mes_passado", "proximo_mes", "outro", None]
todays = [(2026, 10, 8), (2026, 10, 10), (2026, 10, 11), (2026, 10, 12), (2026, 12, 31), (2026, 1, 31), (2026, 12, 27), (2026, 3, 1), (2026, 2, 27)]
period_cases = []
free_range_cases = []
for ymd in todays:
    with S.frozen_time(S.sp(*ymd, 12, 0)):
        for period in period_types:
            start, end, description = CQS.get_period_dates_for_calendar(period)
            period_cases.append({"today": "%04d-%02d-%02d" % ymd, "period": period, "start": iso(start), "end": iso(end)})
        for period in ["hoje", "amanha", "esta_semana", "proxima_semana", "outro"]:
            result = FTF._calculate_date_range(period)
            free_range_cases.append({"today": "%04d-%02d-%02d" % ymd, "period": period,
                                     "start": iso(result["start"]) if result else None, "end": iso(result["end"]) if result else None})
out["agenda_period_cases"] = period_cases
out["free_range_cases"] = free_range_cases

# ------------------------------------------------------------------ C. filtro por periodo do dia
filter_events = [
    {"summary": "madrugada", "start": "2026-10-08T00:30:00-03:00", "end": "2026-10-08T01:00:00-03:00", "all_day": False},
    {"summary": "limite 05:59:00", "start": "2026-10-09T05:59:00-03:00", "end": "2026-10-09T06:30:00-03:00", "all_day": False},
    {"summary": "limite 05:59:30", "start": "2026-10-09T05:59:30-03:00", "end": "2026-10-09T06:30:00-03:00", "all_day": False},
    {"summary": "seis", "start": "2026-10-09T06:00:00-03:00", "end": "2026-10-09T07:00:00-03:00", "all_day": False},
    {"summary": "manha tarde", "start": "2026-10-08T11:59:00-03:00", "end": "2026-10-08T12:30:00-03:00", "all_day": False},
    {"summary": "meio-dia", "start": "2026-10-08T12:00:00-03:00", "end": "2026-10-08T13:00:00-03:00", "all_day": False},
    {"summary": "tarde", "start": "2026-10-08T15:30:00-03:00", "end": "2026-10-08T16:30:00-03:00", "all_day": False},
    {"summary": "17:59", "start": "2026-10-08T17:59:00-03:00", "end": "2026-10-08T18:30:00-03:00", "all_day": False},
    {"summary": "18h", "start": "2026-10-08T18:00:00-03:00", "end": "2026-10-08T19:00:00-03:00", "all_day": False},
    {"summary": "23:59", "start": "2026-10-08T23:59:00-03:00", "end": "2026-10-09T00:30:00-03:00", "all_day": False},
    {"summary": "utc", "start": "2026-10-08T18:00:00+00:00", "end": "2026-10-08T19:00:00+00:00", "all_day": False},
    {"summary": "sem fuso", "start": "2026-10-08T16:00:00", "end": "2026-10-08T17:00:00", "all_day": False},
    {"summary": "ja passou", "start": "2026-10-08T08:00:00-03:00", "end": "2026-10-08T09:00:00-03:00", "all_day": False},
    {"summary": "dia inteiro", "start": "2026-10-08", "end": "2026-10-09", "all_day": True},
    {"summary": "lixo", "start": "2026-10-08Tlixo", "end": "2026-10-08T10:00:00-03:00", "all_day": False},
    {"summary": "sem T", "start": "2026-10-08", "end": "2026-10-09", "all_day": False},
]
filter_cases = []
for now in [S.sp(2026, 10, 8, 10, 0), S.sp(2026, 10, 8, 14, 20), S.sp(2026, 10, 8, 20, 0)]:
    for period in ["manha", "tarde", "noite", "madrugada", "agora", "qualquer", None]:
        with S.frozen_time(now):
            kept = CQS.filter_events_by_time_period(filter_events, period)
        filter_cases.append({"now": now.isoformat(), "period": period, "kept": [e["summary"] for e in kept]})
out["agenda_filter_events"] = filter_events
out["agenda_filter_cases"] = filter_cases

# ------------------------------------------------------------------ D. consulta da agenda (servico real + Google falso)
def world_agenda():
    return [
        S.calendar("primary", "Principal", [
            S.timed("e1", "Academia", "2026-10-08T07:00:00", "2026-10-08T08:00:00", location="Smart Fit"),
            S.timed("e2", "Reunião <b>geral</b>", "2026-10-08T15:00:00", "2026-10-08T16:00:00", description="<p>Pauta&nbsp;do&amp;dia</p>"),
            S.all_day("e3", "Feriado", "2026-10-12", "2026-10-13"),
            S.timed("e4", "Jantar", "2026-10-09T20:00:00", "2026-10-09T22:00:00", location="Restaurante"),
            S.timed("e5", "Sexta cedo", "2026-10-09T08:00:00", "2026-10-09T09:00:00"),
            S.timed("e6", "Domingo", "2026-10-11T10:00:00", "2026-10-11T11:00:00"),
            S.timed("e7", "Próximo mês", "2026-11-03T10:00:00", "2026-11-03T11:00:00"),
            S.timed("e8", "Mês passado", "2026-09-15T10:00:00", "2026-09-15T11:00:00"),
        ], primary=True),
        S.calendar("trab", "Trabalho", [
            S.timed("t1", "Stand-up", "2026-10-08T09:30:00", "2026-10-08T09:45:00", description=""),
            S.timed("t2", "Noite", "2026-10-08T19:30:00", "2026-10-08T20:30:00", offset="+00:00"),
        ]),
        S.calendar("oculto", "Oculto", [S.timed("o1", "Não aparece", "2026-10-08T10:00:00", "2026-10-08T11:00:00")], selected=False),
    ]


def run_agenda(case_name, period, time_filter, now, connected=True, fail_list=False, fail_events=(), redirect=True, world=None):
    OAUTH["connected"] = connected
    google = S.FakeGoogle(world if world is not None else world_agenda(), fail_calendar_list=fail_list, fail_events_for=fail_events)
    OAUTH["service"] = google
    GEMINI_SCRIPT["extract_calendar_query"] = {"period_type": period}
    GEMINI_SCRIPT["extract_time_filter_query"] = {"time_filter": time_filter}
    service = CQS if redirect else CQS_NO_URI
    with S.frozen_time(now):
        try:
            if time_filter:
                text = service.query_agenda_with_time_filter(7, period, time_filter)
            else:
                text = service.query_agenda(7, period)
            error = None
        except Exception as e:
            text, error = None, "excecao"
    return {"name": case_name, "now": now.isoformat(), "period": period, "time_filter": time_filter, "connected": connected,
            "fail_list": fail_list, "fail_events": list(fail_events), "redirect": redirect,
            "text": text, "error": error, "calls": google.calls}


agenda_cases = []
for period in ["hoje", "amanha", "final_de_semana", "esta_semana", "proxima_semana", "este_mes", "mes_passado", "proximo_mes", "desconhecido"]:
    agenda_cases.append(run_agenda("periodo_" + period, period, None, NOW))
for tf in ["manha", "tarde", "noite", "agora", "madrugada"]:
    agenda_cases.append(run_agenda("hoje_filtro_" + tf, "hoje", tf, NOW))
agenda_cases.append(run_agenda("semana_filtro_noite", "esta_semana", "noite", NOW))
agenda_cases.append(run_agenda("semana_filtro_tarde", "final_de_semana", "tarde", NOW))
agenda_cases.append(run_agenda("filtro_desconhecido", "hoje", "qualquer", NOW))
agenda_cases.append(run_agenda("nao_conectado", "hoje", None, NOW, connected=False))
agenda_cases.append(run_agenda("nao_conectado_sem_uri", "hoje", None, NOW, connected=False, redirect=False))
agenda_cases.append(run_agenda("nao_conectado_filtro", "hoje", "tarde", NOW, connected=False))
agenda_cases.append(run_agenda("falha_ao_listar_calendarios", "hoje", None, NOW, fail_list=True))
agenda_cases.append(run_agenda("calendario_com_erro", "hoje", None, NOW, fail_events=["trab"]))
agenda_cases.append(run_agenda("sem_eventos_hoje", "hoje", None, S.sp(2026, 10, 20, 9, 0)))
agenda_cases.append(run_agenda("sem_eventos_amanha", "amanha", None, S.sp(2026, 10, 20, 9, 0)))
agenda_cases.append(run_agenda("sem_eventos_periodo", "esta_semana", None, S.sp(2026, 10, 20, 9, 0)))
agenda_cases.append(run_agenda("domingo_esta_semana", "esta_semana", None, S.sp(2026, 10, 11, 9, 0)))
out["agenda_cases"] = agenda_cases
out["agenda_world"] = world_agenda()

# ------------------------------------------------------------------ E. Deletar Evento (intent real)
def run_delete(name, query, world, now=NOW, redirect=True):
    OAUTH["connected"] = world is not None
    google = S.FakeGoogle(copy.deepcopy(world) if world is not None else [])
    OAUTH["service"] = google
    GEMINI_SCRIPT["extract_event_deletion_query"] = query
    with S.frozen_time(now):
        result = DeletarEvento(usuario_id=7, mensagem="deletar", conn=None, numero_whatsapp="5531999990001").handle()
    return {"name": name, "query": query, "now": now.isoformat(), "success": result["success"], "message": result["message"], "calls": google.calls}


del_world = [
    S.calendar("primary", "Principal", [
        S.timed("d1", "Academia", "2026-10-08T18:00:00", "2026-10-08T19:00:00"),
        S.timed("d2", "Academia", "2026-10-09T07:00:00", "2026-10-09T08:00:00"),
        S.timed("d3", "Dentista", "2026-10-09T10:00:00", "2026-10-09T11:00:00"),
        S.all_day("d4", "Academia dia inteiro", "2026-10-10", "2026-10-11"),
    ], primary=True),
    S.calendar("trab", "Trabalho", [S.timed("d5", "Reunião de academia", "2026-10-15T10:00:00", "2026-10-15T11:00:00")]),
]
delete_cases = [
    run_delete("varios", {"titulo_busca": "academia", "quando": None}, del_world),
    run_delete("um_achado", {"titulo_busca": "dentista", "quando": None}, del_world),
    run_delete("quando_hoje_um", {"titulo_busca": "academia", "quando": "hoje"}, del_world),
    run_delete("quando_amanha_um", {"titulo_busca": "academia", "quando": "amanha"}, del_world),
    run_delete("quando_hoje_nenhum", {"titulo_busca": "dentista", "quando": "hoje"}, del_world),
    run_delete("nao_achou", {"titulo_busca": "yoga", "quando": None}, del_world),
    run_delete("nao_achou_amanha", {"titulo_busca": "yoga", "quando": "amanha"}, del_world),
    run_delete("sem_titulo", {"titulo_busca": None, "quando": "hoje"}, del_world),
    run_delete("sem_google", {"titulo_busca": "academia", "quando": None}, None),
    run_delete("quando_outro_valor", {"titulo_busca": "dentista", "quando": "semana"}, del_world),
]
out["delete_event_cases"] = delete_cases
out["delete_world"] = del_world

# ------------------------------------------------------------------ F. Criar Evento (intent real + Redis em memoria)
def run_create(name, data, addresses=(), now=NOW, redis_up=True):
    fake_redis.store.clear()
    fake_redis.ops.clear()
    GEMINI_SCRIPT["extract_event_creation_details"] = data
    with_addresses(list(addresses))
    with S.frozen_time(now):
        try:
            result = CriarEvento(usuario_id=7, mensagem="criar", conn=None, numero_whatsapp="5531999990001").handle()
        except Exception as e:  # pragma: no cover
            result = {"success": False, "message": "excecao"}
    return {"name": name, "data": data, "addresses": len(addresses), "now": now.isoformat(), "success": result["success"], "message": result["message"],
            "redis": json.loads(json.dumps(fake_redis.store)), "ops": list(fake_redis.ops)}


create_cases = [
    run_create("completo", {"titulo": "Reunião", "data": "2026-10-09", "hora_inicio": "14:00", "hora_fim": "15:00", "descricao": "Pauta", "localizacao": "Av Paulista 1000"},
               addresses=[{"label": "casa", "endereco": "Rua A, 10", "lat": -19.9, "lon": -43.9}]),
    run_create("completo_sem_enderecos", {"titulo": "Reunião", "data": "2026-10-09", "hora_inicio": "14:00", "hora_fim": None, "descricao": None, "localizacao": "Av Paulista 1000"}),
    run_create("hoje", {"titulo": "Academia", "data": "hoje", "hora_inicio": "18:00", "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("amanha", {"titulo": "Academia", "data": "amanha", "hora_inicio": "07:00", "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("dia_inteiro", {"titulo": "Aniversário da Maria", "data": "2026-12-15", "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("data_invalida", {"titulo": "Prova", "data": "15/12", "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("data_inexistente", {"titulo": "Prova", "data": "2026-02-30", "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("sem_titulo", {"titulo": None, "data": "hoje", "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("sem_data", {"titulo": "Algo", "data": None, "hora_inicio": None, "hora_fim": None, "descricao": None, "localizacao": None}),
    run_create("chaves_ausentes", {"titulo": "Algo", "data": "hoje"}),
    run_create("hoje_apos_meia_noite", {"titulo": "Madrugada", "data": "hoje", "hora_inicio": "01:00", "hora_fim": None, "descricao": None, "localizacao": None},
               now=S.sp(2026, 12, 31, 0, 30)),
]
out["create_event_cases"] = create_cases

# ------------------------------------------------------------------ F2. confirmar e cancelar evento pendente (servicos reais)
def run_confirm(name, pending, create_result, redis_extra=None):
    fake_redis.store.clear()
    fake_redis.ops.clear()
    for key, value in (pending or {}).items():
        fake_redis.store[key] = json.loads(json.dumps(value))
    google = S.FakeGoogle([S.calendar("primary", "Principal", [], primary=True)], fail_insert=create_result)
    OAUTH["connected"] = create_result != "NAO_CONECTADO"
    OAUTH["service"] = google
    with S.frozen_time(NOW):
        ok, message, event_id = ECS.confirm_and_create_event("5531999990001", "ab12cd34")
    return {"name": name, "pending": pending, "ok": ok, "message": message, "event_id": event_id, "redis": json.loads(json.dumps(fake_redis.store)), "calls": google.calls}


ev_key = "pending_event:5531999990001:ab12cd34"
base_event = {"usuario_id": 7, "titulo": "Reunião", "data_evento": "2026-10-09", "hora_inicio": "14:00", "hora_fim": None, "descricao": "Pauta", "localizacao": "Sala 3"}
confirm_cases = [
    run_confirm("ok", {ev_key: base_event}, None),
    run_confirm("dia_inteiro", {ev_key: dict(base_event, hora_inicio=None)}, None),
    run_confirm("hora_fim", {ev_key: dict(base_event, hora_fim="16:30")}, None),
    run_confirm("hora_invalida", {ev_key: dict(base_event, hora_inicio="7h")}, None),
    run_confirm("hora_vazia", {ev_key: dict(base_event, hora_inicio="")}, None),
    run_confirm("hora_fim_vazia", {ev_key: dict(base_event, hora_fim="")}, None),
    run_confirm("hora_um_digito", {ev_key: dict(base_event, hora_inicio="7:5", hora_fim="9:0")}, None),
    run_confirm("virada_do_dia", {ev_key: dict(base_event, hora_inicio="23:30")}, None),
    run_confirm("falha_da_api", {ev_key: base_event}, "quotaExceeded"),
    run_confirm("nao_conectado", {ev_key: base_event}, "NAO_CONECTADO"),
    run_confirm("expirado", {}, None),
]
out["confirm_event_cases"] = confirm_cases

# ------------------------------------------------------------------ G. padroes do usuario e horarios livres
def pattern_events():
    base = []
    start = S.sp(2026, 7, 20, 9, 0)
    import datetime as dt
    for index in range(12):
        day = start + dt.timedelta(days=index * 3)
        hour = 9 + (index % 5)
        base.append(S.timed("p%d" % index, "Evento %d" % index, day.strftime("%Y-%m-%dT") + "%02d:00:00" % hour, day.strftime("%Y-%m-%dT") + "%02d:30:00" % (hour + 1)))
    base.append(S.all_day("pd", "Dia inteiro", "2026-08-01", "2026-08-02"))
    base.append({"id": "pz", "summary": "UTC", "start": {"dateTime": "2026-08-03T12:00:00Z"}, "end": {"dateTime": "2026-08-03T13:15:00Z"}})
    base.append({"id": "pbad", "summary": "Sem fim", "start": {"dateTime": "2026-08-04T12:00:00-03:00"}, "end": {}})
    base.append(S.timed("plong", "Longo", "2026-08-05T08:00:00", "2026-08-05T20:00:00"))
    base.append(S.timed("pnight", "Noite", "2026-08-06T19:00:00", "2026-08-06T21:45:30"))
    return base


pattern_cases = []
event_sets = {
    "vazio": [],
    "so_dia_inteiro": [S.all_day("a", "x", "2026-08-01", "2026-08-02")],
    "um": [S.timed("u", "Um", "2026-08-03T10:00:00", "2026-08-03T11:00:00")],
    "doze_mais": pattern_events(),
    "cinco": pattern_events()[:5],
    "seis": pattern_events()[:6],
    "empate_de_dias": [S.timed("t%d" % i, "e", "2026-08-%02dT10:00:00" % (3 + i), "2026-08-%02dT11:00:00" % (3 + i)) for i in range(4)],
}
for name, events in event_sets.items():
    with S.frozen_time(NOW):
        patterns = PATTERNS._extract_patterns_from_events(events)
    pattern_cases.append({"name": name, "events": events, "patterns": patterns, "insights": PATTERNS.get_insights_text(patterns)})
out["pattern_cases"] = pattern_cases
out["default_patterns"] = PATTERNS._get_default_patterns()
out["default_insights"] = PATTERNS.get_insights_text(PATTERNS._get_default_patterns())


def free_world(kind):
    if kind == "livre":
        return [S.calendar("primary", "Principal", [], primary=True)]
    if kind == "ocupado":
        return [
            S.calendar("primary", "Principal", [
                S.timed("f1", "Manhã cheia", "2026-10-08T09:00:00", "2026-10-08T11:30:00"),
                S.timed("f2", "Almoço", "2026-10-08T12:00:00", "2026-10-08T13:00:00"),
                S.timed("f3", "Tarde", "2026-10-08T16:00:00", "2026-10-08T17:00:00"),
                S.timed("f4", "Sexta", "2026-10-09T08:30:00", "2026-10-09T10:00:00"),
                S.timed("f5", "Sexta tarde", "2026-10-09T14:00:00", "2026-10-09T18:30:00"),
                S.all_day("f6", "Feriado", "2026-10-12", "2026-10-13"),
                S.timed("f7", "Longo", "2026-10-13T06:00:00", "2026-10-14T01:00:00"),
                S.timed("f8", "Segunda", "2026-10-12T10:00:00", "2026-10-12T11:00:00"),
                S.timed("f9", "Sobreposto", "2026-10-12T10:30:00", "2026-10-12T12:00:00"),
            ], primary=True),
            S.calendar("trab", "Trabalho", [S.timed("g1", "Call", "2026-10-08T14:30:00", "2026-10-08T15:00:00")]),
        ]
    raise AssertionError(kind)


def run_free(name, world_kind, period, duration, contexto, now, token=True, history=None, fail_calendar_list=False, fail_events=(),
             model_fail=False, model_reply="Sugiro quinta às 9h porque é cedo.", connected=True):
    OAUTH["connected"] = connected
    TOKEN_DB["has_token"] = token
    world = free_world(world_kind)
    if history is not None:
        world[0]["events"] = world[0]["events"] + history
    google = S.FakeGoogle(world, fail_calendar_list=fail_calendar_list, fail_events_for=fail_events)
    OAUTH["service"] = google
    GEMINI_SCRIPT["extract_free_time_query"] = {"period_type": period, "duracao_minutos": duration, "contexto": contexto}
    MODEL.prompts.clear()
    MODEL.fail = model_fail
    MODEL.reply = model_reply
    with S.frozen_time(now):
        result = FTF.find_free_slots(PATTERN_DB, 7, period, duration)
        result_message = FTF.format_free_slots_message(result, contexto)
        data = {"result": result, "contexto": contexto}
        intent = HorariosLivres(usuario_id=7, mensagem="livre", conn=None, numero_whatsapp="5531999990001")
        intent.params = {"period_type": period, "duracao_minutos": duration, "contexto": contexto}
        intent_message = intent.format_response(data)
    MODEL.fail = False
    return {"name": name, "now": now.isoformat(), "period": period, "duration": duration, "contexto": contexto, "world": world_kind, "token": token,
            "connected": connected, "history": history, "fail_calendar_list": fail_calendar_list, "fail_events": list(fail_events),
            "model_fail": model_fail, "model_reply": model_reply,
            "result": result, "message": result_message, "intent_message": intent_message, "prompts": list(MODEL.prompts), "calls": google.calls}


import datetime as _dt
history = pattern_events()
free_cases = [
    run_free("hoje_livre_sem_historico", "livre", "hoje", 60, None, NOW, token=False),
    run_free("hoje_livre_cedo", "livre", "hoje", 60, None, S.sp(2026, 10, 8, 7, 10), token=False),
    run_free("hoje_livre_tarde_da_noite", "livre", "hoje", 60, None, S.sp(2026, 10, 8, 19, 40), token=False),
    run_free("hoje_depois_do_expediente", "livre", "hoje", 60, None, S.sp(2026, 10, 8, 20, 10), token=False),
    run_free("meia_hora_exata", "livre", "hoje", 30, None, S.sp(2026, 10, 8, 10, 30), token=False),
    run_free("amanha_livre", "livre", "amanha", 120, "dentista", NOW, token=False),
    run_free("semana_ocupada", "ocupado", "esta_semana", 60, None, NOW, token=False),
    run_free("proxima_semana_ocupada", "ocupado", "proxima_semana", 90, "reunião", NOW, token=False),
    run_free("hoje_ocupado", "ocupado", "hoje", 45, "dentista", NOW, token=False),
    run_free("duracao_longa", "ocupado", "esta_semana", 240, None, NOW, token=False),
    run_free("com_historico", "ocupado", "esta_semana", 60, "dentista", NOW, token=True, history=history),
    run_free("com_historico_sem_contexto", "ocupado", "proxima_semana", 60, None, NOW, token=True, history=history),
    run_free("periodo_invalido", "livre", "mes", 60, "dentista", NOW, token=False),
    run_free("sem_token", "ocupado", "esta_semana", 60, "dentista", NOW, token=False),
    run_free("nao_conectado", "ocupado", "esta_semana", 60, "dentista", NOW, token=True, connected=False),
    run_free("falha_ao_listar", "ocupado", "esta_semana", 60, "dentista", NOW, token=False, fail_calendar_list=True),
    run_free("calendario_com_erro", "ocupado", "esta_semana", 60, "dentista", NOW, token=False, fail_events=["trab"]),
    run_free("modelo_falha", "ocupado", "esta_semana", 60, "dentista", NOW, token=False, model_fail=True),
    run_free("domingo_esta_semana", "ocupado", "esta_semana", 60, None, S.sp(2026, 10, 11, 9, 0), token=False),
    run_free("duracao_fracionada", "livre", "amanha", 90.5, None, NOW, token=False),
]
out["free_slots_cases"] = free_cases
out["free_worlds"] = {"livre": free_world("livre"), "ocupado": free_world("ocupado")}
out["free_history"] = history

# modelo do prompt da sugestao da IA (marcadores no lugar das variaveis) e casos renderizados
_slot_probe = {"data": "AAAA", "hora_inicio": "BBBB", "hora_fim": "CCCC", "qualidade": "DDDD", "motivo": "EEEE"}
MODEL.prompts.clear()
MODEL.fail = False
FTF.suggest_best_slot_with_ai({"slots_livres": [_slot_probe]}, "⟦CONTEXTO⟧", "⟦PREFS⟧")
_probe_prompt = MODEL.prompts[-1]
_probe_repr = repr([{"data": "AAAA", "horario": "BBBB-CCCC", "qualidade": "DDDD", "motivo": "EEEE"}])
assert _probe_repr in _probe_prompt
_template = _probe_prompt.replace(_probe_repr, "⟦SLOTS⟧")
with open(os.path.join(PROMPTS, "free-time-suggestion.txt"), "w", encoding="utf-8", newline="") as _fh:
    _fh.write(_template)

suggestion_cases = []
for contexto, slots, prefs in [
    ("dentista", [{"data": "2026-10-09", "hora_inicio": "09:00", "hora_fim": "11:00", "qualidade": "otimo", "motivo": "Dia livre"}], "Você costuma ter compromissos entre 09:00 e 18:00."),
    ("reunião \"importante\"", [{"data": "2026-10-09", "hora_inicio": "09:00", "hora_fim": "11:00", "qualidade": "bom", "motivo": "Sem eventos adjacentes, período preferido (manhã)"}] * 6, ""),
    ("café d'água", [{"data": "2026-10-10", "hora_inicio": "14:00", "hora_fim": "15:00", "qualidade": "regular", "motivo": "Horário disponível"}], "Sem histórico suficiente para análise de padrões."),
]:
    MODEL.prompts.clear()
    reply = FTF.suggest_best_slot_with_ai({"slots_livres": slots}, contexto, prefs)
    suggestion_cases.append({"contexto": contexto, "slots": slots, "prefs": prefs, "prompt": MODEL.prompts[-1], "reply": reply})
MODEL.prompts.clear()
suggestion_cases.append({"contexto": "x", "slots": [], "prefs": "", "prompt": None, "reply": FTF.suggest_best_slot_with_ai({"slots_livres": []}, "x", "")})
out["suggestion_cases"] = suggestion_cases

# mensagem dos horarios livres com resultados sinteticos (inclui ramos que o fluxo nao produz sozinho)
format_cases = []
synthetic = [
    {"slots_livres": [], "insights_usuario": ""},
    {"slots_livres": [], "insights_usuario": "Sem histórico suficiente para análise de padrões."},
    {"error": "Período inválido", "slots_livres": []},
    {"slots_livres": [
        {"data": "2026-10-08", "hora_inicio": "15:30", "hora_fim": "18:00", "duracao_minutos": 150, "qualidade": "otimo", "motivo": "Dia livre"},
        {"data": "2026-10-08", "hora_inicio": "08:00", "hora_fim": "09:00", "duracao_minutos": 60, "qualidade": "bom", "motivo": "Sem eventos adjacentes"},
        {"data": "2026-10-08", "hora_inicio": "13:00", "hora_fim": "14:00", "duracao_minutos": 60, "qualidade": "regular", "motivo": "Horário disponível"},
        {"data": "2026-10-08", "hora_inicio": "19:00", "hora_fim": "20:00", "duracao_minutos": 60, "qualidade": "regular", "motivo": "x"},
        {"data": "2026-10-09", "hora_inicio": "09:00", "hora_fim": "10:00", "duracao_minutos": 60, "qualidade": "bom", "motivo": "m"},
        {"data": "2026-10-11", "hora_inicio": "09:00", "hora_fim": "10:00", "duracao_minutos": 60, "qualidade": "estranho", "motivo": "m"},
    ], "insights_usuario": "Você costuma ter compromissos entre 09:00 e 18:00.", "total_analisado": 12},
    {"slots_livres": [{"data": "2026-10-10", "hora_inicio": "09:00", "hora_fim": "10:00", "duracao_minutos": 60, "qualidade": "otimo", "motivo": "Dia livre"}],
     "insights_usuario": "Sem histórico suficiente para análise de padrões.", "total_analisado": 0},
]
for index, result in enumerate(synthetic):
    for contexto in [None, "dentista", ""]:
        format_cases.append({"result": result, "contexto": contexto, "text": FTF.format_free_slots_message(result, contexto)})
out["free_format_cases"] = format_cases
