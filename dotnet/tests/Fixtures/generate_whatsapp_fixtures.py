# -*- coding: utf-8 -*-
"""
Gera as fixtures da feature 012 (webhook WhatsApp e intents) com a referencia do Flask:

  - src/MeuSecretario.Domain/Whatsapp/Prompts/*.txt : modelos dos prompts do Gemini (gemini_service.py), com marcadores
    no lugar das variaveis (⟦MSG⟧, ⟦CONTAS⟧...), mais o menu de ajuda (admin_intents.py)
  - tests/Fixtures/whatsapp-fixtures.json : prompts renderizados com entradas "dificeis", metadados de cache,
    process_confirmation_response, format_category_selection_message, extract_mentioned_account (palavras-chave),
    e as secoes acrescentadas pelas sub-entregas (formatadores).

Uso: python generate_whatsapp_fixtures.py   (so para regenerar; a saida e versionada e o CI nao precisa de Python)
Todos os valores sao FICTICIOS. Os modulos do Flask sao carregados por texto, sem importar o pacote `app`.
"""
import ast
import json
import os
import sys
import types

_REAL_STDOUT = sys.stdout
sys.stdout = open(os.devnull, "w", encoding="utf-8")  # os prints do Flask nao interessam
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "whatsapp-fixtures.json")
APP = os.path.join(HERE, "..", "..", "..", "app")
PROMPTS = os.path.join(HERE, "..", "..", "src", "MeuSecretario.Domain", "Whatsapp", "Prompts")


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


out = {}

# ------------------------------------------------------------------ gemini_service (prompts)
class _Response:
    def __init__(self, text):
        self.text = text
        self.prompt_feedback = ""


class _Model:
    def __init__(self):
        self.prompts = []
        self.reply = "{}"

    def generate_content(self, prompt):
        self.prompts.append(prompt)
        return _Response(self.reply)


MODEL = _Model()
CACHE_CALLS = []


class _Cache:
    def cached_generate_content(self, model, prompt, cfg):
        CACHE_CALLS.append(dict(cfg))
        return model.generate_content(prompt)


class _Keys:
    @staticmethod
    def obter_chave_api(usuario_id, provedor):
        return "chave-de-teste", "sistema"


for mod in ("app", "app.services", "app.services.gemini_cache_service", "app.services.gerenciador_chaves_api", "google", "google.generativeai"):
    sys.modules.setdefault(mod, types.ModuleType(mod))
sys.modules["app.services.gemini_cache_service"].gemini_cache_service = _Cache()
sys.modules["app.services.gerenciador_chaves_api"].GerenciadorChavesApi = _Keys
sys.modules["google"].generativeai = sys.modules["google.generativeai"]

gemini_src = source("services", "gemini_service.py").replace("from app import gemini_model", "gemini_model = _STUB_MODEL")


class _JsonShim:
    """json com dumps controlavel: os modelos guardam o marcador no lugar da lista de contas."""

    def __init__(self, dumps_token=None):
        self.dumps_token = dumps_token

    def dumps(self, value, *a, **k):
        return self.dumps_token if self.dumps_token is not None else json.dumps(value, *a, **k)

    loads = staticmethod(json.loads)


def gemini_ns(dumps_token=None):
    ns = load("gemini_service.py", gemini_src, {"_STUB_MODEL": MODEL})
    ns["json"] = _JsonShim(dumps_token)
    return ns


# (nome do arquivo, funcao, argumentos com marcadores, resposta do modelo para a funcao nao quebrar)
TEXT_PROMPTS = [
    ("intent", "get_message_intent", ["⟦MSG⟧", 7], '{"intent": "x"}'),
    ("transfer-details", "extract_transfer_details", ["⟦MSG⟧", "⟦CONTAS⟧", 7], "{}"),
    ("invoice-payment-details", "extract_fatura_payment_details", ["⟦MSG⟧", "⟦CONTAS⟧", 7], "{}"),
    ("category-query", "extract_category_query", ["⟦MSG⟧", 7], "{}"),
    ("period-query", "extract_period_query", ["⟦MSG⟧", 7], "{}"),
    ("chart-type", "extract_chart_type", ["⟦MSG⟧", 7], "{}"),
    ("payment-items", "extract_payment_items", ["⟦MSG⟧", 7], "{}"),
    ("calendar-query", "extract_calendar_query", ["⟦MSG⟧", 7], "{}"),
    ("event-creation", "extract_event_creation_details", ["⟦MSG⟧", 7], "{}"),
    ("event-deletion", "extract_event_deletion_query", ["⟦MSG⟧", 7], "{}"),
    ("time-filter", "extract_time_filter_query", ["⟦MSG⟧", 7], "{}"),
    ("notification-config", "extract_notification_config", ["⟦MSG⟧", 7], "{}"),
    ("free-time-query", "extract_free_time_query", ["⟦MSG⟧", 7], "{}"),
    ("invoice-query", "extract_fatura_query", ["⟦MSG⟧", "⟦CONTAS⟧", 7], "{}"),
    ("balance-query", "extract_saldo_query", ["⟦MSG⟧", "⟦CONTAS⟧", 7], "{}"),
    ("monthly-report-config", "extract_monthly_report_config", ["⟦MSG⟧", 7], "{}"),
    ("location-config", "extract_location_config", ["⟦MSG⟧", 7], "{}"),
    ("address-config", "extract_address_config", ["⟦MSG⟧", 7], "{}"),
    ("address-deletion", "extract_address_label_from_deletion", ["⟦MSG⟧", 7], "{}"),
]

os.makedirs(PROMPTS, exist_ok=True)
templates = []
for file_name, fn, args, reply in TEXT_PROMPTS:
    MODEL.reply = reply
    MODEL.prompts.clear()
    CACHE_CALLS.clear()
    gemini_ns("⟦CONTAS⟧")[fn](*args)
    template = MODEL.prompts[-1]
    with open(os.path.join(PROMPTS, file_name + ".txt"), "w", encoding="utf-8", newline="") as fh:
        fh.write(template)
    templates.append({"file": file_name, "fn": fn, "cache": CACHE_CALLS[-1] if CACHE_CALLS else None, "has_accounts": "⟦CONTAS⟧" in template})

tricky_messages = [
    "gastei 50 na padaria",
    'Compra "Pão Quente" {aprovada} \\ 100% 😀',
    "Olá, mundo! ç ã é — {x} {{y}} 'a' \"b\"",
    "",
]
accounts_samples = [
    [],
    [{"nome": "Nubank", "tipo": "Conta Corrente"}, {"nome": "Cartão Inter", "tipo": "Cartão de Crédito"}],
    [{"id": 1, "nome": "Carteira \"x\"", "tipo": "Dinheiro 😀"}],
]
prompt_cases = []
for file_name, fn, args, reply in TEXT_PROMPTS:
    MODEL.reply = reply
    uses_accounts = "⟦CONTAS⟧" in [a for a in args if isinstance(a, str)]
    for message in tricky_messages:
        for accounts in (accounts_samples if uses_accounts else [None]):
            MODEL.prompts.clear()
            real = [message if a == "⟦MSG⟧" else (accounts if a == "⟦CONTAS⟧" else a) for a in args]
            gemini_ns(None)[fn](*real)
            prompt_cases.append({"file": file_name, "message": message, "accounts": accounts, "json": json.dumps(accounts) if accounts is not None else None,
                                 "prompt": MODEL.prompts[-1]})
out["prompt_templates"] = templates
out["prompt_cases"] = prompt_cases

# ------------------------------------------------------------------ menu de ajuda (admin_intents.py)
tree = ast.parse(source("routes", "webhooks", "intents", "admin_intents.py"))
menu = None
for node in ast.walk(tree):
    if isinstance(node, ast.ClassDef) and node.name == "MenuAjudaIntent":
        for fn_node in node.body:
            if isinstance(fn_node, ast.FunctionDef) and fn_node.name == "format_response":
                for stmt in ast.walk(fn_node):
                    if isinstance(stmt, ast.Return) and isinstance(stmt.value, ast.Constant):
                        menu = stmt.value.value
assert menu and menu.startswith("📚 *MENU DE FUNCIONALIDADES*")
with open(os.path.join(PROMPTS, "help-menu.txt"), "w", encoding="utf-8", newline="") as fh:
    fh.write(menu)
out["help_menu"] = menu

# ------------------------------------------------------------------ transaction_confirmation_service
class FakeRedis:
    def __init__(self, initial=None):
        self.store = dict(initial or {})
        self.ops = []

    def set_with_ttl(self, key, value, ttl_seconds=300):
        self.ops.append(["set", key, ttl_seconds])
        self.store[key] = json.loads(json.dumps(value))
        return True

    def get(self, key):
        return self.store.get(key)

    def delete(self, key):
        self.ops.append(["delete", key])
        self.store.pop(key, None)
        return True


fake_redis = FakeRedis()
stub_module("app.services.redis_service", redis_service=fake_redis)
sys.modules["app.services"].finance_service = types.ModuleType("finance_service")
stub_module("app.utils", formatar_moeda=lambda v: "R$ %s" % v)
confirmation = load("transaction_confirmation_service.py", source("services", "transaction_confirmation_service.py"))
Confirmation = confirmation["TransactionConfirmationService"]

# redefine o redis do modulo carregado
confirmation["redis_service"] = fake_redis

base_pending = {
    "usuario_id": 1, "conta_id": 3, "conta_nome": "Nubank", "conta_tipo": "Conta Corrente", "categoria_id": 59, "fatura_id": None,
    "descricao": "Padaria", "descricao_final": None, "valor_db": -50.0, "valor_original": 50.0, "valor_total": None, "num_parcelas": None,
    "tipo_transacao": "Despesa", "tipo_pagamento": "debito", "data_transacao": "2026-10-07", "origem": "whatsapp", "extra": {"k": [1, "ç"]},
}
awaiting = dict(base_pending)
awaiting["awaiting_category_selection"] = True
awaiting["categoria_map"] = {"1": 11, "2": 22, "3": 33}

confirmation_scenarios = [
    ("sem_id", None, "ok", None),
    ("expirada", "abc12345", "ok", "missing"),
]
for message in ["ok", "OK", " ok ", "Confirmar", "CONFIRMA", "sim", "confirmar", "trocar", "Mudar", "alterar", "cancelar", "NÃO", "nao", "cancela",
                "5", "abc", "qualquer coisa", "", "  "]:
    confirmation_scenarios.append(("pendente_" + (message.strip().lower() or "vazio"), "abc12345", message, base_pending))
for message in ["2", " 3 ", "+1", "٢", "4", "0", "-1", "cancelar", "CANCELAR", "texto", "1_0", "ok", "", "1.5"]:
    confirmation_scenarios.append(("aguardando_" + (message.strip().lower() or "vazio"), "abc12345", message, awaiting))

confirmation_cases = []
for name, tx_id, message, pending in confirmation_scenarios:
    fake_redis.store.clear()
    fake_redis.ops.clear()
    if pending not in (None, "missing"):
        fake_redis.store["pending_tx:5511:abc12345"] = json.loads(json.dumps(pending))
        fake_redis.store["last_pending:5511"] = "abc12345"
    status, mensagem, dados = Confirmation.process_confirmation_response("5511", message, tx_id)
    confirmation_cases.append({
        "name": name, "id": tx_id, "message": message,
        "pending": None if pending in (None, "missing") else pending,
        "status": status, "reply": mensagem, "data": dados,
        "ops": list(fake_redis.ops), "stored": fake_redis.store.get("pending_tx:5511:abc12345"),
        "last_pending": fake_redis.store.get("last_pending:5511"),
    })
out["confirmation_cases"] = confirmation_cases

category_lists = [
    [],
    [{"id": 1, "nome_sub": "Supermercado", "nome_macro": "Alimentação"}, {"id": 2, "nome_sub": "Restaurante", "nome_macro": "Alimentação"},
     {"id": 3, "nome_sub": "Outros", "nome_macro": "Despesas Gerais"}, {"id": 4, "nome_sub": "Padaria", "nome_macro": "Alimentação"}],
]
category_cases = []
for cats in category_lists:
    text, mapping = Confirmation.format_category_selection_message(cats)
    category_cases.append({"categories": cats, "text": text, "map": mapping})
out["category_selection_cases"] = category_cases

# ------------------------------------------------------------------ extract_mentioned_account (so as palavras-chave)
captured = {}


class _Process:
    @staticmethod
    def extractOne(query, choices, scorer=None, score_cutoff=0):
        captured["query"] = query
        captured["choices"] = list(choices)
        captured["cutoff"] = score_cutoff
        return None


stub_module("rapidfuzz", fuzz=types.SimpleNamespace(WRatio=lambda a, b: 0), process=_Process)
sys.modules["sqlalchemy"] = types.ModuleType("sqlalchemy")
sys.modules["sqlalchemy"].text = lambda s: s
sys.modules["app"].db_engine = None
finance = load("finance_service.py", source("services", "finance_service.py"))


class _Row:
    def __init__(self, id, nome_conta, tipo_conta):
        self.id, self.nome_conta, self.tipo_conta = id, nome_conta, tipo_conta


class _Result:
    def __init__(self, rows):
        self.rows = rows

    def fetchall(self):
        return self.rows


class _Conn:
    def __init__(self, rows):
        self.rows = rows

    def execute(self, sql, params=None):
        return _Result(self.rows)


account_rows = [_Row(1, "Nubank", "Conta Corrente"), _Row(2, "Cartão Inter", "Cartão de Crédito")]
mention_messages = [
    "gastei 50 na padaria", "paguei 30 com o nubank", "recebi 100 no itau", "gastei com cartão", "usando a carteira azul grande pequena",
    "Gastei 20 do Nubank hoje", "entrou no Nubank salario", "caiu no banco", "oi", "", "gastei 10 pela conta do joao silva santos teixeira",
    "paguei com a conta de luz do mes", "recebi 500 usando nubank", "compra na na na", "pelo pela pelo", "gastei 5 da carteira",
]
mention_cases = []
for message in mention_messages:
    captured.clear()
    result = finance["extract_mentioned_account"](_Conn(account_rows), 1, message)
    mention_cases.append({"message": message, "query": captured.get("query"), "choices": captured.get("choices"), "cutoff": captured.get("cutoff"),
                          "result": result})
out["mention_cases"] = mention_cases

# ------------------------------------------------------------------ secoes das sub-entregas
SECTIONS_DIR = os.path.join(HERE, "whatsapp_sections")
if os.path.isdir(SECTIONS_DIR):
    for name in sorted(os.listdir(SECTIONS_DIR)):
        if name.endswith(".py"):
            with open(os.path.join(SECTIONS_DIR, name), encoding="utf-8") as fh:
                exec(compile(fh.read(), name, "exec"), globals())

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump(out, fh, ensure_ascii=False, indent=1)
sys.stdout = _REAL_STDOUT
print("ok", OUT)
