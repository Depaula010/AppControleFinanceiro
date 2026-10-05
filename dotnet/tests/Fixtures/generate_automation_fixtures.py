# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/automation-fixtures.json (feature 011) com a referencia do Flask e do rapidfuzz:

  - sanitize_input (app/shared/validators/input_sanitizer.py)
  - formatar_moeda no ramo de fallback manual (app/shared/formatters/currency_formatter.py)
  - float() do Python e seus erros (mensagens que `/api/transacao` devolve ao cliente)
  - rapidfuzz 3.14.3: ratio, partial_ratio, token_sort/set, partial_token_*, WRatio e extractOne
  - budget_validation_service: periodos e mensagens (carregado por texto, conexao simulada)
  - gemini_service: prompts de extract_from_notification, categorize_transaction e extract_transaction_details
  - transaction_confirmation_service.format_confirmation_message
  - fixed_bills_service: find_matching_bill (limpeza do texto e casamento) e mark_bill_as_paid_response
  - json.dumps da lista de categorias (parte do prompt de categorizacao)

Uso: python generate_automation_fixtures.py   (requer: pip install rapidfuzz==3.14.3, so para regenerar)
Todos os valores sao FICTICIOS. A saida e versionada; o CI nao precisa de Python.
Os modulos do Flask sao carregados por texto, sem importar o pacote `app` (que puxa dependencias de producao).
"""
import json
import locale
import os
import sys
import types

from rapidfuzz import fuzz, process

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "automation-fixtures.json")
APP = os.path.join(HERE, "..", "..", "..", "app")


def source(*parts):
    with open(os.path.join(APP, *parts), encoding="utf-8") as fh:
        return fh.read()


def load(name, text, extra=None):
    namespace = dict(extra or {})
    exec(compile(text, name, "exec"), namespace)
    return namespace


# ---------------------------------------------------------------- sanitize_input
sanitizer = load("input_sanitizer.py", source("shared", "validators", "input_sanitizer.py"))
sanitize_input = sanitizer["sanitize_input"]

sanitize_inputs = [
    ("", 100, False), ("   ", 100, False), ("Mercado", 100, False), ("  Mercado   do  Zé  ", 100, False),
    ("<script>alert('xss')</script>", 200, False), ("a{b}c\\d$e`f|g;h&i<j>k", 200, False),
    ("a{b}c\\d$e`f|g;h&i<j>k", 200, True), ("Olá, mundo!", 5, False), ("abcdef", 3, False),
    ("tab\there\nnova\rlinha", 100, False), ("ctrl\x01\x02\x1fok\x7f", 100, False),
    ("emoji 😀😀😀😀😀", 8, False), ("😀😀😀😀😀", 3, True), ("  <>  ", 100, False), ("a   b   c", 100, False),
    ("x" * 120, 100, False), ("2 sorvetes & 1 café; tudo", 200, True), ("linha1\n\n\nlinha2", 100, True),
    (" sep ", 100, False), ("Ação 100% $", 20, False),
]
sanitize_cases = [{"text": t, "max": m, "allow_special": a, "result": sanitize_input(t, m, a)} for t, m, a in sanitize_inputs]

# ---------------------------------------------------------------- formatar_moeda (fallback manual)
currency_src = source("shared", "formatters", "currency_formatter.py")
currency = load("currency_formatter.py", currency_src)


def _raise(*args, **kwargs):
    raise ValueError("Currency formatting is not possible using the 'C' locale.")


currency["locale"].currency = _raise
formatar_moeda = currency["formatar_moeda"]
money_values = [None, 0, 0.0, 1.5, 12, 1234.56, 1234567.891, 0.005, 0.015, 0.125, 2.675, 100.0, 49.9, 999999.995, 1e9, 15.9, -5, -1234.5]
money_cases = [{"value": v, "text": formatar_moeda(v)} for v in money_values]
grouped_values = [0, 1.5, 12, 1234.56, 1234567.891, 0.005, 0.125, 0.375, 2.675, 1000, 999.995, 150.0]
grouped_cases = [{"value": v, "text": f"{v:,.2f}"} for v in grouped_values]

# ---------------------------------------------------------------- float() do Python
float_inputs = [
    "abc", "1,5", "  12.5 ", "1_000", "inf", "-inf", "nan", "NaN", "Infinity", "-infinity", "1e3", "", "  ", "0x10", "١٢",
    "1__0", "15.90", "+7", "-0", "1e400", ".5", "5.", "'", 'a"b', "it's", "a'b\"c", "tab\t", "nl\n", "back\\slash", "ç",
    True, False, 0, 5, -5, 1.5, 15.9, [1], [], {"a": 1}, {},
]
float_cases = []
for v in float_inputs:
    try:
        r = float(v)
        float_cases.append({"input": v, "ok": True, "value": repr(r)})
    except (ValueError, TypeError) as e:
        message = str(e)
        if sys.version_info < (3, 11):
            # a producao roda Python 3.11 (Dockerfile: python:3.11-slim), cuja mensagem diz "a real number"
            message = message.replace("must be a string or a number", "must be a string or a real number")
        float_cases.append({"input": v, "ok": False, "error": message})

# ---------------------------------------------------------------- rapidfuzz
fuzz_pairs = [
    ("luz", "Luz"), ("luz", "Conta de Luz"), ("net", "Netflix"), ("net", "Net"), ("agua", "Água"), ("agua", "Agua"),
    ("aluguel", "Aluguel"), ("aluguel", "aluguel"), ("internet", "Internet Vivo Fibra"), ("condominio", "Condomínio"),
    ("netflix", "Netflix"), ("spotify", "Spotify Premium Familia"), ("escola", "Escola do Joao"), ("baba", "Babá"),
    ("a", "abc"), ("ab", "ba"), ("", "abc"), ("abc", ""), ("", ""), ("seguro do carro", "Seguro Carro"),
    ("plano de saude", "Plano Saude Unimed Familia Completo"), ("iptu", "IPTU 2026 parcela"), ("gas", "Gás"),
    ("energia eletrica", "Energia"), ("celular", "Plano Celular Claro 50GB"), ("academia", "Smart Fit"),
    ("fatura do cartao", "Cartão Inter"), ("mensalidade", "Mensalidade Faculdade de Engenharia Civil UFMG Campus Pampulha"),
    ("x" * 30, "y" * 5), ("abcdefghij", "abcxefghij"), ("hello world", "world hello"), ("new york mets vs atlanta braves", "braves vs mets"),
    ("tv a cabo", "TV a Cabo Sky"), ("pagamento de luz", "Luz"), ("faculdade", "Faculdade"), ("ç", "c"), ("café", "cafe"),
    ("a b c d e f", "f e d c b a"), ("sky", "Sky Tv"), ("inglês", "Inglês Wizard"), ("passeio com cachorro", "Passeio Cachorro"),
    ("dog walker", "Dog"), ("babysitter", "Baba"), ("pet", "Pet Shop Banho e Tosa Mensal"),
]
# pares aleatorios (semente fixa) para exercitar os ramos de WRatio (len_ratio < 1.5, <= 8 e > 8) e os tokens
import random

_rng = random.Random(20261005)
_words = ["luz", "Luz", "agua", "Água", "net", "Netflix", "internet", "Vivo", "fibra", "aluguel", "casa", "condominio", "Condomínio", "escola",
          "do", "da", "de", "conta", "plano", "saude", "Unimed", "celular", "claro", "tv", "cabo", "sky", "gás", "seguro", "carro", "iptu",
          "parcela", "faculdade", "engenharia", "mensalidade", "academia", "pet", "shop", "ç", "ão", "a", "b"]
for _ in range(400):
    q = " ".join(_rng.choice(_words) for _ in range(_rng.randint(1, 3))).lower()
    c = " ".join(_rng.choice(_words) for _ in range(_rng.randint(1, 6)))
    fuzz_pairs.append((q, c))
fuzz_cases = []
for q, c in fuzz_pairs:
    fuzz_cases.append({
        "query": q, "choice": c,
        "ratio": fuzz.ratio(q, c), "partial_ratio": fuzz.partial_ratio(q, c),
        "token_sort_ratio": fuzz.token_sort_ratio(q, c), "token_set_ratio": fuzz.token_set_ratio(q, c),
        "partial_token_sort_ratio": fuzz.partial_token_sort_ratio(q, c), "partial_token_set_ratio": fuzz.partial_token_set_ratio(q, c),
        "wratio": fuzz.WRatio(q, c),
    })

extract_sets = [
    ("luz", ["Luz", "Aluguel", "Internet"]),
    ("luz", ["Conta de Luz", "Aluguel"]),
    ("agua", ["Água", "Aluguel"]),
    ("internet", ["Internet", "Internet Vivo", "Intercept"]),
    ("net", ["Netflix", "Net", "NET Claro"]),
    ("escola", ["Escola do Joao", "Escolha", "Escola"]),
    ("xyz", ["Luz", "Aluguel"]),
    ("aluguel", ["Aluguel", "aluguel", "Aluguel"]),
    ("condominio", ["Condomínio", "Condominio Bloco B", "Aluguel"]),
    ("plano", ["Plano de Saude", "Plano Celular", "Planta"]),
    ("spotify", []),
    ("gas", ["Gás", "Gas Encanado", "Gasolina"]),
]
extract_cases = []
for q, choices in extract_sets:
    # o Python usa dict(descricao -> linha): repetidas colapsam (a ultima linha vence); a ordem e a da 1a insercao
    ordered = list(dict.fromkeys(choices))
    result = process.extractOne(q, ordered, scorer=fuzz.WRatio, score_cutoff=65)
    extract_cases.append({
        "query": q, "choices": choices,
        "match": None if result is None else result[0], "score": None if result is None else result[1],
    })

# ---------------------------------------------------------------- budget_validation_service
budget_src = source("services", "finance", "budget_validation_service.py")
budget_src = budget_src.replace(
    "from ._database import text, date, timedelta, monthrange, Connection",
    "from datetime import date, timedelta\nfrom calendar import monthrange\ntext = lambda s: s\nConnection = object",
)
budget = load("budget_validation_service.py", budget_src)

period_cases = []
for periodicity in ["SEMANAL", "QUINZENAL", "MENSAL", "ANUAL", "OUTRA"]:
    for y, m, d in [(2026, 10, 5), (2026, 10, 15), (2026, 10, 16), (2026, 2, 28), (2028, 2, 29), (2026, 12, 31), (2026, 1, 1), (2026, 10, 4), (2026, 10, 11)]:
        import datetime as _dt
        start, end = budget["calcular_periodo_pote"](periodicity, _dt.date(y, m, d))
        period_cases.append({"periodicity": periodicity, "date": f"{y:04d}-{m:02d}-{d:02d}", "start": start.isoformat(), "end": end.isoformat()})


class FakeResult:
    def __init__(self, rows=None, scalar=None):
        self._rows, self._scalar = rows, scalar

    def fetchall(self):
        return self._rows

    def scalar(self):
        return self._scalar


class FakeConn:
    def __init__(self, pots, spends):
        self.pots, self.spends, self.i = pots, spends, 0

    def execute(self, sql, params=None):
        if "PotesDeGastos" in sql and "SUM" not in sql:
            return FakeResult(rows=self.pots)
        value = self.spends[self.i]
        self.i += 1
        return FakeResult(scalar=value)


from decimal import Decimal

budget_scenarios = [
    {"name": "sem_pote", "pots": [], "spends": [], "amount": 50.0},
    {"name": "dentro", "pots": [(1, "Mercado", Decimal("500.00"), "MENSAL", None)], "spends": [Decimal("-100.00")], "amount": 50.0},
    {"name": "estoura_um", "pots": [(1, "Mercado", Decimal("500.00"), "MENSAL", None)], "spends": [Decimal("-480.00")], "amount": 50.0},
    {"name": "estoura_milhar", "pots": [(2, "Lazer", Decimal("1500.00"), "SEMANAL", None)], "spends": [Decimal("-1480.5")], "amount": 1234.56},
    {"name": "dois_potes", "pots": [(1, "Mercado", Decimal("500.00"), "MENSAL", None), (3, "Alimentacao", Decimal("200.00"), "QUINZENAL", None)],
     "spends": [Decimal("-480.00"), Decimal("-199.00")], "amount": 50.0},
    {"name": "um_estoura_um_nao", "pots": [(1, "Mercado", Decimal("500.00"), "MENSAL", None), (3, "Alimentacao", Decimal("2000.00"), "ANUAL", None)],
     "spends": [Decimal("-480.00"), Decimal("-100.00")], "amount": 50.0},
    {"name": "limite_zero", "pots": [(4, "Zero", Decimal("0.00"), "MENSAL", None)], "spends": [Decimal("0")], "amount": 10.0},
    {"name": "exato_no_limite", "pots": [(1, "Mercado", Decimal("500.00"), "MENSAL", None)], "spends": [Decimal("-450.00")], "amount": 50.0},
    {"name": "percentual_meio_par", "pots": [(5, "Tie", Decimal("3.00"), "MENSAL", None)], "spends": [Decimal("-1.00")], "amount": 2.0125},
    {"name": "sem_gasto", "pots": [(6, "Vazio", Decimal("100.00"), "MENSAL", None)], "spends": [None], "amount": 150.0},
]
budget_cases = []
for sc in budget_scenarios:
    out = budget["validate_budget"](FakeConn(sc["pots"], sc["spends"]), 1, 10, sc["amount"], _dt.date(2026, 10, 5))
    budget_cases.append({
        "name": sc["name"],
        "pots": [{"id": p[0], "name": p[1], "limit": str(p[2]), "periodicity": p[3]} for p in sc["pots"]],
        "spends": [None if s is None else str(s) for s in sc["spends"]],
        "amount": sc["amount"],
        "requires_confirmation": out.requer_confirmacao,
        "message": out.mensagem,
        "validations": [{"id": v.pote_id, "spent": v.valor_gasto_atual, "after": v.valor_apos_transacao,
                         "percent": v.percentual_usado, "exceeds": v.ultrapassaria_limite} for v in out.validacoes],
    })

# ---------------------------------------------------------------- gemini_service (prompts)
gemini_src = source("services", "gemini_service.py").replace("from app import gemini_model", "gemini_model = _STUB_MODEL")


class _Response:
    def __init__(self, text):
        self.text = text
        self.prompt_feedback = ""


class _Model:
    def __init__(self):
        self.prompts = []

    def generate_content(self, prompt):
        self.prompts.append(prompt)
        return _Response("1")


_model = _Model()
for mod in ("app", "app.services", "app.services.gemini_cache_service"):
    sys.modules.setdefault(mod, types.ModuleType(mod))


class _Cache:
    def cached_generate_content(self, model, prompt, cfg):
        return model.generate_content(prompt)


sys.modules["app.services.gemini_cache_service"].gemini_cache_service = _Cache()
# imports locais de _obter_modelo_gemini_para_usuario (resolvidos antes do teste de usuario_id nulo)
for mod in ("app.services.gerenciador_chaves_api", "google", "google.generativeai"):
    sys.modules.setdefault(mod, types.ModuleType(mod))
class _Keys:
    @staticmethod
    def obter_chave_api(usuario_id, provedor):
        return "chave-de-teste", "sistema"


sys.modules["app.services.gerenciador_chaves_api"].GerenciadorChavesApi = _Keys
sys.modules["google"].generativeai = sys.modules["google.generativeai"]
gemini = load("gemini_service.py", gemini_src, {"_STUB_MODEL": _model})

categories = [
    {"id": 1, "nome_sub": "Supermercado", "nome_macro": "Alimentação"},
    {"id": 2, "nome_sub": "Outros", "nome_macro": "Despesas Gerais"},
    {"id": 3, "nome_sub": "Quitação de Empréstimos (Principal)", "nome_macro": "Dívidas \"x\" \\ 😀"},
]
prompt_cases = []
_model.prompts.clear()
_model.generate_content_text = None
gemini["extract_from_notification"]('Compra aprovada R$ 45,90 no "MERCADO" ç', None)
prompt_cases.append({"fn": "extract_from_notification", "args": {"texto": 'Compra aprovada R$ 45,90 no "MERCADO" ç'}, "prompt": _model.prompts[-1]})
gemini["categorize_transaction"](categories, 'Padaria "Pão Quente" - 2 pães', "Despesa", 2, 7)
prompt_cases.append({"fn": "categorize_transaction", "args": {"categories": categories, "descricao": 'Padaria "Pão Quente" - 2 pães', "tipo": "Despesa", "fallback": 2}, "prompt": _model.prompts[-1]})
gemini["categorize_transaction"]([], "x", "Renda", None, None)
prompt_cases.append({"fn": "categorize_transaction", "args": {"categories": [], "descricao": "x", "tipo": "Renda", "fallback": None}, "prompt": _model.prompts[-1]})
gemini["extract_transaction_details"]("Paguei 150.5 de Conta de Água", "Despesa", 7)
prompt_cases.append({"fn": "extract_transaction_details", "args": {"texto": "Paguei 150.5 de Conta de Água", "intent": "Despesa"}, "prompt": _model.prompts[-1]})
categories_json = [{"categories": categories, "json": json.dumps(categories)}, {"categories": [], "json": json.dumps([])}]

# ---------------------------------------------------------------- transaction_confirmation_service
sys.modules["app.services.redis_service"] = types.ModuleType("app.services.redis_service")
sys.modules["app.services.redis_service"].redis_service = object()
sys.modules["app.services"].finance_service = types.ModuleType("finance_service")
utils = types.ModuleType("app.utils")
utils.formatar_moeda = formatar_moeda
sys.modules["app.utils"] = utils
confirmation = load("transaction_confirmation_service.py", source("services", "transaction_confirmation_service.py"))
fmt = confirmation["TransactionConfirmationService"].format_confirmation_message

cats = [{"id": 1, "nome_sub": "Supermercado", "nome_macro": "Alimentação"}, {"id": 2, "nome_sub": "Outros", "nome_macro": "Despesas Gerais"}]
confirmation_scenarios = [
    {"name": "automate_despesa", "data": {"usuario_id": 1, "conta_id": 3, "categoria_id": 1, "fatura_id": None, "descricao": "Mercado Central", "valor_db": -45.9,
                                          "valor_original": 45.9, "tipo_transacao": "Despesa", "data_transacao": "2026-10-05", "origem": "automate"}},
    {"name": "automate_renda", "data": {"usuario_id": 1, "conta_id": 3, "categoria_id": 2, "fatura_id": None, "descricao": "Salario", "valor_db": 3000.0,
                                        "valor_original": 3000.0, "tipo_transacao": "Renda", "data_transacao": "2026-10-05", "origem": "automate"}},
    {"name": "automate_categoria_desconhecida", "data": {"usuario_id": 1, "conta_id": 3, "categoria_id": 99, "fatura_id": 5, "descricao": "X", "valor_db": -1234567.5,
                                                         "valor_original": 1234567.5, "tipo_transacao": "Despesa", "data_transacao": "2026-10-05", "origem": "automate"}},
]
for pay, label in [("credito", "credito"), ("debito", "debito"), ("pix", "pix"), ("dinheiro", "dinheiro")]:
    confirmation_scenarios.append({"name": f"api_{label}_com_descricao", "data": {
        "usuario_id": 1, "conta_id": 3, "conta_nome": "Cartão Inter", "conta_tipo": "Cartão de Crédito", "categoria_id": 1,
        "fatura_id": 9 if pay == "credito" else None, "local": "Sorveteria", "descricao": "2 sorvetes", "descricao_final": "Sorveteria - 2 sorvetes",
        "valor_db": -15.9, "valor_original": 15.9, "tipo_transacao": "Despesa", "tipo_pagamento": pay, "data_transacao": "2026-10-05",
        "origem": "api_endpoint", "budget_warning": None}})
confirmation_scenarios.append({"name": "api_sem_descricao_credito_sem_fatura", "data": {
    "usuario_id": 1, "conta_id": 3, "conta_nome": "Nubank", "conta_tipo": "Conta Corrente", "categoria_id": 2, "fatura_id": None, "local": "Padaria",
    "descricao": None, "descricao_final": "Padaria", "valor_db": -8.0, "valor_original": 8.0, "tipo_transacao": "Despesa", "tipo_pagamento": "credito",
    "data_transacao": "2026-10-05", "origem": "api_endpoint", "budget_warning": None}})
confirmation_scenarios.append({"name": "api_com_aviso_de_orcamento", "data": {
    "usuario_id": 1, "conta_id": 3, "conta_nome": "Carteira", "conta_tipo": "Dinheiro", "categoria_id": 1, "fatura_id": None, "local": "Mercado",
    "descricao": None, "descricao_final": "Mercado", "valor_db": -50.0, "valor_original": 50.0, "tipo_transacao": "Despesa", "tipo_pagamento": "dinheiro",
    "data_transacao": "2026-10-05", "origem": "api_endpoint", "budget_warning": "Atenção: Esta despesa ultrapassará seu limite!\n\nPote: Mercado"}})
confirmation_cases = [{"name": s["name"], "data": s["data"], "id": "ab12cd34", "text": fmt(s["data"], cats, "ab12cd34")} for s in confirmation_scenarios]

# ---------------------------------------------------------------- fixed_bills_service
fb_src = source("services", "fixed_bills_service.py")
fb_src = fb_src.replace("from sqlalchemy import text", "text = lambda s: s").replace(
    "from app.utils import formatar_moeda, formatar_mes_ano_pt", "from _fb_utils import formatar_moeda, formatar_mes_ano_pt")
fb_utils = types.ModuleType("_fb_utils")
fb_utils.formatar_moeda = formatar_moeda
fb_utils.formatar_mes_ano_pt = lambda d: ""
sys.modules["_fb_utils"] = fb_utils
fixed = load("fixed_bills_service.py", fb_src)
FixedBillsService = fixed["FixedBillsService"]


class _Row:
    def __init__(self, id, descricao, valor_previsto, dia_execucao, tipo_agendamento, conta_id, categoria):
        self.id, self.descricao, self.valor_previsto = id, descricao, valor_previsto
        self.dia_execucao, self.tipo_agendamento, self.conta_id, self.categoria = dia_execucao, tipo_agendamento, conta_id, categoria


class _BillsConn:
    def __init__(self, rows):
        self.rows = rows

    def execute(self, sql, params=None):
        return FakeResult(rows=self.rows)


bills = [_Row(1, "Luz", Decimal("120.00"), 10, "FIXO", 2, "Energia"), _Row(2, "Aluguel", Decimal("1500.00"), 5, "FIXO", 2, "Moradia"),
         _Row(3, "Internet Vivo", Decimal("99.90"), 15, "LEMBRETE_VARIAVEL", 3, "Internet"), _Row(4, "netflix", Decimal("39.90"), 20, "FIXO", 3, "Streaming"),
         _Row(5, "Água", Decimal("80.00"), 12, "FIXO", 2, "Água"), _Row(6, "Condomínio", Decimal("400.00"), 7, "FIXO", 2, "Moradia"),
         _Row(7, "Luz", Decimal("55.00"), 11, "FIXO", 2, "Energia")]
match_inputs = ["luz", "Luz", "LUZ", "a luz", "o aluguel", "da luz", "do condominio", "de internet", "conta de agua", "conta netflix", "conta de conta de luz",
                "  conta  ", "xyz", "internet", "vivo", "netflix", "aluguel de apartamento", "agua", "Conta de Luz", "da conta de luz", "conta luz", "a o luz",
                "de a luz", "condomínio"]
match_cases = []
for q in match_inputs:
    m = FixedBillsService.find_matching_bill(_BillsConn(bills), 1, q)
    match_cases.append({"input": q, "result_id": None if m is None else m[0], "result_desc": None if m is None else m[1]})
paid_cases = [{"descricao": d, "valor": v, "categoria": c, "text": FixedBillsService.mark_bill_as_paid_response(d, v, c)}
              for d, v, c in [("Luz", 150.5, "Energia"), ("Aluguel", 1500, "Moradia"), ("Netflix", 39.9, "Streaming")]]

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({
        "sanitize": sanitize_cases, "money": money_cases, "grouped": grouped_cases, "float": float_cases,
        "fuzz": fuzz_cases, "extract_one": extract_cases, "periods": period_cases, "budget": budget_cases,
        "prompts": prompt_cases, "categories_json": categories_json, "confirmation": confirmation_cases,
        "bill_matching": {"bills": [{"id": b.id, "descricao": b.descricao, "valor": str(b.valor_previsto)} for b in bills], "cases": match_cases},
        "paid_message": paid_cases,
    }, fh, ensure_ascii=False, indent=1)
    fh.write("\n")
print("escrito", OUT)
