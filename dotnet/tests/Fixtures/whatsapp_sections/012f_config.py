# -*- coding: utf-8 -*-
"""
Secao 012f (configuracoes) do gerador de fixtures: roda as intencoes REAIS do Flask (ConfigurarLocalizacaoIntent,
ConfigurarRelatorioMensalIntent, SolicitarApiKeyIntent, ConfigurarEnderecoIntent, ListarEnderecosIntent, DeletarEnderecoIntent)
sobre os servicos reais (location_service, monthly_report_config_service, user_address_service) com banco falso em memoria
e Gemini roteirizado. Valores FICTICIOS.
"""
import copy
import datetime as _dt
import json
import sys
import types

sys.path.insert(0, HERE)
import efh_support as S  # noqa: E402

STATE = {"report": None, "location": None, "addresses": [], "user": {"api_key": None, "nome": "Ana"}, "fail_location": False,
         "geocode": (None, None, None), "geocode_calls": []}
SCRIPT = {}


def _hm(text):
    return _dt.datetime.strptime(text, "%H:%M:%S").time()


def _db(sql, params):
    if "FROM MonthlyReportConfigs" in sql and sql.startswith("SELECT"):
        r = STATE["report"]
        return [S.Row(usuario_id=1, ativo=r["ativo"], momento=r["momento"], hora=r["hora"], c=None, u=None)] if r else []
    if sql.startswith("INSERT INTO MonthlyReportConfigs"):
        STATE["report"] = {"ativo": True, "momento": "INICIO_MES", "hora": _dt.time(8, 0)}
        return []
    if sql.startswith("UPDATE MonthlyReportConfigs"):
        r = STATE["report"]
        if "ativo" in params:
            r["ativo"] = params["ativo"]
        if "momento_envio" in params:
            r["momento"] = params["momento_envio"]
        if "hora_envio" in params:
            r["hora"] = _hm(params["hora_envio"])
        return []
    if sql.startswith("UPDATE Usuarios SET cidade"):
        if STATE["fail_location"]:
            raise Exception("boom segredo")
        STATE["location"] = {"cidade": params["cidade"], "estado": params["estado"]}
        return []
    if "SELECT api_key_automate" in sql:
        u = STATE["user"]
        return [S.Row(a=u["api_key"], n=u["nome"])] if u is not None else []
    if "FROM EnderecosFavoritos" in sql and sql.startswith("SELECT label"):
        order = {"casa": 1, "trabalho": 2, "outro": 3}
        rows = sorted(STATE["addresses"], key=lambda a: order.get(a["label"], 9))
        return [S.Row(l=a["label"], e=a["endereco"], la=a.get("lat"), lo=a.get("lon")) for a in rows]
    if "FROM EnderecosFavoritos" in sql and sql.startswith("SELECT endereco_completo"):
        found = [a for a in STATE["addresses"] if a["label"] == params["label"]]
        return [S.Row(e=found[0]["endereco"])] if found else []
    if sql.startswith("INSERT INTO EnderecosFavoritos"):
        STATE["addresses"] = [a for a in STATE["addresses"] if a["label"] != params["label"]]
        STATE["addresses"].append({"label": params["label"], "endereco": params["endereco"], "lat": params["lat"], "lon": params["lon"]})
        return []
    if sql.startswith("DELETE FROM EnderecosFavoritos"):
        STATE["addresses"] = [a for a in STATE["addresses"] if a["label"] != params["label"]]
        return []
    raise AssertionError("SQL inesperado: " + sql)


DB = S.FakeDb(_db)


def _geocode(address):
    STATE["geocode_calls"].append(address)
    return STATE["geocode"]


_gemini = types.ModuleType("app.services.gemini_service")
for _name in ["extract_location_config", "extract_monthly_report_config", "extract_address_config", "extract_address_label_from_deletion"]:
    setattr(_gemini, _name, (lambda n: lambda texto, usuario_id=None: copy.deepcopy(SCRIPT[n]))(_name))
sys.modules["app.services.gemini_service"] = _gemini
sys.modules["app.services"].gemini_service = _gemini
stub_module("app.shared")
stub_module("app.shared.formatters")
stub_module("app.shared.formatters.currency_formatter", formatar_moeda=lambda v: "R$ %s" % v)
stub_module("app.utils", formatar_moeda=lambda v: "R$ %s" % v, with_db_retry=lambda *a, **k: (lambda f: f), formatar_mes_ano_pt=lambda d: "")
stub_module("app.services.travel_time_service", TravelTimeService=types.SimpleNamespace(geocode_address=_geocode))
sys.modules.setdefault("sqlalchemy.engine", types.ModuleType("sqlalchemy.engine")).Connection = object

_saved_engine = sys.modules["app"].db_engine
sys.modules["app"].db_engine = DB
_base = load("base_intent.py", source("routes", "webhooks", "intents", "base_intent.py"))
stub_module("app.routes.webhooks.intents.base_intent", BaseIntent=_base["BaseIntent"], ConfirmationRequiredIntent=_base["ConfirmationRequiredIntent"])
LOC = load("location_service.py", source("services", "location_service.py"))["LocationService"]
stub_module("app.services.location_service", LocationService=LOC)
_report = load("monthly_report_config_service.py", source("services", "monthly_report_config_service.py"))
stub_module("app.services.monthly_report_config_service", **{k: _report[k] for k in ("get_or_create_config", "update_config", "desativar_config")})
UAS_F = load("user_address_service.py", source("services", "user_address_service.py"))["UserAddressService"]
stub_module("app.services.user_address_service", UserAddressService=UAS_F)
_admin = load("admin_intents.py", source("routes", "webhooks", "intents", "admin_intents.py").replace(
    "from .base_intent import", "from app.routes.webhooks.intents.base_intent import"))
_config = load("config_intents.py", source("routes", "webhooks", "intents", "config_intents.py").replace(
    "from .base_intent import", "from app.routes.webhooks.intents.base_intent import"))
sys.modules["app"].db_engine = _saved_engine


def snapshot():
    report = STATE["report"]
    return {
        "report": None if report is None else {"ativo": report["ativo"], "momento": report["momento"], "hora": report["hora"].strftime("%H:%M:%S")},
        "location": STATE["location"],
        "addresses": copy.deepcopy(STATE["addresses"]),
    }


def reset(report=None, addresses=(), user=None, fail_location=False, geocode=(None, None, None)):
    STATE["report"] = None if report is None else {"ativo": report[0], "momento": report[1], "hora": _hm(report[2])}
    STATE["location"] = None
    STATE["addresses"] = [dict(a) for a in addresses]
    STATE["user"] = user
    STATE["fail_location"] = fail_location
    STATE["geocode"] = geocode
    STATE["geocode_calls"] = []


def run(cls, name, gemini_name, reply, **setup):
    reset(**setup)
    if gemini_name:
        SCRIPT[gemini_name] = reply
    intent = cls(usuario_id=7, mensagem="mensagem", conn=DB.connect(), numero_whatsapp="5531999990001")
    result = intent.handle()
    return {"name": name, "gemini": reply, "setup": {k: v for k, v in setup.items()}, "success": result["success"], "message": result["message"],
            "state": snapshot(), "geocoded": list(STATE["geocode_calls"])}


# ------------------------------------------------------------------ localizacao
LocationIntent = _admin["ConfigurarLocalizacaoIntent"]
location_cases = []
for name, reply, setup in [
    ("minusculo", {"cidade": "Belo Horizonte", "estado": "mg"}, {}),
    ("normal", {"cidade": "São Paulo", "estado": "SP"}, {}),
    ("sem_estado", {"cidade": "Rio de Janeiro", "estado": None}, {}),
    ("estado_ausente", {"cidade": "Curitiba"}, {}),
    ("cidade_so_espacos", {"cidade": "   ", "estado": "SP"}, {}),
    ("sem_cidade", {"cidade": None, "estado": "SP"}, {}),
    ("estado_longo", {"cidade": "Recife", "estado": "PEE"}, {}),
    ("estado_vazio", {"cidade": "Recife", "estado": ""}, {}),
    ("estado_com_espacos", {"cidade": "  Recife  ", "estado": " pe "}, {}),
    ("estado_espaco", {"cidade": "Recife", "estado": "  "}, {}),
    ("cidade_numero", {"cidade": 5, "estado": "SP"}, {}),
    ("estado_numero", {"cidade": "Recife", "estado": 5}, {}),
    ("banco_falha", {"cidade": "Recife", "estado": "PE"}, {"fail_location": True}),
]:
    location_cases.append(run(LocationIntent, name, "extract_location_config", reply, **setup))
out["location_cases"] = location_cases

# ------------------------------------------------------------------ relatorio mensal
ReportIntent = _admin["ConfigurarRelatorioMensalIntent"]
report_cases = []
existing = (False, "FIM_MES", "10:30:00")
for name, reply, setup in [
    ("consultar_sem_config", {"acao": "consultar", "momento_envio": None, "hora_envio": None}, {}),
    ("consultar_ativo_inicio", {"acao": "consultar"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("consultar_desativado_fim", {"acao": "consultar"}, {"report": existing}),
    ("ativar_simples", {"acao": "ativar", "momento_envio": None, "hora_envio": None}, {"report": existing}),
    ("ativar_com_dados", {"acao": "ativar", "momento_envio": "INICIO_MES", "hora_envio": "14:00"}, {"report": existing}),
    ("ativar_sem_config", {"acao": "ativar"}, {}),
    ("desativar", {"acao": "desativar"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("configurar_hora", {"acao": "configurar", "momento_envio": None, "hora_envio": "14:00"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("configurar_hora_com_segundos", {"acao": "configurar", "hora_envio": "8:30:00"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("configurar_momento", {"acao": "configurar", "momento_envio": "FIM_MES", "hora_envio": None}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("configurar_nada", {"acao": "configurar", "momento_envio": None, "hora_envio": None}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("momento_invalido", {"acao": "configurar", "momento_envio": "MEIO_MES"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("hora_invalida", {"acao": "configurar", "hora_envio": "25:00"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("hora_de_um_digito", {"acao": "configurar", "hora_envio": "8:30"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("hora_segundos_61", {"acao": "configurar", "hora_envio": "08:00:61"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("hora_numero", {"acao": "configurar", "hora_envio": 8}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("acao_desconhecida", {"acao": "apagar"}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("acao_nula", {"acao": None}, {"report": (True, "INICIO_MES", "08:00:00")}),
    ("sem_chaves", {}, {"report": (True, "INICIO_MES", "08:00:00")}),
]:
    report_cases.append(run(ReportIntent, name, "extract_monthly_report_config", reply, **setup))
out["report_cases"] = report_cases

# ------------------------------------------------------------------ API key
ApiKeyIntent = _admin["SolicitarApiKeyIntent"]
apikey_cases = []
for name, user in [
    ("com_chave", {"api_key": "chave-ficticia-0123456789", "nome": "Ana"}),
    ("sem_nome", {"api_key": "chave-ficticia-0123456789", "nome": ""}),
    ("nome_nulo", {"api_key": "chave-ficticia-0123456789", "nome": None}),
    ("chave_nula", {"api_key": None, "nome": "Ana"}),
    ("chave_vazia", {"api_key": "", "nome": "Ana"}),
    ("sem_usuario", None),
]:
    apikey_cases.append(run(ApiKeyIntent, name, None, None, user=user))
out["apikey_cases"] = apikey_cases

# ------------------------------------------------------------------ enderecos
AddressConfigIntent = _config["ConfigurarEnderecoIntent"]
AddressListIntent = _config["ListarEnderecosIntent"]
AddressDeleteIntent = _config["DeletarEnderecoIntent"]
home = {"label": "casa", "endereco": "Rua A, 10, Centro", "lat": -19.9, "lon": -43.9}
work = {"label": "trabalho", "endereco": "Av B, 20", "lat": -19.8, "lon": -43.8}
other = {"label": "outro", "endereco": "Praça C", "lat": 0, "lon": None}

address_config_cases = []
for name, reply, setup in [
    ("salva_casa", {"label": "casa", "endereco_completo": "Av Paulista 1000"}, {"geocode": (-23.56, -46.65, "Avenida Paulista, 1000, São Paulo")}),
    ("salva_sem_formatado", {"label": "Trabalho", "endereco_completo": "Rua X 5"}, {"geocode": (-23.5, -46.6, None)}),
    ("atualiza_existente", {"label": "casa", "endereco_completo": "Rua Nova 9"}, {"addresses": [home], "geocode": (-1.0, -2.0, "Rua Nova, 9")}),
    ("nao_localizado", {"label": "casa", "endereco_completo": "Lugar nenhum"}, {}),
    ("lat_zero_vale", {"label": "outro", "endereco_completo": "Equador"}, {"geocode": (0.0, -50.0, "Linha do Equador")}),
    ("label_invalido", {"label": "escritorio", "endereco_completo": "Rua X"}, {"geocode": (1.0, 2.0, "x")}),
    ("sem_label", {"label": None, "endereco_completo": "Rua X"}, {}),
    ("sem_endereco", {"label": "casa", "endereco_completo": ""}, {}),
    ("label_numero", {"label": 5, "endereco_completo": "Rua X"}, {}),
]:
    address_config_cases.append(run(AddressConfigIntent, name, "extract_address_config", reply, **setup))
out["address_config_cases"] = address_config_cases

address_list_cases = []
for name, addresses in [("vazio", []), ("um", [home]), ("tres_fora_de_ordem", [other, work, home])]:
    address_list_cases.append(run(AddressListIntent, name, None, None, addresses=addresses))
out["address_list_cases"] = address_list_cases

address_delete_cases = []
for name, reply, addresses in [
    ("remove_casa", {"label": "casa"}, [home, work]),
    ("remove_maiusculo", {"label": "TRABALHO"}, [home, work]),
    ("nao_cadastrado", {"label": "outro"}, [home]),
    ("sem_label_vira_outro", {}, [other]),
    ("label_nulo", {"label": None}, [home]),
    ("label_desconhecido", {"label": "escritorio"}, [home]),
]:
    address_delete_cases.append(run(AddressDeleteIntent, name, "extract_address_label_from_deletion", reply, addresses=addresses))
out["address_delete_cases"] = address_delete_cases
