# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/jobs-fixtures.json com a implementação de REFERÊNCIA do Flask (spec 013, plan §5).

Cobre os textos que o usuário vê e que precisam ser idênticos: alertas financeiros, notificações de fatura, prompt e
limpeza do resumo matinal, intervalos livres e localizações do briefing, mensagem consolidada e sessão do check-in,
mensagem do relatório mensal. Os módulos do Flask são carregados por TEXTO (sem importar o pacote app, que puxa
dependências de produção): só as linhas de import são trocadas por stubs. Os dados são FICTÍCIOS.

Uso: python generate_jobs_fixtures.py
A saída é versionada; o CI não precisa de Python.
"""
import ast
import datetime as dt
import importlib.util
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.join(HERE, "..", "..", "..", "app")
OUT = os.path.join(HERE, "jobs-fixtures.json")


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def read(*parts):
    with open(os.path.join(APP, *parts), encoding="utf-8") as handle:
        return handle.read()


class FakeDate(dt.date):
    fixed = dt.date(2026, 3, 18)

    @classmethod
    def today(cls):
        return cls.fixed


def formatar_moeda(valor):
    # fallback manual de currency_formatter.py (locale pt_BR indisponível)
    if valor is None:
        return "R$ 0,00"
    return f"R$ {valor:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")


def d(text):
    return dt.date.fromisoformat(text)


# ---------------------------------------------------------------- alertas financeiros e faturas
alerts_mod = load_module(os.path.join(APP, "shared", "formatters", "financial_alert_formatter.py"), "fa")
invoice_mod = load_module(os.path.join(APP, "shared", "formatters", "invoice_notification_formatter.py"), "inv")

bill = lambda i, desc, val, tipo: {"id": i, "descricao": desc, "valor": val, "categoria": "Cat", "conta": "Conta", "tipo": tipo}
alert_cases = [
    {"hoje": [], "amanha": [], "faturas_hoje": [], "faturas_amanha": []},
    {"hoje": [bill(1, "Aluguel", 1500.0, "Despesa"), bill(2, "Salário", 5000.5, "Receita")], "amanha": [],
     "faturas_hoje": [{"cartao": "Nubank", "valor": 320.456, "vencimento": "2026-03-18"}], "faturas_amanha": []},
    {"hoje": [], "amanha": [bill(3, "Internet", 99.9, "Despesa"), bill(4, "Freela", 800.0, "Receita")],
     "faturas_hoje": [], "faturas_amanha": [{"cartao": "Inter", "valor": 0.0, "vencimento": "2026-03-19"}]},
    {"hoje": [bill(5, "Luz", 210.0, "Despesa")], "amanha": [bill(6, "Água", 80.0, "Despesa")],
     "faturas_hoje": [], "faturas_amanha": []},
]
financial_alerts = []
for case in alert_cases:
    data = {"contas_hoje": case["hoje"], "contas_amanha": case["amanha"],
            "faturas_hoje": case["faturas_hoje"], "faturas_amanha": case["faturas_amanha"]}
    for greeting in (False, True):
        financial_alerts.append({"input": case, "greeting": greeting,
                                 "output": alerts_mod.FinancialAlertFormatter.format(data, include_greeting=greeting)})

invoice_closed = []
for total in (0.0, 1234.5, 99.995):
    inv = {"nome_conta": "Nubank", "data_fechamento": d("2026-03-10"), "data_vencimento": d("2026-03-17"), "valor_total": total}
    invoice_closed.append({"total": total, "output": invoice_mod.InvoiceNotificationFormatter.format_invoice_closed(inv)})
invoice_due = []
for days in (1, 2, 3):
    inv = {"nome_conta": "Inter", "data_vencimento": d("2026-03-21"), "valor_total": 456.7, "dias_ate_vencimento": days}
    invoice_due.append({"days": days, "output": invoice_mod.InvoiceNotificationFormatter.format_due_date_warning(inv)})

# ---------------------------------------------------------------- resumo matinal (gemini_service.generate_daily_briefing)
source = read("services", "gemini_service.py")
tree = ast.parse(source)
func = next(n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == "generate_daily_briefing")
func_src = ast.get_source_segment(source, func)
CAPTURE = []
MODE = {"raise": True, "text": ""}


def safe_generate_content(model, prompt):
    CAPTURE.append(prompt)
    if MODE["raise"]:
        raise Exception("falha simulada")
    return MODE["text"]


ns = {"_obter_modelo_gemini_para_usuario": lambda uid: object(), "safe_generate_content": safe_generate_content,
      "get_gemini_text_response": lambda response: response, "datetime": dt.datetime}
exec(func_src, ns)
generate_daily_briefing = ns["generate_daily_briefing"]


def weather(desc, rain=None):
    return {"descricao_completa": desc, "chance_chuva": rain}


briefing_cases = [
    {
        "eventos": [
            {"summary": "Daily", "start": "2026-03-18T09:00:00-03:00", "end": "2026-03-18T09:30:00-03:00",
             "location": "https://meet.google.com/abc", "description": "", "all_day": False},
            {"summary": "Almoço com João", "start": "2026-03-18T12:30:00-03:00", "end": "2026-03-18T14:00:00-03:00",
             "location": "Restaurante X, Campinas, SP", "description": "Reunião", "all_day": False},
            {"summary": "Feriado", "start": "2026-03-18", "end": "2026-03-19", "location": "", "description": "", "all_day": True},
        ],
        "clima_principal": weather("Parcialmente nublado, 24°C", 45),
        "climas_adicionais": [{"cidade": "Campinas", "estado": "SP", "clima": weather("Sol, 28°C")}],
        "gaps": [{"inicio": "09:30", "fim": "12:30", "duracao_minutos": 180}, {"inicio": "14:00", "fim": "16:45", "duracao_minutos": 165}],
        "alertas_financeiros": {
            "contas_hoje": [bill(1, "Aluguel", 1500.0, "Despesa"), bill(2, "Salário", 5000.0, "Receita")],
            "contas_amanha": [bill(3, "Internet", 99.9, "Despesa"), bill(4, "Freela", 800.0, "Receita")],
            "faturas_hoje": [{"cartao": "Nubank", "valor": 320.45, "vencimento": "2026-03-18"}],
            "faturas_amanha": [{"cartao": "Inter", "valor": 10.0, "vencimento": "2026-03-19"}]},
        "data": "2026-03-18",
    },
    {
        "eventos": [{"summary": "Consulta", "start": "2026-03-19T08:00:00Z", "end": "2026-03-19T08:45:00Z",
                     "location": "", "description": "", "all_day": False},
                    {"summary": "Sem fim", "start": "", "end": "", "location": "", "description": None, "all_day": False}],
        "clima_principal": None, "climas_adicionais": [], "gaps": [],
        "alertas_financeiros": {"contas_hoje": [], "contas_amanha": [], "faturas_hoje": [], "faturas_amanha": []},
        "data": "2026-03-19",
    },
    {
        "eventos": [{"summary": "Reunião", "start": "2026-03-20T10:00:00-03:00", "end": "2026-03-20T10:30:00-03:00",
                     "location": "Sala 3", "description": "", "all_day": False}],
        "clima_principal": weather("Chuva, 19°C", 80), "climas_adicionais": [], "gaps": [{"inicio": "10:30", "fim": "11:00", "duracao_minutos": 30}],
        "alertas_financeiros": {"contas_hoje": [], "contas_amanha": [bill(9, "Luz", 50.0, "Despesa")], "faturas_hoje": [], "faturas_amanha": []},
        "data": "2026-03-20",
    },
]
briefing_prompt = []
for case in briefing_cases:
    data = dict(case)
    data["data"] = d(case["data"])
    CAPTURE.clear()
    MODE["raise"] = True
    fallback = generate_daily_briefing(data)
    briefing_prompt.append({"input": case, "prompt": CAPTURE[0], "fallback": fallback})

clean_inputs = [
    "  Olá!\n*   *Reunião às 9h\n* *Almoço\n   * *Treino\n<blockquote>Dica</blockquote> <b>Importante</b>\nFim  ",
    "Texto simples sem marcadores",
    "**Negrito duplo** e <br> tag e </p> outra\n*\n*  * item",
]
briefing_clean = []
for text in clean_inputs:
    MODE["raise"] = False
    MODE["text"] = text
    briefing_clean.append({"input": text, "output": generate_daily_briefing(briefing_cases[1] | {"data": d("2026-03-19")})})

# ---------------------------------------------------------------- daily_briefing_service (métodos puros)
bs_source = read("services", "daily_briefing_service.py")
bs_source = re.sub(r"^from app import db_engine\s*$", "db_engine = None", bs_source, flags=re.M)
bs_source = re.sub(r"^from app\.services\.google_calendar_oauth_service import .*$", "GoogleCalendarOAuthService = object", bs_source, flags=re.M)
bs_source = re.sub(r"^from app\.services\.weather_service import .*$", "WeatherService = object", bs_source, flags=re.M)
bs_ns = {}
exec(bs_source, bs_ns)
service = object.__new__(bs_ns["DailyBriefingService"])

gap_events = [
    {"all_day": False, "start": "2026-03-18T09:00:00-03:00", "end": "2026-03-18T10:00:00-03:00"},
    {"all_day": False, "start": "2026-03-18T10:20:00-03:00", "end": "2026-03-18T11:00:00-03:00"},
    {"all_day": False, "start": "2026-03-18T14:15:00-03:00", "end": "2026-03-18T15:00:00-03:00"},
    {"all_day": True, "start": "2026-03-18", "end": "2026-03-19"},
    {"all_day": False, "start": "2026-03-18T17:00:00Z", "end": "2026-03-18T18:00:00Z"},
    {"all_day": False, "start": "2026-03-18T19:00:00-03:00", "end": "2026-03-18T19:30:00-03:00"},
    {"all_day": False, "start": "xx", "end": "2026-03-18T19:30:00-03:00"},
]
briefing_gaps = {"events": gap_events, "gaps": service.calculate_time_gaps(gap_events)}
location_events = [
    {"location": "Shopping Iguatemi, Campinas, SP"}, {"location": "Av. Brasil, Santos, SP"}, {"location": "Campinas, SP"},
    {"location": "Sala 2"}, {"location": ""}, {"location": "Rua A, Curitiba, PR"}, {"location": "SP, Campinas, SP"},
    {"location": "Centro, rj"},
]
briefing_locations = {"events": location_events, "locations": [list(x) for x in sorted(service.extract_event_locations(location_events))]}
type_events = [
    {"summary": "Call com cliente", "description": "", "location": ""}, {"summary": "Almoço", "description": "", "location": "Restaurante"},
    {"summary": "Estudo", "description": "", "location": ""}, {"summary": "Reunião Online", "description": "", "location": "Sala"},
    {"summary": "Treino", "description": "no Zoom", "location": ""},
]
briefing_types = [{"event": e, "type": service.detect_event_type(e)} for e in type_events]

# ---------------------------------------------------------------- check-in (nightly_checkin_service)
class FakeRedis:
    def __init__(self):
        self.saved = {}

    def set_with_ttl(self, key, value, ttl_seconds=300):
        self.saved[key] = value
        return True


fake_redis = FakeRedis()
nc_source = read("services", "nightly_checkin_service.py")
nc_source = re.sub(r"^from app\.services\.redis_service import redis_service$", "redis_service = FAKE_REDIS", nc_source, flags=re.M)
nc_source = re.sub(r"^from app\.services import finance_service$", "finance_service = None", nc_source, flags=re.M)
nc_source = re.sub(r"^from app\.services\.queries import .*$", "AgendamentosQueries = FaturasQueries = None", nc_source, flags=re.M)
nc_source = re.sub(r"^from app\.utils import formatar_moeda$", "", nc_source, flags=re.M)
nc_source = re.sub(r"^from datetime import date, timedelta$", "from datetime import timedelta", nc_source, flags=re.M)
nc_ns = {"FAKE_REDIS": fake_redis, "formatar_moeda": formatar_moeda, "date": FakeDate}
exec(nc_source, nc_ns)
nc_ns["date"] = FakeDate
Checkin = nc_ns["NightlyCheckinService"]
import uuid  # noqa: E402  (create_checkin_session usa uuid.uuid4)
nc_ns["uuid"].uuid4 = lambda: uuid.UUID("abcdef12-3456-7890-abcd-ef1234567890")


def cb(i, desc, valor, dia, tipo_conta, grupo, kind="FIXO", exec_=0, total=None, venc=None, conta="Conta Corrente"):
    return {"id": i, "descricao": desc, "valor_previsto": valor, "dia_execucao": dia, "conta_id": 10 + i, "subcategoria_id": 20 + i,
            "usuario_id": 7, "nome_conta": conta, "tipo_conta": tipo_conta, "categoria": "Cat", "nome_macro": "Macro",
            "nome_grupo": grupo, "tipo_agendamento": kind, "parcelas_executadas": exec_, "total_parcelas": total,
            "data_vencimento_real": venc}


from decimal import Decimal  # noqa: E402

pending = [
    cb(1, "Salário", Decimal("5000.00"), 18, "Conta Corrente", "Renda", venc=d("2026-03-18")),
    cb(2, "Aluguel", Decimal("1500.50"), 17, "Conta Corrente", "Despesa Essencial", venc=d("2026-03-17")),
    cb(3, "Streaming", Decimal("39.90"), 18, "Cartão de Crédito", "Despesa", venc=d("2026-03-18"), conta="Nubank"),
    cb(4, "Curso", Decimal("200.00"), 16, "Conta Corrente", "Despesa Discricionária", kind="PARCELADO", exec_=2, total=10, venc=d("2026-03-16")),
    cb(5, "Condomínio", None, 12, "Conta Corrente", "Despesa Essencial", venc=d("2026-03-12")),
    cb(6, "Academia", Decimal("120.00"), 15, "Cartão de Crédito", "Despesa", venc=d("2026-03-15"), conta="Nubank"),
]
overdue = [
    cb(7, "Seguro", Decimal("300.00"), 5, "Conta Corrente", "Despesa Essencial", venc=d("2026-03-05")),
    cb(8, "Aluguel garagem", Decimal("250.00"), 3, "Conta Corrente", "Renda", venc=d("2026-03-03")),
    cb(9, "Parcela carro", Decimal("900.00"), 1, "Conta Corrente", "Despesa Essencial", kind="PARCELADO", exec_=0, total=2, venc=None),
]
due_today = [{"id": 1, "nome_conta": "Inter", "data_vencimento": d("2026-03-18"), "status": "Aberta", "valor_total": Decimal("777.70")}]
checkin_cases = [
    {"pending": pending, "overdue": overdue, "overdue_invoices": [], "due_today": due_today, "checkin_id": "abcdef12"},
    {"pending": pending, "overdue": overdue, "overdue_invoices": [], "due_today": due_today, "checkin_id": None},
    {"pending": [pending[2]], "overdue": [], "overdue_invoices": [], "due_today": [], "checkin_id": "abcdef12"},
    {"pending": [], "overdue": [], "overdue_invoices": [], "due_today": due_today, "checkin_id": "abcdef12"},
    {"pending": [], "overdue": [], "overdue_invoices": [], "due_today": [], "checkin_id": "abcdef12"},
    {"pending": [pending[0]], "overdue": [], "overdue_invoices": [], "due_today": [], "checkin_id": "zz"},
    # Desvio 130 (implementado): a seção FATURAS VENCIDAS usa o nome do cartão. O dict traz 'cartao' (o que a query devolve) e
    # 'nome_conta' (o que o código do Flask lê), então o Python real produz o texto que o .NET passou a gerar.
    {"pending": [pending[0]], "overdue": [], "checkin_id": "abcdef12", "due_today": due_today,
     "overdue_invoices": [{"id": 1, "cartao": "Inter", "nome_conta": "Inter", "data_vencimento": d("2026-03-10"), "status": "Aberta", "valor_total": Decimal("1234.50")},
                          {"id": 2, "cartao": "Nubank", "nome_conta": "Nubank", "data_vencimento": d("2026-02-25"), "status": "Fechada", "valor_total": Decimal("80.00")}]},
]


def jsonable(value):
    if isinstance(value, (dt.date, dt.datetime)):
        return value.isoformat()
    if isinstance(value, Decimal):
        return float(value)
    if isinstance(value, dict):
        return {k: jsonable(v) for k, v in value.items()}
    if isinstance(value, list):
        return [jsonable(v) for v in value]
    return value


checkin_messages = []
for case in checkin_cases:
    out = Checkin.format_consolidated_checkin_message(case["pending"], case["overdue"], [], case["overdue_invoices"], case["due_today"], case["checkin_id"])
    checkin_messages.append({"input": jsonable(case), "output": out})

# KeyError do Flask (evidência do comportamento ANTIGO, desvio 130 já implementado): a query devolve 'cartao', o código lê 'nome_conta'.
try:
    Checkin.format_consolidated_checkin_message(
        [pending[0]], [], [], [{"id": 1, "cartao": "Inter", "data_vencimento": d("2026-03-10"), "status": "Aberta", "valor_total": Decimal("10")}], [], "abcdef12")
    key_error = None
except KeyError as error:
    key_error = str(error)

fake_redis.saved.clear()
session_id = Checkin.create_checkin_session("5511999990000", pending + overdue)
session = jsonable(fake_redis.saved["nightly_checkin:5511999990000:" + session_id])
checkin_session = {"id": session_id, "bills": jsonable(pending + overdue), "session": session,
                   "active": fake_redis.saved["nightly_checkin_active:5511999990000"]}
checkin_days = [{"dia": dia, "hoje": h, "dias": Checkin.calculate_days_overdue(dia, d(h))}
                for dia, h in [(18, "2026-03-18"), (15, "2026-03-18"), (28, "2026-03-03"), (31, "2026-03-02"), (30, "2026-01-03"), (31, "2026-03-31")]]

# ---------------------------------------------------------------- relatório mensal
mr_source = read("services", "monthly_report_service.py")
mr_source = re.sub(r"^from app import db_engine$", "db_engine = None", mr_source, flags=re.M)
mr_source = re.sub(r"^from app\.utils import with_db_retry$", "with_db_retry = lambda *a, **k: (lambda f: f)", mr_source, flags=re.M)
mr_source = re.sub(r"^from app\.services\.chart_service import generate_pie_chart$", "generate_pie_chart = None", mr_source, flags=re.M)
mr_ns = {}
exec(mr_source, mr_ns)


def report(mes, ano, rendas, despesas, qtd, top, comp, potes, contas):
    return {"mes": mes, "ano": ano, "periodo": {"inicio": d(f"{ano}-{mes:02d}-01"), "fim": d(f"{ano}-{mes:02d}-28")},
            "totais": {"total_despesas": despesas, "total_rendas": rendas, "saldo_periodo": rendas - despesas, "total_transacoes": qtd},
            "top_categorias": top, "comparacao": comp, "potes": potes, "contas": contas}


report_cases = [
    report(2, 2026, 12345.67, 4321.1, 58,
           [{"categoria": "Mercado", "valor": 1200.5, "percentual": 27.78}, {"categoria": "Transporte", "valor": 300.0, "percentual": 6.94}],
           {"mes_atual": 4321.1, "mes_anterior": 4000.0, "variacao_valor": 321.1, "variacao_percentual": 8.0275},
           [{"nome": "Lazer", "limite": 500.0, "usado": 650.0, "saldo": -150.0, "percentual": 130.0},
            {"nome": "Comida", "limite": 1500.0, "usado": 1000.0, "saldo": 500.0, "percentual": 66.666}],
           {"pagas": 20, "valor_pago": 4321.1, "pendentes": 3, "valor_pendente": 1234.5}),
    # Desvio 132: as despesas chegam em valor absoluto (positivas).
    report(12, 2025, 0.0, 2500.25, 4,
           [], {"mes_atual": 2500.25, "mes_anterior": 0, "variacao_valor": 2500.25, "variacao_percentual": 0},
           [], {"pagas": 0, "valor_pago": 0, "pendentes": 0, "valor_pendente": 0}),
    report(1, 2026, 1234567.891, 0.005, 1,
           [{"categoria": "Outros", "valor": 0.005, "percentual": 100.0}],
           {"mes_atual": 0.005, "mes_anterior": 0.0, "variacao_valor": 0.005, "variacao_percentual": 0},
           [{"nome": "Vazio", "limite": 0.0, "usado": 0.0, "saldo": 0.0, "percentual": 0}],
           {"pagas": 1, "valor_pago": 0.005, "pendentes": 1, "valor_pendente": 99999.995}),
]
report_cases.append(
    # Gasto maior que a renda: saldo negativo, variação negativa e pote estourado (desvio 132 com despesas positivas).
    report(3, 2026, 1000.0, 2345678.9, 12,
           [{"categoria": "Moradia", "valor": 2000000.0, "percentual": 85.26}, {"categoria": "Lazer", "valor": 345678.9, "percentual": 14.74}],
           {"mes_atual": 2345678.9, "mes_anterior": 3000000.0, "variacao_valor": -654321.1, "variacao_percentual": -21.8107},
           [{"nome": "Lazer", "limite": 100.0, "usado": 345678.9, "saldo": -345578.9, "percentual": 345678.9}],
           {"pagas": 10, "valor_pago": 2345678.9, "pendentes": 2, "valor_pendente": 12.5}))


def to_ptbr(text):
    """Desvio 133: o mesmo texto do Flask com milhar '.' e decimal ',' (1,234.56 -> 1.234,56; +8.0 -> +8,0)."""
    return re.sub(r"(\d[\d,]*)\.(\d+)", lambda m: m.group(1).replace(",", ".") + "," + m.group(2), text)


monthly_messages = []
for case in report_cases:
    legacy = mr_ns["format_report_message"](case, "Rafael Silva")
    monthly_messages.append({"input": jsonable(case), "name": "Rafael Silva", "output": legacy, "output_ptbr": to_ptbr(legacy)})

output = {
    "financial_alerts": financial_alerts,
    "invoice_closed": invoice_closed,
    "invoice_due": invoice_due,
    "briefing_prompt": briefing_prompt,
    "briefing_clean": briefing_clean,
    "briefing_gaps": briefing_gaps,
    "briefing_locations": briefing_locations,
    "briefing_types": briefing_types,
    "checkin_messages": checkin_messages,
    "checkin_key_error": key_error,
    "checkin_session": checkin_session,
    "checkin_days": checkin_days,
    "monthly_messages": monthly_messages,
}
with open(OUT, "w", encoding="utf-8") as handle:
    json.dump(output, handle, ensure_ascii=False, indent=1, default=str)
print("ok", OUT)
