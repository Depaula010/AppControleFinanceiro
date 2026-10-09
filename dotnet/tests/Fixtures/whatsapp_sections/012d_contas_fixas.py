# -*- coding: utf-8 -*-
"""
Secao do gerador de fixtures (escopo de generate_whatsapp_fixtures.py): formatadores da 012d (contas fixas, vencimentos e resposta ao
check-in noturno). Saida propria: whatsapp-fixedbills-fixtures.json. Tudo o que e texto vem do codigo REAL do Flask:
FixedBillsService (list_pending_bills_formatted, list_all_bills_formatted), format_vencimentos_message e
NightlyCheckinService.parse_checkin_response. Todos os valores sao FICTICIOS.
"""
import collections as _co
import datetime as _dt
import decimal as _decimal
import textwrap as _textwrap

FB = {}

_prev_utils = sys.modules.get("app.utils")
_cur = load("currency_formatter.py", source("shared", "formatters", "currency_formatter.py"))


def _no_locale(*a, **k):
    raise ValueError("locale")


_cur["locale"].currency = _no_locale  # o container nao tem pt_BR: vale o formato manual
_df = load("date_formatter.py", source("shared", "formatters", "date_formatter.py"))
stub_module("app.utils", formatar_moeda=_cur["formatar_moeda"], formatar_mes_ano_pt=_df["formatar_mes_ano_pt"])


class _Fixed(_dt.date):
    _today = (2026, 10, 7)

    @classmethod
    def today(cls):
        return cls(*_Fixed._today)


_fbill = load("fixed_bills_service.py", source("services", "fixed_bills_service.py"))
_fbill["date"] = _Fixed
_svc = _fbill["FixedBillsService"]


def _extract(parts, names, class_name=None):
    src = source(*parts)
    tree = ast.parse(src)
    ns = {"re": __import__("re")}
    scope = tree.body
    if class_name:
        scope = [n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == class_name][0].body
    for node in scope:
        if isinstance(node, ast.FunctionDef) and node.name in names:
            code = _textwrap.dedent(ast.get_source_segment(src, node, padded=True))
            exec(compile(code, node.name, "exec"), ns)
    return ns


_vf = _extract(("services", "finance_service.py"), {"format_vencimentos_message"})
_ck = _extract(("services", "nightly_checkin_service.py"), {"parse_checkin_response"}, "NightlyCheckinService")


def _dec(value):
    return None if value is None else _decimal.Decimal(value)


# ------------------------------------------------------------------ contas fixas pendentes
_Pending = _co.namedtuple(
    "Pending", "id descricao valor_previsto dia_execucao tipo_agendamento categoria nome_conta tipo_conta nome_grupo ja_executado")


def _pending(today, rows):
    _Fixed._today = today
    original = _svc.__dict__["get_pending_bills_for_month"]
    _svc.get_pending_bills_for_month = staticmethod(lambda conn, uid, mes=None, ano=None: [
        _Pending(i + 1, r["descricao"], _dec(r["valor"]), r["dia"], "FIXO", r["categoria"], "Conta", r["tipo_conta"], r["nome_grupo"], 0)
        for i, r in enumerate(rows)])
    try:
        return _svc.list_pending_bills_formatted(None, 1)
    finally:
        _svc.get_pending_bills_for_month = original


def _p(descricao, valor, dia, tipo_conta="Conta Corrente", grupo="Despesa", categoria="Moradia"):
    return {"descricao": descricao, "valor": valor, "dia": dia, "categoria": categoria, "tipo_conta": tipo_conta, "nome_grupo": grupo}


_P_EXPENSES = [_p("Aluguel", "1500.00", 1), _p("Internet", "99.90", 7), _p("Luz", "230.45", 8), _p("Água", "80.10", 10),
               _p("Seguro", "310.00", 11), _p("Condomínio", "450.00", 31), _p("Sem valor", None, 20)]
_P_CARD = [_p("Netflix", "55.90", 15, "Cartão de Crédito", "Despesa", "Lazer"), _p("Spotify", "21.90", 3, "Cartão de Crédito", "Despesa", "Lazer"),
           _p("Academia", "119.90", 7, "Cartão de Crédito", "Despesa", "Saúde"), _p("Seguro cartão", "10.00", 9, "Cartão de Crédito", "Despesa", "Lazer")]
_P_INCOME = [_p("Salário", "5000.00", 5, grupo="Renda", categoria="Salário"), _p("Aluguel recebido", "1200.50", 7, grupo="Renda", categoria="Aluguel"),
             _p("Freela", "800.00", 25, grupo="Renda", categoria="Extra"), _p("Cartão renda", "1.00", 20, "Cartão de Crédito", "Renda", "Extra")]
_P_BIG = [_p("Casa", "1234567.89", 12), _p("Pequena", "0.01", 12), _p("Quase", "0.005", 12)]
_P_INVALID_DAYS = [_p("Dia zero", "10.00", 0), _p("Dia 29", "10.00", 29), _p("Dia 30", "20.00", 30), _p("Dia 31", "30.00", 31), _p("Dia 28", "40.00", 28)]

FB["pending"] = []
for _today in [(2026, 10, 7), (2026, 2, 10), (2026, 12, 31), (2026, 3, 1)]:
    for _label, _rows in [("vazio", []), ("despesas", _P_EXPENSES), ("cartao", _P_CARD), ("receitas", _P_INCOME), ("tudo", _P_EXPENSES + _P_CARD + _P_INCOME),
                          ("grandes", _P_BIG), ("dias_invalidos", _P_INVALID_DAYS), ("receita_e_cartao", _P_CARD + _P_INCOME)]:
        FB["pending"].append({"label": _label, "today": "%04d-%02d-%02d" % _today, "rows": _rows, "text": _pending(_today, _rows)})

# ------------------------------------------------------------------ catalogo completo
_Catalog = _co.namedtuple(
    "Catalog", "id descricao valor_previsto dia_execucao mes_execucao periodicidade tipo_agendamento categoria nome_conta tipo_conta nome_grupo")


def _catalog(rows):
    original = _svc.__dict__["get_all_scheduled_bills"]
    _svc.get_all_scheduled_bills = staticmethod(lambda conn, uid: [
        _Catalog(i + 1, r["descricao"], _dec(r["valor"]), r["dia"], r["mes"], r["periodicidade"], "FIXO", r["categoria"], "Conta", r["tipo_conta"], r["nome_grupo"])
        for i, r in enumerate(rows)])
    try:
        return _svc.list_all_bills_formatted(None, 1)
    finally:
        _svc.get_all_scheduled_bills = original


def _c(descricao, valor, dia, periodicidade="MENSAL", mes=None, tipo_conta="Conta Corrente", grupo="Despesa", categoria="Moradia"):
    return {"descricao": descricao, "valor": valor, "dia": dia, "mes": mes, "periodicidade": periodicidade, "categoria": categoria,
            "tipo_conta": tipo_conta, "nome_grupo": grupo}


_C_MIX = [
    _c("Feira", "120.00", 6, "SEMANAL"), _c("Netflix semanal", "10.10", 2, "SEMANAL", tipo_conta="Cartão de Crédito"), _c("Mesada", "50.00", 1, "SEMANAL", grupo="Renda"),
    _c("Faxina", "200.00", 15, "QUINZENAL"), _c("Streaming", "33.33", 10, "QUINZENAL", tipo_conta="Cartão de Crédito"), _c("Bico", "300.00", 5, "QUINZENAL", grupo="Renda"),
    _c("Aluguel", "1500.00", 5), _c("Internet", "99.90", 10), _c("Academia", "119.90", 12, tipo_conta="Cartão de Crédito"), _c("Salário", "5000.00", 5, grupo="Renda"),
    _c("IPTU", "1200.00", 10, "ANUAL", 3), _c("Seguro", "900.00", 15, "ANUAL", None, tipo_conta="Cartão de Crédito"), _c("Bônus", "2000.00", 20, "ANUAL", 12, grupo="Renda"),
    _c("Mês zero", "1.00", 1, "ANUAL", 0), _c("Mês 13", "1.00", 1, "ANUAL", 13),
]
_C_SETS = [
    ("vazio", []),
    ("mix", _C_MIX),
    ("sem_periodicidade", [_c("Sem periodo", "10.00", 9, None), _c("Vazia", "5.50", 9, "")]),
    ("diaria_oculta", [_c("Café", "5.00", 1, "DIARIA"), _c("Aluguel", "1500.00", 5)]),
    ("so_diaria", [_c("Café", "5.00", 1, "DIARIA")]),
    ("so_receitas", [_c("Salário", "5000.00", 5, grupo="Renda"), _c("Freela", "300.00", 9, "SEMANAL", grupo="Renda")]),
    ("so_anuais", [_c("IPTU", "1200.00", 10, "ANUAL", 3)]),
    ("valor_nulo", [_c("Sem valor", None, 3)]),
    ("soma_flutuante", [_c("A", "0.10", 1, "SEMANAL"), _c("B", "0.20", 2, "SEMANAL"), _c("C", "0.30", 3, "QUINZENAL"), _c("D", "1234567.89", 4)]),
]
FB["catalog"] = [{"label": label, "rows": rows, "text": _catalog(rows)} for label, rows in _C_SETS]

# ------------------------------------------------------------------ vencimentos


class _O:
    def __init__(self, **kw):
        self.__dict__.update(kw)


def _due_bill(descricao, valor, dia, grupo="Despesa"):
    return {"descricao": descricao, "valor": valor, "dia": dia, "nome_grupo": grupo}


def _due_invoice(cartao, valor, vencimento):
    return {"nome_conta": cartao, "valor": valor, "data_vencimento": vencimento}


def _due(periodo, reference, bills, invoices):
    data = {
        "contas_fixas": [_O(descricao=b["descricao"], valor_previsto=_dec(b["valor"]), dia_execucao=b["dia"], nome_grupo=b["nome_grupo"]) for b in bills],
        "faturas": [_O(nome_conta=i["nome_conta"], valor_fatura=_dec(i["valor"]), data_vencimento=_dt.date.fromisoformat(i["data_vencimento"])) for i in invoices],
    }
    return _vf["format_vencimentos_message"](data, periodo, _dt.date.fromisoformat(reference))


_D_INCOME = [_due_bill("Salário", "5000.00", 7, "Renda"), _due_bill("Freela", None, 7, "Renda")]
_D_EXPENSE = [_due_bill("Aluguel", "1500.00", 7), _due_bill("Internet", "99.90", 8), _due_bill("Sem valor", None, 9)]
_D_INVOICE = [_due_invoice("Cartão Inter", "1250.50", "2026-10-07"), _due_invoice("Nubank", "0", "2026-10-09"), _due_invoice("Zero", None, "2026-10-10")]
_D_SETS = [
    ("vazio", [], []), ("receitas", _D_INCOME, []), ("despesas", _D_EXPENSE, []), ("faturas", [], _D_INVOICE),
    ("tudo", _D_INCOME + _D_EXPENSE, _D_INVOICE), ("despesas_e_faturas", _D_EXPENSE, _D_INVOICE), ("saldo_negativo", _D_EXPENSE, _D_INVOICE[:1]),
    ("receitas_e_faturas", _D_INCOME[:1], _D_INVOICE[:1]),
]
FB["due"] = []
for _periodo in ["HOJE", "AMANHÃ", "NOS PRÓXIMOS 7 DIAS"]:
    for _reference in ["2026-10-07", "2026-12-31"]:
        for _label, _bills, _invoices in _D_SETS:
            FB["due"].append({"label": _label, "period": _periodo, "reference": _reference, "bills": _bills, "invoices": _invoices,
                              "text": _due(_periodo, _reference, _bills, _invoices)})

# ------------------------------------------------------------------ resposta do check-in (parse_checkin_response)
_PARSE_MESSAGES = [
    "ok", "OK", "Ok!", " ok ", "👍", "👌", "✅", "✓", "☑", "sim", "Sim, tudo certo", "tudo pago", "Tudo Ok", "tudo certo", "confirmar", "assim", "okay", "joaquim",
    "1", "2", "1 e 3", "1, 2", "3, 1", "1, 2, 3", "1 2 3", "1,1,2", "pagou 2", "paguei 1 e 3", "01", "0", "4", "1 e 4", "0 1", "1, 99", "99999999999999999999",
    "٣", "1️⃣", "2️⃣ e 3", "não", "Não", "nao", "NÃO", "depois", "Depois", "ainda não", "ainda nao", "amanhã", "amanha", "não paguei 1", "1 não", "ok não",
    "não ok", "talvez", "", "   ", "abc", "xyz 1", "pago", "paguei", "tudo", "hoje", "bom dia",
]
FB["parse"] = []
for _message in _PARSE_MESSAGES:
    for _total in (1, 3, 5):
        _kind, _payload = _ck["parse_checkin_response"](_message, _total)
        FB["parse"].append({"message": _message, "total": _total, "kind": _kind, "payload": _payload})

# ------------------------------------------------------------------ datas do lancamento (mark_bills_as_paid)
# A regra do Python e a mesma funcao em dois ramos; os casos saem da implementacao real em Python por replica minima do trecho.
_mark_src = source("services", "nightly_checkin_service.py")
FB["payment_dates"] = []
for _today in [(2026, 10, 7), (2026, 10, 1), (2026, 3, 31)]:
    _hoje = _dt.date(*_today)
    for _real in [None, "", "2026-10-07", "2026-10-06", "2026-10-05", "2026-10-08", "2026-09-30", "2026-03-31", "2026-03-30"]:
        for _dia in [1, 5, 6, 7, 30, 31]:
            if _real:
                _venc = _dt.date.fromisoformat(_real)
                _data_tx = _venc if (_hoje - _venc).days <= 1 else _hoje
            elif 0 <= _hoje.day - _dia <= 1:
                _data_tx = _hoje.replace(day=_dia)
            else:
                _data_tx = _hoje
            FB["payment_dates"].append({"today": _hoje.isoformat(), "real": _real, "day": _dia, "date": _data_tx.isoformat()})
assert "data_tx = data_venc_real if dias_atraso_real <= 1 else hoje" in _mark_src  # a regra acima e a do arquivo
assert "if 0 <= hoje.day - dia_venc <= 1:" in _mark_src

with open(os.path.join(HERE, "whatsapp-fixedbills-fixtures.json"), "w", encoding="utf-8", newline="\n") as _fh:
    json.dump(FB, _fh, ensure_ascii=False, indent=1)

if _prev_utils is not None:
    sys.modules["app.utils"] = _prev_utils
