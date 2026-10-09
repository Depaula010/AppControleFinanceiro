# -*- coding: utf-8 -*-
"""Cenarios da 012c (consultas financeiras). Infraestrutura em 012a_transacoes.py; aqui entram os intents de admin/analytics reais."""
import re as _re

# ---------------------------------------------------------------- formatadores e servicos reais
_df_ns = load("date_formatter.py", source("shared", "formatters", "date_formatter.py"))
sys.modules["app.utils"].formatar_mes_ano_pt = _df_ns["formatar_mes_ano_pt"]
stub_module("app.shared")
stub_module("app.shared.formatters")
stub_module("app.shared.formatters.currency_formatter", formatar_moeda=currency["formatar_moeda"])

_pq_ns = load("period_query_service.py", source("services", "period_query_service.py"))
stub_module("app.services.period_query_service", PeriodQueryService=_pq_ns["PeriodQueryService"])


def _to_dates(value):
    if isinstance(value, dict):
        return {k: (_dt.date.fromisoformat(v) if k in ("data_vencimento", "data") and isinstance(v, str) else _to_dates(v)) for k, v in value.items()}
    if isinstance(value, list):
        return [_to_dates(v) for v in value]
    return value


_old_getattr = FinanceFacade.__getattr__


def _facade_getattr(self, name):
    if name in ("get_fatura_valor", "get_fatura_detalhada"):
        inner = getattr(SeqRec("finance_service"), name)
        return lambda *a, **k: _to_dates(inner(*a, **k))
    if name == "get_category_spending":
        inner = getattr(SeqRec("finance_service"), name)

        def get_category_spending(*a, **k):
            raw = inner(*a, **k)
            return (float(raw or 0)) * -1  # como o finance_service real: soma negativa vira gasto positivo
        return get_category_spending
    return _old_getattr(self, name)


FinanceFacade.__getattr__ = _facade_getattr


class FinConn2(FinConn):
    def execute(self, sql, params=None):
        s = " ".join(str(sql).split())
        cfg = ENV.cfg
        if "incluir_na_reserva = TRUE" in s and "impacto_n_meses" in s:
            return Result([Row(descricao=i["descricao"], valor_previsto=_decimal.Decimal(str(i["valor_previsto"])), periodicidade=i["periodicidade"],
                               categoria=i["categoria"], impacto_n_meses=(None if i["impacto"] is None else _decimal.Decimal(str(i["impacto"]))))
                           for i in cfg.get("reserve_items", [])])
        if "s.nome_sub as categoria" in s and "FROM Transacoes t" in s:
            return Result([(r[0], _decimal.Decimal(str(r[1])), r[2], _dt.date.fromisoformat(r[3]), r[4]) for r in cfg.get("period_rows", [])])
        if "ILIKE :cat_nome" in s:
            return Result([(r[0], _decimal.Decimal(str(r[1])), _dt.date.fromisoformat(r[2])) for r in cfg.get("category_rows", [])])
        return super().execute(sql, params)


class FinEngine2:
    def connect(self):
        return FinConn2()


handler_ns["db_engine"] = FinEngine2()

# ---------------------------------------------------------------- intents reais de admin e analytics
_BaseIntent = _base_ns["BaseIntent"]
_admin_src = source("routes", "webhooks", "intents", "admin_intents.py").replace("from .base_intent import BaseIntent", "")
_admin_ns = load("admin_intents.py", _admin_src, {"BaseIntent": _BaseIntent})
_an_src = source("routes", "webhooks", "intents", "analytics_intents.py").replace("from .base_intent import BaseIntent", "")
_an_ns = load("analytics_intents.py", _an_src, {"BaseIntent": _BaseIntent})
REAL_INTENTS.update({
    "Listar Contas": _admin_ns["ListarContasIntent"],
    "Ajustar Saldo Inicial": _admin_ns["AjustarSaldoIntent"],
    "Consulta Valor Fatura": _admin_ns["ConsultaFaturaIntent"],
    "Consulta Período": _an_ns["ConsultaPeriodoIntent"],
    "Consulta Categoria Específica": _an_ns["ConsultaCategoriaIntent"],
})

SALDO_ONE = [{"nome_conta": "Nubank", "tipo_conta": "Conta Corrente", "saldo": 1500.5}]
SALDO_MANY = [
    {"nome_conta": "Cartão Inter", "tipo_conta": "Cartão de Crédito", "saldo": -350.25},
    {"nome_conta": "Nubank", "tipo_conta": "Conta Corrente", "saldo": 1234567.891},
    {"nome_conta": "Carteira", "tipo_conta": "Dinheiro", "saldo": 0.125},
]

# ---- Consulta Saldo
fin("saldo_uma_conta", "012c", "saldo do nubank", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": js(nome_conta="Nubank")},
    returns={"finance_service.get_account_by_name": 1, "finance_service.get_saldo_contas": SALDO_ONE})
fin("saldo_cartao_icone", "012c", "saldo do cartão", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": js(nome_conta="Cartão Inter")},
    returns={"finance_service.get_account_by_name": 2, "finance_service.get_saldo_contas": [SALDO_MANY[0]]})
fin("saldo_todas_sem_chave", "012c", "meu saldo", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": js(nome_nome=None)},
    returns={"finance_service.get_saldo_contas": SALDO_MANY})
fin("saldo_todas_nome_nulo", "012c", "meu saldo", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": js(nome_conta=None)},
    returns={"finance_service.get_saldo_contas": SALDO_MANY})
fin("saldo_conta_inexistente", "012c", "saldo do xpto", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": js(nome_conta="Xpto")},
    returns={"finance_service.get_account_by_name": None})
fin("saldo_sem_contas", "012c", "meu saldo", intent="Consulta Saldo", accounts=[], llm={"extract_saldo_query": js(nome_conta=None)},
    returns={"finance_service.get_saldo_contas": []})
fin("saldo_cota_do_gemini", "012c", "meu saldo", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": {"__raise__": "429 Quota exceeded"}})
fin("saldo_json_invalido", "012c", "meu saldo", intent="Consulta Saldo", accounts=ACC, llm={"extract_saldo_query": "nao json"})

# ---- Reserva
fin("reserva_resumo", "012c", "minha reserva", intent="Consulta Reserva", returns={"finance_service.get_reserva_status": {"__tuple__": [2500.5, 15003.0, 6]}})
fin("reserva_resumo_zero", "012c", "minha reserva", intent="Consulta Reserva", returns={"finance_service.get_reserva_status": {"__tuple__": [0, 0.0, 12]}})
RESERVE_ITEMS = [
    {"descricao": "Aluguel", "valor_previsto": 1500.0, "periodicidade": "MENSAL", "categoria": "Moradia", "impacto": 9000.0},
    {"descricao": "IPTU", "valor_previsto": 1200.0, "periodicidade": "ANUAL", "categoria": "Moradia", "impacto": 1200.0},
    {"descricao": "Faxina", "valor_previsto": 80.0, "periodicidade": "SEMANAL", "categoria": "Casa", "impacto": 2080.0},
    {"descricao": "Mesada", "valor_previsto": 50.0, "periodicidade": "QUINZENAL", "categoria": "Familia", "impacto": 600.0},
    {"descricao": "Café", "valor_previsto": 5.5, "periodicidade": "DIARIA", "categoria": "Lazer", "impacto": 990.0},
]
fin("reserva_detalhes", "012c", "detalhes da reserva", intent="Consulta Detalhes Reserva", reserve_items=RESERVE_ITEMS,
    returns={"finance_service.get_reserva_status": {"__tuple__": [2500.5, 15003.0, 6]}})
fin("reserva_detalhes_tres_meses", "012c", "detalhes da reserva", intent="Consulta Detalhes Reserva", reserve_items=RESERVE_ITEMS,
    returns={"finance_service.get_reserva_status": {"__tuple__": [1000.0, 3000.0, 3]}})
fin("reserva_detalhes_vazio", "012c", "detalhes da reserva", intent="Consulta Detalhes Reserva", reserve_items=[],
    returns={"finance_service.get_reserva_status": {"__tuple__": [0, 0.0, 6]}})
fin("reserva_detalhes_impacto_nulo", "012c", "detalhes da reserva", intent="Consulta Detalhes Reserva",
    reserve_items=[{"descricao": "Anuidade", "valor_previsto": 99.9, "periodicidade": "BIMESTRAL", "categoria": "X", "impacto": None}],
    returns={"finance_service.get_reserva_status": {"__tuple__": [16.65, 99.9, 6]}})

# ---- Potes
fin("potes_nenhum", "012c", "meus potes", intent="Consulta Potes", returns={"finance_service.get_pote_status": []})
fin("potes_varios", "012c", "meus potes", intent="Consulta Potes",
    returns={"finance_service.get_pote_status": [["Alimentação", 500.0, -380.4], ["Lazer", 200.0, 0], ["Transporte", 100.0, -150.0], ["Zero", 0.0, -10]]})

# ---- Listar contas
fin("contas_listar", "012c", "quais minhas contas", intent="Listar Contas", accounts=ACC + [{"id": 4, "nome_conta": "Itaú", "tipo_conta": "Conta Corrente"}])
fin("contas_listar_vazio", "012c", "quais minhas contas", intent="Listar Contas", accounts=[])

# ---- Ajustar saldo inicial
fin("ajuste_sucesso", "012c", "ajustar saldo inicial nubank 1.500,50", intent="Ajustar Saldo Inicial", accounts=ACC, returns={"finance_service.update_saldo_inicial": True})
fin("ajuste_cartao", "012c", "corrigir saldo do cartão inter para 5000", intent="Ajustar Saldo Inicial", accounts=ACC, returns={"finance_service.update_saldo_inicial": True})
fin("ajuste_sem_valor", "012c", "ajustar saldo inicial nubank", intent="Ajustar Saldo Inicial", accounts=ACC)
fin("ajuste_sem_conta", "012c", "ajustar saldo inicial 1500", intent="Ajustar Saldo Inicial", accounts=ACC)
fin("ajuste_sem_conta_nem_contas", "012c", "ajustar saldo inicial 1500", intent="Ajustar Saldo Inicial", accounts=[])
fin("ajuste_falha", "012c", "ajustar saldo nubank 100", intent="Ajustar Saldo Inicial", accounts=ACC, returns={"finance_service.update_saldo_inicial": False})
fin("ajuste_valor_decimal_ponto", "012c", "ajustar saldo nubank 12.50", intent="Ajustar Saldo Inicial", accounts=ACC, returns={"finance_service.update_saldo_inicial": True})
fin("ajuste_erro_no_banco", "012c", "ajustar saldo nubank 100", intent="Ajustar Saldo Inicial", accounts=ACC,
    returns={"finance_service.update_saldo_inicial": {"__raise__": "boom"}})

# ---- Fatura
INV_ONE = [{"nome_cartao": "Cartão Inter", "valor_fatura": 1250.5, "data_vencimento": "2026-10-17", "status": "Aberta"}]
INV_MANY = INV_ONE + [{"nome_cartao": "Nubank", "valor_fatura": 99.99, "data_vencimento": "2026-11-05", "status": "Aberta"}]
INV_DETAIL = {
    "nome_cartao": "Cartão Inter", "valor_total": 400.9, "data_vencimento": "2026-10-17", "status": "Aberta",
    "transacoes": [
        {"descricao": "Netflix", "valor": 49.9, "data": "2026-10-05", "tipo_agendamento": "FIXO", "parcela_info": None},
        {"descricao": "Tênis", "valor": 150.0, "data": "2026-10-05", "tipo_agendamento": "PARCELADO", "parcela_info": "2/3"},
        {"descricao": "Mercado", "valor": 201.0, "data": "2026-09-28", "tipo_agendamento": None, "parcela_info": None},
    ],
}
fin("fatura_resumida_um_cartao", "012c", "quanto está a fatura do inter", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao="Cartão Inter")}, returns={"finance_service.get_account_by_name": 2, "finance_service.get_fatura_valor": INV_ONE})
fin("fatura_resumida_todas_um", "012c", "valor da fatura", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao=None)}, returns={"finance_service.get_fatura_valor": INV_ONE})
fin("fatura_resumida_varias", "012c", "valor das faturas", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao=None)}, returns={"finance_service.get_fatura_valor": INV_MANY})
fin("fatura_nenhuma", "012c", "valor da fatura", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao=None)}, returns={"finance_service.get_fatura_valor": []})
fin("fatura_nenhuma_com_cartao", "012c", "valor da fatura do inter", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao="Cartão Inter")}, returns={"finance_service.get_account_by_name": 2, "finance_service.get_fatura_valor": []})
fin("fatura_cartao_nao_encontrado", "012c", "valor da fatura do xpto", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao="Xpto")}, returns={"finance_service.get_account_by_name": None})
fin("fatura_detalhada", "012c", "detalhar a fatura do inter", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao="Cartão Inter")}, returns={"finance_service.get_account_by_name": 2, "finance_service.get_fatura_detalhada": INV_DETAIL})
fin("fatura_detalhada_sem_transacoes", "012c", "itens da fatura do inter", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao="Cartão Inter")},
    returns={"finance_service.get_account_by_name": 2, "finance_service.get_fatura_detalhada": dict(INV_DETAIL, transacoes=[])})
fin("fatura_detalhada_inexistente", "012c", "detalhe da fatura do inter", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao="Cartão Inter")}, returns={"finance_service.get_account_by_name": 2, "finance_service.get_fatura_detalhada": None})
fin("fatura_detalhes_sem_cartao_vira_resumo", "012c", "detalhes das faturas", intent="Consulta Valor Fatura", accounts=ACC,
    llm={"extract_fatura_query": js(conta_cartao=None)}, returns={"finance_service.get_fatura_valor": INV_MANY})
fin("fatura_cota_do_gemini", "012c", "valor da fatura", intent="Consulta Valor Fatura", accounts=ACC, llm={"extract_fatura_query": {"__raise__": "429 Quota exceeded"}})

# ---- Categoria específica
fin("categoria_gasto", "012c", "quanto gastei com alimentação", intent="Consulta Categoria Específica",
    llm={"extract_category_query": js(nome_categoria="Alimentação")}, returns={"finance_service.get_category_spending": -1234.56})
fin("categoria_gasto_zero", "012c", "quanto gastei com lazer", intent="Consulta Categoria Específica",
    llm={"extract_category_query": js(nome_categoria="Lazer")}, returns={"finance_service.get_category_spending": 0})
fin("categoria_nao_identificada", "012c", "quanto gastei", intent="Consulta Categoria Específica", llm={"extract_category_query": js(nome_categoria=None)})
fin("categoria_json_invalido", "012c", "quanto gastei", intent="Consulta Categoria Específica", llm={"extract_category_query": "nao json"})

# ---- Período
ROWS = [
    ["Padaria do Zé", -25.5, "Padaria", "2026-10-07", "Alimentação"],
    ["Mercado", -180.0, "Supermercado", "2026-10-06", "Alimentação"],
    ["Uber", -32.9, "Transporte", "2026-10-06", "Mobilidade"],
    ["Cinema", -60.0, "Lazer", "2026-10-05", "Diversão"],
]
ROWS_MANY = [["Item %d" % i, -(i + 1) * 1.5, "Padaria", "2026-10-0%d" % (1 + i % 7), "Alimentação"] for i in range(8)]
for _pt in ["hoje", "ontem", "final_de_semana", "esta_semana", "semana_passada", "ultimos_7_dias", "este_mes", "mes_passado", "desconhecido", None]:
    fin("periodo_" + str(_pt), "012c", "gastos do período", intent="Consulta Período",
        llm={"extract_period_query": js(period_type=_pt, categoria=None)}, period_rows=ROWS)
fin("periodo_sem_chave_period_type", "012c", "gastos", intent="Consulta Período", llm={"extract_period_query": "{}"}, period_rows=ROWS)
fin("periodo_vazio", "012c", "gastos de hoje", intent="Consulta Período", llm={"extract_period_query": js(period_type="hoje", categoria=None)}, period_rows=[])
fin("periodo_muitos_itens", "012c", "gastos do mes", intent="Consulta Período", llm={"extract_period_query": js(period_type="este_mes", categoria=None)}, period_rows=ROWS_MANY)
fin("periodo_categoria", "012c", "quanto gastei com alimentação esse mês", intent="Consulta Período",
    llm={"extract_period_query": js(period_type="este_mes", categoria="alimentação")}, category_rows=[[r[0], r[1], r[3]] for r in ROWS[:2]])
fin("periodo_categoria_vazia", "012c", "quanto gastei com saúde", intent="Consulta Período",
    llm={"extract_period_query": js(period_type="mes_passado", categoria="saúde")}, category_rows=[])
fin("periodo_categoria_muitos", "012c", "quanto gastei com padaria", intent="Consulta Período",
    llm={"extract_period_query": js(period_type="ultimos_7_dias", categoria="padaria")}, category_rows=[[r[0], r[1], r[3]] for r in ROWS_MANY] + [["Extra %d" % i, -1.0, "2026-10-01"] for i in range(5)])
fin("periodo_cota_do_gemini", "012c", "gastos", intent="Consulta Período", llm={"extract_period_query": {"__raise__": "429 Quota exceeded"}})
