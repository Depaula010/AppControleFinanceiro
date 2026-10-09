# -*- coding: utf-8 -*-
"""
Secao do harness (executada no escopo de whatsapp_handler_harness.py): cenarios da sub-entrega 012d (contas fixas, vencimentos,
contas atrasadas e resposta ao check-in noturno). A saida vai para tests/Fixtures/whatsapp-fixedbills-scenarios.json.

Aqui sao REAIS: FixedBillsService, get_vencimentos_periodo / format_vencimentos_message (finance_service.py), as intencoes de
admin_intents.py e notification_intents.py, NightlyCheckinService inteiro (collect_financial_snapshot, formatador, parse_checkin_response,
process_response e mark_bills_as_paid) e o WhatsAppHandler. O banco e simulado por roteiro: as linhas de cada consulta vem da configuracao
do cenario e cada consulta registra um rotulo (com os parametros que decidem o que e buscado) para o teste comparar a sequencia.
O estado alterado aqui (modulos, datas, banco, FINANCE) e restaurado em 012d_zz_gravar.py. Todos os valores sao FICTICIOS.
"""
import collections as _co

_D_SAVED = {
    "date": _proxy.date,
    "handler_date": handler_ns.get("date"),
    "handler_db": handler_ns.get("db_engine"),
    "handler_nightly": handler_ns.get("NightlyCheckinService"),
    "app_db": sys.modules["app"].db_engine,
    "mod_fixed": sys.modules.get("app.services.fixed_bills_service"),
    "mod_nightly": sys.modules.get("app.services.nightly_checkin_service"),
    "facade_getattr": FinanceFacade.__getattr__,
    "registry_atrasadas": sys.modules["app.routes.webhooks.intents"].INTENT_REGISTRY.get("Contas Atrasadas"),
    "real_intents": dict(REAL_INTENTS),
}

# ---------------------------------------------------------------- datas (a data do cenario pode ser trocada por `today`)


def _scenario_today():
    value = ENV.cfg.get("today") if ENV is not None else None
    return _dt.date.fromisoformat(value) if value else FIXED_TODAY


class FrozenDate2(_dt.date):
    @classmethod
    def today(cls):
        d = _scenario_today()
        return cls(d.year, d.month, d.day)


class FrozenDateTime(_dt.datetime):
    @classmethod
    def now(cls, tz=None):
        d = _scenario_today()
        return cls(d.year, d.month, d.day, 12, 0, 0, tzinfo=tz)


_proxy.date = FrozenDate2
handler_ns["date"] = FrozenDate2

# ---------------------------------------------------------------- servicos reais
_fbs_ns = load("fixed_bills_service.py", source("services", "fixed_bills_service.py"))
stub_module("app.services.fixed_bills_service", FixedBillsService=_fbs_ns["FixedBillsService"])

_base_intent = _base_ns["BaseIntent"]
_fin_mod = sys.modules["app.services.finance_service"]
_fin_mod.get_vencimentos_periodo = _fs_ns["get_vencimentos_periodo"]
_fin_mod.format_vencimentos_message = _fs_ns["format_vencimentos_message"]
_notif_src = source("routes", "webhooks", "intents", "notification_intents.py").replace("from .base_intent import BaseIntent", "")
_notif_ns = load("notification_intents.py", _notif_src, {"BaseIntent": _base_intent})
_notif_ns["datetime"] = FrozenDateTime
del _fin_mod.get_vencimentos_periodo
del _fin_mod.format_vencimentos_message

_fq_ns = load("faturas_queries.py", source("services", "queries", "faturas_queries.py"))
sys.modules["app.services.queries"].FaturasQueries = _fq_ns["FaturasQueries"]
sys.modules["app.services.queries"].AgendamentosQueries = sys.modules["app.services.queries.agendamentos_queries"].AgendamentosQueries
_nc_ns = load("nightly_checkin_service.py", source("services", "nightly_checkin_service.py"))
_nc_ns["date"] = FrozenDate2
REAL_NIGHTLY = _nc_ns["NightlyCheckinService"]
stub_module("app.services.nightly_checkin_service", NightlyCheckinService=REAL_NIGHTLY)
handler_ns["NightlyCheckinService"] = REAL_NIGHTLY

REAL_INTENTS.update({
    "Consulta Contas Fixas": _admin_ns["ConsultaContasFixasIntent"],
    "Consulta Todas Contas": _admin_ns["ConsultaTodasContasIntent"],
    "Vencimentos Hoje": _notif_ns["VencimentosHojeIntent"],
    "Vencimentos Amanhã": _notif_ns["VencimentosAmanhaIntent"],
    "Vencimentos Essa Semana": _notif_ns["VencimentosSemanaIntent"],
})
sys.modules["app.routes.webhooks.intents"].INTENT_REGISTRY["Contas Atrasadas"] = _notif_ns["ContasAtrasadasIntent"]


# create_transaction passa a registrar o agendamento e a data do lancamento (o .NET entrega isso ao IScheduleExecutor)
def _bills_getattr(self, name):
    if name == "create_transaction":
        inner = _D_SAVED["facade_getattr"](self, name)

        def create_transaction(conn, uid, cid, scid, fid, descricao, valor, tipo, data, **kw):
            ENV.call("create_transaction:ag=%s:data=%s" % (kw.get("agendamento_id"), data.isoformat()))
            return inner(conn, uid, cid, scid, fid, descricao, valor, tipo, data, **kw)
        return create_transaction
    return _D_SAVED["facade_getattr"](self, name)


FinanceFacade.__getattr__ = _bills_getattr


# ---------------------------------------------------------------- banco simulado
class _MapRow:
    def __init__(self, data):
        self._mapping = data


def _row_dates(data, keys):
    out = dict(data)
    for key in keys:
        if out.get(key):
            out[key] = _dt.date.fromisoformat(out[key])
    for key in ("valor_previsto", "valor_total"):
        if key in out and out[key] is not None:
            out[key] = _decimal.Decimal(str(out[key]))
    return out


_Pending10 = _co.namedtuple(
    "Pending10", "id descricao valor_previsto dia_execucao tipo_agendamento categoria nome_conta tipo_conta nome_grupo ja_executado")
_Catalog11 = _co.namedtuple(
    "Catalog11", "id descricao valor_previsto dia_execucao mes_execucao periodicidade tipo_agendamento categoria nome_conta tipo_conta nome_grupo")


def _money(value):
    return None if value is None else _decimal.Decimal(str(value))


class BillsConn(FinConn2):
    def _boom(self, kind):
        if ENV.cfg.get("db_fail") == kind:
            raise Exception("banco indisponivel")

    def execute(self, sql, params=None):
        s = " ".join(str(sql).split())
        cfg = ENV.cfg
        if "UPDATE Agendamentos SET parcelas_executadas" in s:
            return Result([])
        if "ExpectedDates" in s:
            if ":hoje" in s:
                return Result([_MapRow(_row_dates(r, ["data_vencimento_real"])) for r in cfg.get("snapshot", {}).get("overdue_bills", [])])
            ENV.call("checkin.snapshot")
            self._boom("snapshot")
            return Result([_MapRow(_row_dates(r, ["data_vencimento_real"])) for r in cfg.get("snapshot", {}).get("pending_bills", [])])
        if "FROM Faturas f" in s and "f.data_vencimento < :hoje" in s:
            return Result([_MapRow(_row_dates(r, ["data_vencimento"])) for r in cfg.get("snapshot", {}).get("overdue_invoices", [])])
        if "FROM Faturas f" in s and "f.data_vencimento = :hoje" in s:
            return Result([_MapRow(_row_dates(r, ["data_vencimento"])) for r in cfg.get("snapshot", {}).get("invoices_due_today", [])])
        if "FROM Faturas f" in s and "BETWEEN :data_inicio AND :data_fim" in s:
            ENV.call("faturas:%s:%s" % (params["data_inicio"].isoformat(), params["data_fim"].isoformat()))
            self._boom("vencimentos")
            return Result([Row(nome_conta=r["nome_conta"], data_vencimento=_dt.date.fromisoformat(r["data_vencimento"]), status="Aberta",
                               valor_fatura=_money(r["valor_fatura"])) for r in cfg.get("due_invoices", [])])
        if "ja_executado" in s and "FROM Agendamentos a" in s:
            ENV.call("pendentes:%s:%s:%s" % (params["primeiro_dia"].isoformat(), params["ultimo_dia"].isoformat(), params["mes"]))
            self._boom("pendentes")
            return Result([_Pending10(i + 1, r["descricao"], _money(r["valor_previsto"]), r["dia_execucao"], "FIXO", r["categoria"], "Conta",
                                      r["tipo_conta"], r["nome_grupo"], r.get("ja_executado", 0)) for i, r in enumerate(cfg.get("pending_rows", []))])
        if "ORDER BY CASE a.periodicidade" in s:
            ENV.call("catalogo")
            self._boom("catalogo")
            return Result([_Catalog11(i + 1, r["descricao"], _money(r["valor_previsto"]), r["dia_execucao"], r.get("mes_execucao"), r.get("periodicidade"), "FIXO",
                                      r["categoria"], "Conta", r["tipo_conta"], r["nome_grupo"]) for i, r in enumerate(cfg.get("catalog_rows", []))])
        if "a.periodicidade, a.mes_execucao" in s and "NOT EXISTS" in s:
            if "BETWEEN :dia_inicio AND :dia_fim" in s:
                kind = "single"
            elif ">= :dia_inicio" in s:
                kind = "first"
            else:
                kind = "second"
            ENV.call("vencimentos:%s:%s:%s:%s/%s" % (kind, params.get("dia_inicio", "-"), params.get("dia_fim", "-"), params["mes_ref"], params["ano_ref"]))
            self._boom("vencimentos")
            return Result([Row(descricao=r["descricao"], valor_previsto=_money(r["valor_previsto"]), dia_execucao=r["dia_execucao"], periodicidade="MENSAL",
                               mes_execucao=None, categoria="X", nome_conta="Conta", nome_grupo=r["nome_grupo"])
                           for r in cfg.get("due_bills", {}).get(kind, [])])
        return super().execute(sql, params)


class BillsEngine:
    def connect(self):
        return BillsConn()


handler_ns["db_engine"] = BillsEngine()
sys.modules["app"].db_engine = handler_ns["db_engine"]

BILLS_SCENARIOS = []


def bills(name, texto, **kw):
    BILLS_SCENARIOS.append(scenario(name, "012d", texto, **kw))


# ---------------------------------------------------------------- dados comuns


def pend(descricao, valor, dia, tipo_conta="Conta Corrente", grupo="Despesa", categoria="Moradia", ja_executado=0):
    return {"descricao": descricao, "valor_previsto": valor, "dia_execucao": dia, "categoria": categoria, "tipo_conta": tipo_conta,
            "nome_grupo": grupo, "ja_executado": ja_executado}


PENDING_MIX = [
    pend("Aluguel", 1500.0, 1), pend("Internet", 99.9, 7), pend("Luz", 230.45, 9), pend("Condomínio", 450.0, 31), pend("Sem valor", None, 20),
    pend("Netflix", 55.9, 15, "Cartão de Crédito", categoria="Lazer"), pend("Academia", 119.9, 7, "Cartão de Crédito", categoria="Saúde"),
    pend("Salário", 5000.0, 5, grupo="Renda", categoria="Salário"), pend("Freela", 800.0, 25, grupo="Renda", categoria="Extra"),
]

# ---- Consulta Contas Fixas
bills("fixas_pendentes_misto", "minhas contas fixas", intent="Consulta Contas Fixas", pending_rows=PENDING_MIX)
bills("fixas_pendentes_vazio", "contas fixas pendentes", intent="Consulta Contas Fixas", pending_rows=[])
bills("fixas_pendentes_todas_executadas", "contas fixas pendentes", intent="Consulta Contas Fixas",
      pending_rows=[pend("Aluguel", 1500.0, 1, ja_executado=1), pend("Luz", 230.0, 9, ja_executado=2)])
bills("fixas_pendentes_filtra_executadas", "quais agendamentos tenho", intent="Consulta Contas Fixas",
      pending_rows=[pend("Aluguel", 1500.0, 1, ja_executado=1), pend("Luz", 230.0, 9)])
bills("fixas_pendentes_fevereiro", "contas fixas", intent="Consulta Contas Fixas", today="2026-02-10",
      pending_rows=[pend("Dia 28", 10.0, 28), pend("Dia 29", 20.0, 29), pend("Dia 30", 30.0, 30), pend("Dia 31", 40.0, 31)])
bills("fixas_pendentes_dezembro", "contas fixas", intent="Consulta Contas Fixas", today="2026-12-31", pending_rows=PENDING_MIX)
bills("fixas_pendentes_so_receitas", "contas fixas", intent="Consulta Contas Fixas", pending_rows=[pend("Salário", 5000.0, 5, grupo="Renda")])
bills("fixas_pendentes_erro_no_banco", "contas fixas", intent="Consulta Contas Fixas", db_fail="pendentes", pending_rows=PENDING_MIX)

# ---- Consulta Todas Contas
CATALOG = [
    {"descricao": "Feira", "valor_previsto": 120.0, "dia_execucao": 6, "periodicidade": "SEMANAL", "categoria": "Casa", "tipo_conta": "Conta Corrente", "nome_grupo": "Despesa"},
    {"descricao": "Faxina", "valor_previsto": 200.0, "dia_execucao": 15, "periodicidade": "QUINZENAL", "categoria": "Casa", "tipo_conta": "Cartão de Crédito", "nome_grupo": "Despesa"},
    {"descricao": "Aluguel", "valor_previsto": 1500.0, "dia_execucao": 5, "periodicidade": "MENSAL", "categoria": "Moradia", "tipo_conta": "Conta Corrente", "nome_grupo": "Despesa"},
    {"descricao": "Salário", "valor_previsto": 5000.0, "dia_execucao": 5, "periodicidade": "MENSAL", "categoria": "Salário", "tipo_conta": "Conta Corrente", "nome_grupo": "Renda"},
    {"descricao": "Sem periodo", "valor_previsto": None, "dia_execucao": 9, "periodicidade": None, "categoria": "X", "tipo_conta": "Conta Corrente", "nome_grupo": "Despesa"},
    {"descricao": "IPTU", "valor_previsto": 1200.0, "dia_execucao": 10, "mes_execucao": 3, "periodicidade": "ANUAL", "categoria": "Moradia", "tipo_conta": "Conta Corrente", "nome_grupo": "Despesa"},
    {"descricao": "Bônus", "valor_previsto": 2000.0, "dia_execucao": 20, "mes_execucao": None, "periodicidade": "ANUAL", "categoria": "Extra", "tipo_conta": "Conta Corrente", "nome_grupo": "Renda"},
    {"descricao": "Café", "valor_previsto": 5.0, "dia_execucao": 1, "periodicidade": "DIARIA", "categoria": "Lazer", "tipo_conta": "Conta Corrente", "nome_grupo": "Despesa"},
]
bills("todas_catalogo", "todas as minhas contas", intent="Consulta Todas Contas", catalog_rows=CATALOG)
bills("todas_vazio", "listar todas as contas", intent="Consulta Todas Contas", catalog_rows=[])
bills("todas_so_receitas", "quais contas tenho esse mês", intent="Consulta Todas Contas", catalog_rows=[CATALOG[3]])
bills("todas_so_anuais", "me mostra todas as contas", intent="Consulta Todas Contas", catalog_rows=[CATALOG[5]])
bills("todas_erro_no_banco", "todas as contas", intent="Consulta Todas Contas", db_fail="catalogo", catalog_rows=CATALOG)


# ---- Vencimentos
def due(descricao, valor, dia, grupo="Despesa"):
    return {"descricao": descricao, "valor_previsto": valor, "dia_execucao": dia, "nome_grupo": grupo}


def inv(cartao, valor, vencimento):
    return {"nome_conta": cartao, "valor_fatura": valor, "data_vencimento": vencimento}


DUE_MIX = [due("Salário", 5000.0, 7, "Renda"), due("Aluguel", 1500.0, 7), due("Internet", 99.9, 7), due("Sem valor", None, 7)]
bills("vencimentos_hoje_misto", "o que vence hoje", intent="Vencimentos Hoje", due_bills={"single": DUE_MIX}, due_invoices=[inv("Cartão Inter", 1250.5, "2026-10-07")])
bills("vencimentos_hoje_vazio", "o que vence hoje", intent="Vencimentos Hoje", due_bills={"single": []}, due_invoices=[])
bills("vencimentos_hoje_so_fatura", "contas de hoje", intent="Vencimentos Hoje", due_bills={"single": []}, due_invoices=[inv("Cartão Inter", 99.99, "2026-10-07")])
bills("vencimentos_hoje_so_receita", "contas de hoje", intent="Vencimentos Hoje", due_bills={"single": [DUE_MIX[0]]}, due_invoices=[])
bills("vencimentos_hoje_erro_no_banco", "o que vence hoje", intent="Vencimentos Hoje", db_fail="vencimentos", due_bills={"single": DUE_MIX}, due_invoices=[])
bills("vencimentos_amanha_misto", "o que vence amanhã", intent="Vencimentos Amanhã", due_bills={"single": DUE_MIX[1:3]}, due_invoices=[inv("Nubank", 10.0, "2026-10-08")])
bills("vencimentos_amanha_vazio", "o que vence amanhã", intent="Vencimentos Amanhã", due_bills={"single": []}, due_invoices=[])
bills("vencimentos_amanha_virada_de_mes", "o que vence amanhã", intent="Vencimentos Amanhã", today="2026-10-31",
      due_bills={"single": [due("Dia um", 10.0, 1)]}, due_invoices=[])
bills("vencimentos_amanha_virada_de_ano", "o que vence amanhã", intent="Vencimentos Amanhã", today="2026-12-31",
      due_bills={"single": [due("Dia um", 10.0, 1)]}, due_invoices=[inv("Nubank", 5.5, "2027-01-01")])
bills("vencimentos_semana_mesmo_mes", "o que vence essa semana", intent="Vencimentos Essa Semana",
      due_bills={"single": DUE_MIX + [due("Luz", 230.45, 12)]}, due_invoices=[inv("Cartão Inter", 1250.5, "2026-10-10"), inv("Nubank", 0, "2026-10-14")])
bills("vencimentos_semana_virada_de_mes", "o que vence essa semana", intent="Vencimentos Essa Semana", today="2026-10-28",
      due_bills={"first": [due("Condomínio", 450.0, 30), due("Salário", 5000.0, 31, "Renda")], "second": [due("Aluguel", 1500.0, 1), due("Internet", 99.9, 3)]},
      due_invoices=[inv("Cartão Inter", 1250.5, "2026-11-02")])
bills("vencimentos_semana_virada_de_ano", "vencimentos dos próximos dias", intent="Vencimentos Essa Semana", today="2026-12-28",
      due_bills={"first": [due("Seguro", 300.0, 30)], "second": [due("Aluguel", 1500.0, 1)]}, due_invoices=[])
bills("vencimentos_semana_virada_so_segundo", "o que vence essa semana", intent="Vencimentos Essa Semana", today="2026-10-25",
      due_bills={"first": [], "second": [due("Aluguel", 1500.0, 1)]}, due_invoices=[])
bills("vencimentos_semana_vazio", "o que vence essa semana", intent="Vencimentos Essa Semana", due_bills={"single": []}, due_invoices=[])
bills("vencimentos_semana_virada_vazio", "o que vence essa semana", intent="Vencimentos Essa Semana", today="2026-10-30",
      due_bills={"first": [], "second": []}, due_invoices=[])
bills("vencimentos_semana_erro_no_banco", "o que vence essa semana", intent="Vencimentos Essa Semana", db_fail="vencimentos",
      due_bills={"single": DUE_MIX}, due_invoices=[])


# ---- Contas Atrasadas (snapshot do check-in em modo leitura)
def ck_bill(i, descricao, valor, dia, real, grupo="Despesa", tipo_conta="Conta Corrente", kind="FIXO", executed=None, total=None):
    return {"id": i, "descricao": descricao, "valor_previsto": valor, "dia_execucao": dia, "conta_id": 1, "subcategoria_id": 10, "usuario_id": 7,
            "nome_conta": "Nubank" if tipo_conta != "Cartão de Crédito" else "Cartão Inter", "tipo_conta": tipo_conta, "categoria": "Moradia",
            "nome_macro": "Moradia", "nome_grupo": grupo, "tipo_agendamento": kind, "parcelas_executadas": executed, "total_parcelas": total,
            "data_vencimento_real": real}


def snap(pending=(), overdue=(), invoices=(), today_invoices=()):
    return {"pending_bills": list(pending), "overdue_bills": list(overdue), "overdue_invoices": list(invoices), "invoices_due_today": list(today_invoices)}


bills("atrasadas_vazio", "tenho conta atrasada", intent="Contas Atrasadas", snapshot=snap())
bills("atrasadas_pendentes", "contas vencidas", intent="Contas Atrasadas", snapshot=snap(pending=[
    ck_bill(1, "Aluguel", 1500.0, 7, "2026-10-07"), ck_bill(2, "Luz", 230.45, 6, "2026-10-06"), ck_bill(3, "Água", 80.1, 5, "2026-10-05"),
    ck_bill(4, "Internet", 99.9, 4, "2026-10-04"), ck_bill(5, "Salário", 5000.0, 7, "2026-10-07", "Renda"),
    ck_bill(6, "Tênis", 150.0, 7, "2026-10-07", kind="PARCELADO", executed=1, total=3)]))
bills("atrasadas_com_atrasadas", "o que já passou do vencimento", intent="Contas Atrasadas", snapshot=snap(
    pending=[ck_bill(1, "Aluguel", 1500.0, 7, "2026-10-07")],
    overdue=[ck_bill(2, "Seguro", 300.0, 20, "2026-09-20"), ck_bill(3, "Bônus", 900.0, 25, "2026-09-25", "Renda"), ck_bill(1, "Aluguel", 1500.0, 7, "2026-09-07"),
             ck_bill(4, "Hoje mesmo", 10.0, 7, "2026-10-07"), ck_bill(5, "Sem data", 20.0, 3, None)]))
bills("atrasadas_cartao_debita_hoje", "contas atrasadas", intent="Contas Atrasadas", snapshot=snap(
    pending=[ck_bill(1, "Netflix", 55.9, 7, "2026-10-07", tipo_conta="Cartão de Crédito"), ck_bill(2, "Spotify", 21.9, 6, "2026-10-06", tipo_conta="Cartão de Crédito")]))
bills("atrasadas_so_cartao_vira_vazio", "contas atrasadas", intent="Contas Atrasadas", snapshot=snap(
    pending=[ck_bill(1, "Spotify", 21.9, 6, "2026-10-06", tipo_conta="Cartão de Crédito")]))
bills("atrasadas_fatura_hoje", "contas atrasadas", intent="Contas Atrasadas", snapshot=snap(
    today_invoices=[{"id": 1, "nome_conta": "Cartão Inter", "data_vencimento": "2026-10-07", "status": "Aberta", "valor_total": 1250.5}]))
# Desvio 130 (implementado): o Flask real lê fatura['nome_conta'] e a query devolve 'cartao' (KeyError). O cenário traz as duas chaves,
# de modo que o código real do Flask produza o texto que o .NET passou a gerar (nome do cartão na seção FATURAS VENCIDAS).
bills("atrasadas_fatura_vencida", "contas atrasadas", intent="Contas Atrasadas", snapshot=snap(
    pending=[ck_bill(1, "Aluguel", 1500.0, 7, "2026-10-07")],
    invoices=[{"id": 1, "cartao": "Cartão Inter", "nome_conta": "Cartão Inter", "data_vencimento": "2026-10-01", "status": "Aberta", "valor_total": 300.0}]), deviation="130")
bills("atrasadas_erro_no_banco", "contas atrasadas", intent="Contas Atrasadas", db_fail="snapshot", snapshot=snap())
bills("atrasadas_data_diferente", "contas atrasadas", intent="Contas Atrasadas", today="2026-03-02", snapshot=snap(pending=[
    ck_bill(1, "Aluguel", 1500.0, 28, "2026-02-28"), ck_bill(2, "Luz", 230.0, 2, "2026-03-02")]))

# ---- Resposta ao check-in noturno
CHECKIN = "chk00001"
SESSION_KEY = "nightly_checkin:%s:%s" % (NUMBER, CHECKIN)
ACTIVE_KEY = "nightly_checkin_active:%s" % NUMBER


def sess_item(i, descricao, valor, dia, real, grupo="Despesa", tipo_conta="Conta Corrente", kind="FIXO", executed=None, total=None):
    item = ck_bill(i, descricao, valor, dia, real, grupo, tipo_conta, kind, executed, total)
    item["status_text"] = "Vence hoje"
    item["dias_atraso"] = 0
    return item


def sess(items):
    return {"items": {str(i + 1): item for i, item in enumerate(items)}, "created_at": "2026-10-07", "total_items": len(items)}


def checkin_redis(items):
    return {SESSION_KEY: sess(items), ACTIVE_KEY: CHECKIN}


THREE = [sess_item(11, "Salário", 5000.0, 7, "2026-10-07", "Renda"), sess_item(12, "Aluguel", 1500.0, 7, "2026-10-07"), sess_item(13, "Luz", 230.45, 5, "2026-10-05")]
for _name, _text in [("ok", "ok"), ("emoji", "👍"), ("sim", "Sim"), ("tudo_pago", "tudo pago"), ("assim_confirma_tudo", "assim"), ("checkmark", "✅")]:
    bills("checkin_todas_" + _name, _text, redis=checkin_redis(THREE))
bills("checkin_uma_conta_so", "ok", redis=checkin_redis(THREE[:1]))
bills("checkin_parcial_um", "1", redis=checkin_redis(THREE))
bills("checkin_parcial_um_e_tres", "1 e 3", redis=checkin_redis(THREE))
bills("checkin_parcial_virgulas", "1, 2, 3", redis=checkin_redis(THREE))
bills("checkin_parcial_ordem_trocada", "3, 1", redis=checkin_redis(THREE))
bills("checkin_parcial_repetido", "2 2 2", redis=checkin_redis(THREE))
bills("checkin_parcial_texto_e_numero", "só a 2", redis=checkin_redis(THREE))
bills("checkin_nao", "não", redis=checkin_redis(THREE))
bills("checkin_nao_sem_acento", "nao", redis=checkin_redis(THREE))
bills("checkin_depois", "Depois", redis=checkin_redis(THREE))
bills("checkin_amanha", "amanhã", redis=checkin_redis(THREE))
bills("checkin_ok_ganha_do_nao", "ok não", redis=checkin_redis(THREE))
bills("checkin_invalido", "talvez", redis=checkin_redis(THREE))
bills("checkin_numero_fora", "5", redis=checkin_redis(THREE))
bills("checkin_numero_zero", "0", redis=checkin_redis(THREE))
bills("checkin_numeros_misturados", "1 e 9", redis=checkin_redis(THREE))
bills("checkin_numero_gigante", "99999999999999999999", redis=checkin_redis(THREE))
bills("checkin_sessao_expirada", "ok", redis={ACTIVE_KEY: CHECKIN})
bills("checkin_sessao_vazia", "ok", redis={SESSION_KEY: sess([]), ACTIVE_KEY: CHECKIN})
bills("checkin_escape_hatch_apaga_flag", "quanto devo?", redis=checkin_redis(THREE), intent="Menu de Ajuda")

# datas do lancamento: vencimento real (hoje, ontem, 2 dias, futuro, mes anterior) e fallback por dia_execucao
DATES = [
    sess_item(21, "Hoje", 10.0, 7, "2026-10-07"), sess_item(22, "Ontem", 20.0, 6, "2026-10-06"), sess_item(23, "Dois dias", 30.0, 5, "2026-10-05"),
    sess_item(24, "Futuro", 40.0, 9, "2026-10-09"), sess_item(25, "Mes anterior", 50.0, 30, "2026-09-30"),
    sess_item(26, "Sem data dia 7", 60.0, 7, None), sess_item(27, "Sem data dia 6", 70.0, 6, None), sess_item(28, "Sem data dia 5", 80.0, 5, None),
    sess_item(29, "Sem data dia 20", 90.0, 20, None),
]
bills("checkin_datas_do_lancamento", "ok", redis=checkin_redis(DATES))
bills("checkin_datas_virada_de_mes", "ok", today="2026-03-01", redis=checkin_redis([
    sess_item(31, "Fev 28", 10.0, 28, "2026-02-28"), sess_item(32, "Mar 1", 20.0, 1, "2026-03-01"), sess_item(33, "Sem data 1", 30.0, 1, None), sess_item(34, "Sem data 31", 40.0, 31, None)]))
bills("checkin_cartao_e_parcelado", "ok", redis=checkin_redis([
    sess_item(41, "Salário cartão", 100.0, 7, "2026-10-07", "Renda", "Cartão de Crédito"),
    sess_item(42, "Tênis", 150.0, 7, "2026-10-07", kind="PARCELADO", executed=1, total=3),
    sess_item(43, "Última parcela", 150.0, 7, "2026-10-07", kind="PARCELADO", executed=2, total=3)]))
bills("checkin_erro_ao_salvar", "ok", deviation="008", redis=checkin_redis(THREE),
      returns={"finance_service.create_transaction": {"__raise__": "falha no banco"}})
bills("checkin_data_invalida_na_sessao", "ok", deviation="008", redis=checkin_redis([sess_item(51, "Data ruim", 10.0, 7, "amanhã")]))
