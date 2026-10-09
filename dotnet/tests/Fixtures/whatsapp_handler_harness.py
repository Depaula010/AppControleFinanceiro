# -*- coding: utf-8 -*-
"""
Harness de paridade da feature 012: executa o `WhatsAppHandler` REAL do Flask (app/routes/webhooks/handlers/whatsapp_handler.py)
com os servicos simulados por roteiro, e grava por cenario a resposta (status e corpo) e as chamadas relevantes.
`TransactionConfirmationService` e `EventConfirmationService` tambem sao os reais (com Redis em memoria).

Saida: tests/Fixtures/whatsapp-pipeline-scenarios.json, lida por WhatsAppPipelineParityTests (unitario) e pelos testes de rota.
Cada cenario traz `group` (a sub-entrega que o habilita: 012-0, 012a, ...); o teste so roda os grupos ja implementados.
Uso: python whatsapp_handler_harness.py   (so para regenerar; a saida e versionada)
Todos os valores sao FICTICIOS.
"""
import ast
import fnmatch
import hashlib
import hmac
import json
import os
import secrets
import sys
import types

_REAL_STDOUT = sys.stdout
sys.stdout = open(os.devnull, "w", encoding="utf-8")  # os prints do Flask nao interessam

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "whatsapp-pipeline-scenarios.json")
APP = os.path.join(HERE, "..", "..", "..", "app")

API_KEY = "chave-de-teste-0123456789abcdef0123456789"
SIGNATURE_KEY = "assinatura-de-teste-0123456789abcdef"
NUMBER = "5531999990001"


def source(*parts):
    with open(os.path.join(APP, *parts), encoding="utf-8") as fh:
        return fh.read()


def load(name, text, extra=None):
    namespace = dict(extra or {})
    exec(compile(text, name, "exec"), namespace)
    return namespace


def stub_module(name, **attrs):
    module = types.ModuleType(name)
    for key, value in attrs.items():
        setattr(module, key, value)
    sys.modules[name] = module
    return module


# ------------------------------------------------------------------ ambiente por cenario
class FakeRedis:
    def __init__(self, initial=None):
        self.store = {}
        for key, value in (initial or {}).items():
            self.store[key] = json.loads(json.dumps(value))
        self.writes = []

    def set_with_ttl(self, key, value, ttl_seconds=300):
        self.writes.append(["set", key, ttl_seconds])
        self.store[key] = json.loads(json.dumps(value))
        return True

    def get(self, key):
        return self.store.get(key)

    def delete(self, key):
        self.writes.append(["delete", key])
        self.store.pop(key, None)
        return True

    def exists(self, key):
        return key in self.store

    def get_keys_by_pattern(self, pattern):
        return [k for k in self.store if fnmatch.fnmatchcase(k, pattern)]


class Env:
    """Roteiro do cenario corrente; os servicos simulados leem daqui."""

    def __init__(self, cfg):
        self.cfg = cfg
        self.redis = FakeRedis(cfg.get("redis"))
        self.calls = []

    def call(self, label):
        self.calls.append(label)


ENV = None


class RedisProxy:
    def __getattr__(self, name):
        return getattr(ENV.redis, name)


class Request:
    def __init__(self):
        self.headers = {}
        self._body = b""
        self._json_error = None

    def get_data(self):
        return self._body

    @property
    def json(self):
        if self._json_error:
            raise Exception(self._json_error)
        return json.loads(self._body.decode("utf-8"))


REQUEST = Request()
DB_OK = {"value": True}


class FakeConn:
    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def begin(self):
        return self

    def commit(self):
        pass

    def execute(self, *a, **k):
        raise AssertionError("o harness nao executa SQL")


class FakeEngine:
    def connect(self):
        return FakeConn()


def jsonify(payload):
    return payload


# modulos de infraestrutura
stub_module("flask", jsonify=jsonify, request=REQUEST)
app = stub_module("app", db_engine=FakeEngine(), gemini_model=object())
stub_module("app.config", API_SECRET_KEY=API_KEY, BOT_WHATSAPP_URL="http://bot", WEBHOOK_SIGNATURE_KEY=SIGNATURE_KEY)

sanitizer = load("input_sanitizer.py", source("shared", "validators", "input_sanitizer.py"))
currency_src = source("shared", "formatters", "currency_formatter.py")
currency = load("currency_formatter.py", currency_src)


def _no_locale(*a, **k):
    raise ValueError("locale")


currency["locale"].currency = _no_locale
hmac_ns = load("hmac_validator.py", source("shared", "security", "hmac_validator.py"))


def ensure_db_connection():
    if not DB_OK["value"]:
        raise Exception("banco fora")


stub_module(
    "app.utils",
    formatar_moeda=currency["formatar_moeda"], ensure_db_connection=ensure_db_connection,
    verify_hmac_signature=hmac_ns["verify_hmac_signature"], compare_keys_safe=hmac_ns["compare_keys_safe"],
    sanitize_for_log=sanitizer["sanitize_for_log"], sanitize_input=sanitizer["sanitize_input"],
    formatar_mes_ano_pt=lambda d: "", )

services = stub_module("app.services")


class Recorder:
    """Servico simulado: cada metodo registra `Servico.metodo` e devolve o valor do roteiro (ENV.cfg['returns'])."""

    def __init__(self, name):
        self._name = name

    def __getattr__(self, method):
        def call(*args, **kwargs):
            label = "%s.%s" % (self._name, method)
            ENV.call(label)
            result = ENV.cfg.get("returns", {}).get(label)
            if isinstance(result, dict) and result.get("__raise__"):
                raise Exception(result["__raise__"])
            if isinstance(result, dict) and "__tuple__" in result:
                return tuple(result["__tuple__"])
            return result
        return call


class UserService:
    def check_user_exists(self, numero):
        ENV.call("user_service.check_user_exists:" + numero)
        return (7, "Ana") if ENV.cfg.get("user", True) else None

    def get_registration_state(self, numero):
        return None


user_service = UserService()
services.user_service = user_service
services.finance_service = Recorder("finance_service")
services.notification_service = Recorder("notification_service")


class GeminiService:
    def get_message_intent(self, texto, usuario_id=None):
        ENV.call("gemini_service.get_message_intent")
        intent = ENV.cfg.get("intent")
        if isinstance(intent, dict) and intent.get("__raise__"):
            raise Exception(intent["__raise__"])
        return intent

    def __getattr__(self, name):
        def call(*a, **k):
            ENV.call("gemini_service." + name)
            result = ENV.cfg.get("returns", {}).get("gemini_service." + name)
            return result
        return call


services.gemini_service = GeminiService()
services.redis_service = RedisProxy()
stub_module("app.services.redis_service", redis_service=RedisProxy())
stub_module("app.services.period_query_service", PeriodQueryService=Recorder("PeriodQueryService"))
stub_module("app.services.fixed_bills_service", FixedBillsService=Recorder("FixedBillsService"))
stub_module("app.services.transaction_feedback_service", gerar_feedback_transacao=lambda conn, tid: (ENV.call("feedback") or "FEEDBACK"))
stub_module("app.services.finance")
stub_module("app.services.finance.budget_validation_service", validate_budget=lambda *a, **k: None)
stub_module("app.services.google_calendar_oauth_service", GoogleCalendarOAuthService=Recorder("GoogleCalendarOAuthService"))
stub_module("app.services.calendar_query_service", CalendarQueryService=Recorder("CalendarQueryService"))
stub_module("app.services.notification_config_service", NotificationConfigService=Recorder("NotificationConfigService"))
stub_module("app.services.user_address_service", UserAddressService=types.SimpleNamespace(
    LABEL_NAMES={"casa": "Casa", "trabalho": "Trabalho", "outro": "Outro"}, LABEL_EMOJIS={"casa": "🏠", "trabalho": "💼", "outro": "➕"},
    get_user_addresses=lambda uid: (ENV.call("UserAddressService.get_user_addresses") or ENV.cfg.get("addresses", []))))
stub_module("app.services.travel_time_service", TravelTimeService=types.SimpleNamespace(
    geocode_address=lambda address: (ENV.call("TravelTimeService.geocode_address") or tuple(ENV.cfg.get("geocode", [None, None, None]))),
    calculate_travel_time=lambda *a: (ENV.call("TravelTimeService.calculate_travel_time") or ENV.cfg.get("travel"))))


class CalendarManagement:
    @staticmethod
    def create_event(**kwargs):
        ENV.call("CalendarManagementService.create_event")
        return tuple(ENV.cfg.get("create_event", [True, "Evento criado", "g1"]))


stub_module("app.services.calendar_management_service", CalendarManagementService=CalendarManagement)
sys.modules["app.services"].finance_service = services.finance_service

# NightlyCheckinService: ESCAPE_KEYWORDS reais (ast) e process_response roteirizado
tree = ast.parse(source("services", "nightly_checkin_service.py"))
escape = None
for node in ast.walk(tree):
    if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "ESCAPE_KEYWORDS" for t in node.targets):
        escape = ast.literal_eval(node.value)
assert escape and len(escape) == 31


class NightlyCheckinService:
    ESCAPE_KEYWORDS = escape

    @staticmethod
    def process_response(numero, mensagem, checkin_id):
        ENV.call("NightlyCheckinService.process_response")
        ENV.redis.delete("nightly_checkin_active:" + numero)
        return tuple(ENV.cfg.get("checkin_response", ["ok", "RESPOSTA-DO-CHECKIN"]))


stub_module("app.services.nightly_checkin_service", NightlyCheckinService=NightlyCheckinService)

# servicos reais (Redis em memoria)
confirmation_ns = load("transaction_confirmation_service.py", source("services", "transaction_confirmation_service.py"))
stub_module("app.services.transaction_confirmation_service", TransactionConfirmationService=confirmation_ns["TransactionConfirmationService"])
events_ns = load("event_confirmation_service.py", source("services", "event_confirmation_service.py"))
stub_module("app.services.event_confirmation_service", EventConfirmationService=events_ns["EventConfirmationService"])

# intents: route_intent / INTENT_REGISTRY simulados (a mensagem de cada um e [intent:NOME])
menu = None
for node in ast.walk(ast.parse(source("routes", "webhooks", "intents", "admin_intents.py"))):
    if isinstance(node, ast.ClassDef) and node.name == "MenuAjudaIntent":
        for fn_node in node.body:
            if isinstance(fn_node, ast.FunctionDef) and fn_node.name == "format_response":
                for stmt in ast.walk(fn_node):
                    if isinstance(stmt, ast.Return) and isinstance(stmt.value, ast.Constant):
                        menu = stmt.value.value


def route_intent(intent_name, usuario_id, mensagem, conn, numero_whatsapp=None):
    ENV.call("route_intent:" + intent_name)
    if intent_name == "Menu de Ajuda":
        return {"success": True, "message": menu, "data": None}
    return {"success": True, "message": "[intent:%s]" % intent_name, "data": None}


class _FakeIntent:
    def __init__(self, name):
        self.name = name

    def __call__(self, **kwargs):
        ENV.call("registry:" + self.name)
        return types.SimpleNamespace(handle=lambda: {"success": True, "message": "[intent:%s]" % self.name})


INTENT_REGISTRY = {name: _FakeIntent(name) for name in ["Contas Atrasadas", "Transferência", "Upload Drive"]}
stub_module("app.routes")
stub_module("app.routes.webhooks")
stub_module("app.routes.webhooks.intents", route_intent=route_intent, INTENT_REGISTRY=INTENT_REGISTRY)

handler_ns = load("whatsapp_handler.py", source("routes", "webhooks", "handlers", "whatsapp_handler.py"))
HANDLER = handler_ns["WhatsAppHandler"]()


# ------------------------------------------------------------------ execucao de um cenario
def run(cfg):
    global ENV
    ENV = Env(cfg)
    DB_OK["value"] = cfg.get("db_ok", True)
    body = cfg.get("body")
    raw = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else b""
    REQUEST._body = raw
    REQUEST._json_error = cfg.get("json_error")
    headers = {}
    if cfg.get("send_api_key", True):
        headers["x-api-key"] = cfg.get("api_key", API_KEY)
    if cfg.get("signature") == "valid":
        headers["X-Webhook-Signature"] = hmac.new(SIGNATURE_KEY.encode(), raw, hashlib.sha256).hexdigest()
    elif cfg.get("signature"):
        headers["X-Webhook-Signature"] = cfg["signature"]
    REQUEST.headers = _Headers(headers)
    payload, status = HANDLER.handle()
    return {"status": status, "body": payload, "calls": list(ENV.calls), "redis": ENV.redis.store, "redis_writes": ENV.redis.writes}


class _Headers(dict):
    def get(self, key, default=None):
        for k, v in self.items():
            if k.lower() == key.lower():
                return v
        return default


def scenario(name, group, texto, numero=NUMBER + "@s.whatsapp.net", deviation=None, layer="pipeline", **cfg):
    cfg = dict(cfg)
    cfg.setdefault("body", {"texto": texto, "numero_remetente": numero})
    result = run(cfg)
    return {
        "name": name, "group": group, "layer": layer, "deviation": deviation,
        "texto": texto, "numero": numero,
        "config": {k: v for k, v in cfg.items() if k not in ("body",)},
        "body": cfg["body"] if not isinstance(cfg["body"], bytes) else None,
        "result": result,
    }


SCENARIOS = []
add = SCENARIOS.append

# ---- 012-0: HTTP
add(scenario("db_fora", "012-0", "oi", db_ok=False, layer="http"))
add(scenario("assinatura_invalida", "012-0", "oi", signature="deadbeef", layer="http"))
add(scenario("assinatura_valida", "012-0", "oi", signature="valid", intent="Menu de Ajuda", layer="http"))
add(scenario("sem_assinatura_aceita", "012-0", "oi", intent="Menu de Ajuda", layer="http"))
add(scenario("chave_ausente", "012-0", "oi", send_api_key=False, layer="http"))
add(scenario("chave_errada", "012-0", "oi", api_key="outra-chave", layer="http"))
add(scenario("chave_com_espacos", "012-0", "oi", api_key="  " + API_KEY + "  ", intent="Menu de Ajuda", layer="http"))
add(scenario("texto_vazio", "012-0", "", body={"texto": "", "numero_remetente": NUMBER}, layer="http"))
add(scenario("numero_vazio", "012-0", "oi", body={"texto": "oi", "numero_remetente": ""}, layer="http"))
add(scenario("sem_texto", "012-0", None, body={"numero_remetente": NUMBER}, layer="http"))
add(scenario("sem_content_type", "012-0", "oi", deviation="024", json_error="415 Unsupported Media Type", layer="http"))
add(scenario("texto_numerico", "012-0", None, body={"texto": 5, "numero_remetente": NUMBER}, layer="http"))
add(scenario("cota_do_gemini", "012-0", "oi", intent={"__raise__": "429 Quota exceeded - Gemini API rate limit reached"}, layer="http"))
add(scenario("erro_generico", "012-0", "oi", intent={"__raise__": "boom"}, layer="http"))
add(scenario("erro_rate_limit_texto", "012-0", "oi", intent={"__raise__": "Rate Limit hit"}, layer="http"))

# ---- 012-0: pipeline
add(scenario("nao_cadastrado", "012-0", "oi", user=False))
add(scenario("numero_sem_arroba", "012-0", "ajuda", numero=NUMBER, intent="Menu de Ajuda"))
add(scenario("numero_com_especiais", "012-0", "ajuda", numero="55<31>9;99|9&0001@s.whatsapp.net", intent="Menu de Ajuda"))
add(scenario("texto_com_especiais", "012-0", "gastei {50} <ok> $x", intent="Menu de Ajuda"))
add(scenario("menu_de_ajuda", "012-0", "o que voce faz?", intent="Menu de Ajuda"))
add(scenario("intencao_desconhecida", "012-0", "blabla", intent="Intencao Inexistente"))
add(scenario("intencao_nula", "012-0", "blabla", intent=None))
add(scenario("quitar_conta_fixa", "012-0", "marcar luz como paga", intent="Quitar Conta Fixa"))
add(scenario("intencao_sem_acento", "012-0", "x", intent="Consulta Periodo"))
add(scenario("cancelar_sem_pendencia", "012-0", "cancelar"))
add(scenario("cancela_sem_pendencia", "012-0", "  Cancela  "))
add(scenario("cancela_com_last_pending", "012-0", "cancela", redis={"last_pending:" + NUMBER: "abc12345"}, intent="Menu de Ajuda"))
add(scenario("checkin_ativo_resposta", "012-0", "1, 3", redis={"nightly_checkin_active:" + NUMBER: "chk00001"}))
add(scenario("checkin_ativo_ok", "012-0", "ok", redis={"nightly_checkin_active:" + NUMBER: "chk00001"}, checkin_response=["ok", "TUDO PAGO"]))
add(scenario("checkin_ativo_escape", "012-0", "quanto devo?", redis={"nightly_checkin_active:" + NUMBER: "chk00001"}, intent="Menu de Ajuda"))
add(scenario("checkin_ativo_escape_maiusculas", "012-0", "CRIAR evento", redis={"nightly_checkin_active:" + NUMBER: "chk00001"}, intent="Menu de Ajuda"))

# ---- secoes das sub-entregas (whatsapp_harness_sections/*.py): rodam neste escopo, depois dos cenarios acima, e gravam o proprio JSON
_SECTIONS = os.path.join(HERE, "whatsapp_harness_sections")
if os.path.isdir(_SECTIONS):
    for _name in sorted(os.listdir(_SECTIONS)):
        if _name.endswith(".py"):
            with open(os.path.join(_SECTIONS, _name), encoding="utf-8") as _fh:
                exec(compile(_fh.read(), _name, "exec"), globals())

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({"api_key": API_KEY, "signature_key": SIGNATURE_KEY, "number": NUMBER, "scenarios": SCENARIOS}, fh, ensure_ascii=False, indent=1)
sys.stdout = _REAL_STDOUT
print("ok", OUT, len(SCENARIOS))
