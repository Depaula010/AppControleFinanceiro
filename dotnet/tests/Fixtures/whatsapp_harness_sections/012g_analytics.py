# -*- coding: utf-8 -*-
"""
Secao do harness (executada no escopo de whatsapp_handler_harness.py, depois de 012c_zz_gravar.py): sub-entrega 012g (analises e grafico).
Roda o `WhatsAppHandler` REAL com as intencoes reais de analytics_intents.py (AnaliseInteligenteIntent, ComparacaoMensalIntent,
PrevisaoGastosIntent, GraficoGastosIntent) sobre os servicos REAIS analytics_service.py, forecast_service.py e chart_service.py, com banco
falso em memoria (as linhas de cada consulta vem do roteiro do cenario), Gemini roteirizado e matplotlib simulado (grava os dados que
iriam para o desenho). Saida: tests/Fixtures/whatsapp-analytics-scenarios.json (lida por AnalyticsParityTests).

Hoje fixo: 2026-10-07 (quarta), 14:20 em Sao Paulo. Todos os valores sao FICTICIOS. No fim a secao restaura tudo que trocou (outros
geradores importam este harness).
"""
import copy as _g_copy
import unittest.mock as _g_mock

sys.path.insert(0, HERE)
import efh_support as _gS  # noqa: E402

G_SCENARIOS = []
G_EXTRA = {}

# ---------------------------------------------------------------- estado a restaurar no fim
_G_MISSING = object()
_g_saved_modules = {k: sys.modules.get(k, _G_MISSING) for k in (
    "datetime", "sqlalchemy", "matplotlib", "matplotlib.pyplot", "app.services.analytics_service", "app.services.forecast_service",
    "app.services.chart_service")}
_g_saved_clock = _gS.CLOCK.now
_g_saved_label = prompt_label
_g_saved_services_attrs = {k: getattr(sys.modules["app.services"], k, _G_MISSING) for k in ("chart_service", "notification_service")}
_g_saved_utils = {k: getattr(sys.modules["app.utils"], k, _G_MISSING) for k in ("formatar_mes_pt",)}
_g_saved_real_intents = dict(REAL_INTENTS)
_g_app = sys.modules["app"]
_g_saved_app = {k: getattr(_g_app, k, _G_MISSING) for k in ("db_engine", "gemini_model")}

# ---------------------------------------------------------------- relogio fixo e modulos de infraestrutura
_gS.CLOCK.now = _gS.sp(2026, 10, 7, 14, 20)
sys.modules["datetime"] = _gS._proxy()

_g_sqlalchemy = types.ModuleType("sqlalchemy")
_g_sqlalchemy.text = lambda s: s
sys.modules["sqlalchemy"] = _g_sqlalchemy

# ---------------------------------------------------------------- matplotlib simulado (grava os dados que iriam para o desenho)
G_PLT = {"pie": None, "legend": None, "title": None, "bar": [], "xticklabels": None, "plot": None, "ax_title": None}
_g_ax = _g_mock.MagicMock(name="ax")
_g_plt = _g_mock.MagicMock(name="plt")


def _g_pie(values, **kw):
    G_PLT["pie"] = {"values": list(values), "labels": list(kw["labels"])}
    return [], [], []


def _g_savefig(buf, **kw):
    buf.write(b"FAKE-PNG")


_g_plt.pie.side_effect = _g_pie
_g_plt.savefig.side_effect = _g_savefig
_g_plt.legend.side_effect = lambda labels, **kw: G_PLT.__setitem__("legend", list(labels))
_g_plt.title.side_effect = lambda text, **kw: G_PLT.__setitem__("title", text)
_g_plt.subplots.return_value = (_g_mock.MagicMock(name="fig"), _g_ax)
_g_ax.bar.side_effect = lambda xs, ys, width, **kw: (G_PLT["bar"].append({"label": kw.get("label"), "values": list(ys)}), [])[1]
_g_ax.set_xticklabels.side_effect = lambda labels, **kw: G_PLT.__setitem__("xticklabels", list(labels))
_g_ax.plot.side_effect = lambda xs, ys, **kw: G_PLT.__setitem__("plot", {"x": [d.isoformat() for d in xs], "y": list(ys)})
_g_ax.set_title.side_effect = lambda text, **kw: G_PLT.__setitem__("ax_title", text)
_g_mpl = types.ModuleType("matplotlib")
_g_mpl.use = lambda *a, **k: None
_g_mpl.pyplot = _g_plt
sys.modules["matplotlib"] = _g_mpl
sys.modules["matplotlib.pyplot"] = _g_plt


# ---------------------------------------------------------------- banco falso das consultas de analise, previsao e grafico
def _g_dec(v):
    return None if v is None else _decimal.Decimal(str(v))


def _g_date(v):
    return _dt.date.fromisoformat(v)


class _GResult(Result):
    def scalar(self):
        return self.rows[0] if self.rows else None


class _GConn(FakeConn):
    def execute(self, sql, params=None):
        s = " ".join(str(sql).split())
        cfg = ENV.cfg
        label, rows = self._route(s, params, cfg)
        G_EXTRA["sql"].append([label, {k: str(v) for k, v in (params or {}).items() if k != "uid"}])
        if label in cfg.get("db_fail", []):
            raise Exception("boom segredo")
        return rows

    def _route(self, s, params, cfg):
        if s.startswith("SELECT TO_CHAR(data_transacao, 'YYYY-MM') as mes, SUM(valor) as total FROM Transacoes") and "IN (:mes_atual" not in s:
            return "monthly", _GResult([Row(mes=r[0], total=_g_dec(r[1])) for r in cfg.get("monthly", [])])
        if "ORDER BY total DESC LIMIT 10" in s:
            return "by_category", _GResult([Row(nome_macro=r[0], nome_subcategoria=r[1], total=_g_dec(r[2]), quantidade=r[3]) for r in cfg.get("by_category", [])])
        if "as dia_semana" in s:
            return "weekday", _GResult([Row(dia_semana=r[0], total=_g_dec(r[1]), quantidade=r[2]) for r in cfg.get("weekday", [])])
        if "IN (:mes_atual, :mes_anterior)" in s:
            return "comparison", _GResult([Row(mes=r[0], total=_g_dec(r[1])) for r in cfg.get("comparison", [])])
        if "FROM PotesDeGastos p" in s:
            return "pots", _GResult([Row(nome_pote=r[0], valor_limite=_g_dec(r[1]), periodicidade=r[2], gasto_atual=_g_dec(r[3])) for r in cfg.get("pots", [])])
        if "ORDER BY valor DESC LIMIT 5" in s:
            return "top", _GResult([Row(descricao=r[0], valor=_g_dec(r[1]), data_transacao=_g_date(r[2]), nome_subcategoria=r[3]) for r in cfg.get("top", [])])
        if "ILIKE '%delivery%'" in s:
            return "delivery", _GResult([])
        if "total_parcelas" in s and "FROM Agendamentos WHERE" in s:
            return "f_fixed", _GResult([Row(descricao=r[0], valor_previsto=_g_dec(r[1]), periodicidade=r[2], dia_execucao=r[3], tipo_agendamento=r[4],
                                            total_parcelas=r[5], parcelas_executadas=r[6]) for r in cfg.get("f_fixed", [])])
        if "tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL') ORDER BY valor_previsto DESC" in s:
            return "fixed", _GResult([Row(descricao=r[0], valor_previsto=_g_dec(r[1]), periodicidade=r[2], dia_execucao=r[3]) for r in cfg.get("fixed", [])])
        if "ORDER BY mes DESC, total DESC" in s:
            return "history", _GResult([])
        if "SUM(valor) as total_ate_hoje" in s:
            mtd = cfg.get("f_mtd", [None, 0])
            return "f_mtd", _GResult([Row(total_ate_hoje=_g_dec(mtd[0]), quantidade=mtd[1])])
        if "NOT EXISTS" in s:
            return "f_pending", _GResult([Row(descricao=r[0], valor_previsto=_g_dec(r[1]), dia_execucao=r[2], tipo_conta=r[3], nome_grupo=r[4])
                                          for r in cfg.get("f_pending", [])])
        if "EXTRACT(DAY FROM data_transacao) as dia_mes" in s:
            return "daily", _GResult([])
        if "IN (:mes_ref, :mes_ant)" in s:
            return "mc", _GResult([Row(mes=r[0], nome_macro=r[1], total=_g_dec(r[2])) for r in cfg.get("mc", [])])
        if "COALESCE(mc.nome_macro, 'Outros') as categoria" in s:
            return "pie", _GResult([Row(categoria=r[0], total=_g_dec(r[1])) for r in cfg.get("pie_rows", [])])
        if "DATE_TRUNC('month', data_transacao) as mes" in s:
            return "bar", _GResult([Row(mes=_g_date(r[0]), despesas=_g_dec(r[1]), rendas=_g_dec(r[2])) for r in cfg.get("bar_rows", [])])
        if "COALESCE(SUM(saldo_inicial), 0)" in s:
            return "initial_balance", _GResult([_g_dec(cfg.get("saldo_inicial", 0))])
        if "SUM(valor) OVER (ORDER BY data_transacao)" in s:
            return "line", _GResult([Row(data_transacao=_g_date(r[0]), saldo_acumulado=_g_dec(r[1])) for r in cfg.get("line_rows", [])])
        raise AssertionError("SQL nao simulado: " + s[:120])


class _GEngine:
    def connect(self):
        return _GConn()


class _GReply:
    def __init__(self, text):
        self.text = text
        self.prompt_feedback = ""


class _GModel:
    """gemini_model global do analytics_service e do forecast_service (um prompt por intencao)."""

    def generate_content(self, prompt):
        label = "forecast" if prompt.startswith("\nVocê é um assistente financeiro. Analise os dados") else "analysis"
        G_EXTRA["prompts"].append({"label": label, "prompt": prompt})
        ENV.call("gemini_model.generate_content:" + label)
        script = ENV.cfg.get("llm", {}).get(label)
        if script is None:
            raise AssertionError("Gemini nao roteirizado: " + label)
        if isinstance(script, dict) and script.get("__raise__"):
            raise Exception(script["__raise__"])
        return _GReply(script)


_g_app.db_engine = _GEngine()
_g_app.gemini_model = _GModel()
sys.modules["app.utils"].formatar_mes_pt = _df_ns["formatar_mes_pt"]
_g_analytics = load("analytics_service.py", source("services", "analytics_service.py"))
_g_forecast = load("forecast_service.py", source("services", "forecast_service.py"))
_g_chart = load("chart_service.py", source("services", "chart_service.py"))
stub_module("app.services.analytics_service", generate_ai_insights=_g_analytics["generate_ai_insights"],
            get_monthly_comparison=_g_analytics["get_monthly_comparison"])
stub_module("app.services.forecast_service", generate_forecast_insights=_g_forecast["generate_forecast_insights"])
_g_chart_mod = stub_module("app.services.chart_service", generate_pie_chart=_g_chart["generate_pie_chart"],
                           generate_bar_chart=_g_chart["generate_bar_chart"], generate_line_chart=_g_chart["generate_line_chart"])
sys.modules["app.services"].chart_service = _g_chart_mod
for _k, _v in _g_saved_app.items():
    if _v is _G_MISSING:
        delattr(_g_app, _k)
    else:
        setattr(_g_app, _k, _v)


class _GNotifications:
    @staticmethod
    def enviar_imagem_whatsapp_bytes(numero, image_bytes, caption, bot_url, api_key):
        ENV.call("notification_service.enviar_imagem_whatsapp_bytes")
        G_EXTRA["sent"].append({"numero": numero, "bytes": len(image_bytes) > 0, "caption": caption})
        if ENV.cfg.get("send_raise"):
            raise Exception("boom segredo")
        return ENV.cfg.get("send_ok", True)


sys.modules["app.services"].notification_service = _GNotifications

_g_an_ns = _an_ns  # analytics_intents.py carregado pela 012c
REAL_INTENTS.update({
    "Análise Inteligente": _g_an_ns["AnaliseInteligenteIntent"],
    "Comparação Mensal": _g_an_ns["ComparacaoMensalIntent"],
    "Previsão de Gastos": _g_an_ns["PrevisaoGastosIntent"],
    "Gráfico de Gastos": _g_an_ns["GraficoGastosIntent"],
})


def prompt_label(prompt):  # noqa: F811  (o ScriptedModel resolve o nome em tempo de chamada)
    if prompt.startswith('Analise a mensagem: "') and "Tipos de gráfico" in prompt:
        return "extract_chart_type"
    return _g_saved_label(prompt)


def gs(name, texto, **cfg):
    G_EXTRA.clear()
    G_EXTRA.update({"prompts": [], "sent": [], "sql": []})
    for key in ("pie", "legend", "title", "xticklabels", "plot", "ax_title"):
        G_PLT[key] = None
    G_PLT["bar"] = []
    deviation = cfg.pop("deviation", None)
    item = scenario(name, "012g", texto, deviation=deviation, **cfg)
    item["result"]["extra"] = {"prompts": list(G_EXTRA["prompts"]), "sent": list(G_EXTRA["sent"]), "sql": [list(s) for s in G_EXTRA["sql"]],
                               "plt": _g_copy.deepcopy(G_PLT)}
    G_SCENARIOS.append(item)


# ---------------------------------------------------------------- Analise Inteligente
LLM_OK = "  *Resumo do Mês*\nVocê gastou bastante.\n\n"
ANALYSIS_FULL = dict(
    comparison=[["2026-10", -1234.5], ["2026-09", -1000.25]],
    by_category=[["Alimentação", "Supermercado", -400.1, 5], ["Moradia", "Luz", -250.0, 1], ["Lazer", "Cinema", -120.5, 3],
                 ["Transporte", "Uber", -90.0, 7], ["Saúde", "Farmácia", -45.9, 2], ["Casa", "Reparos", -30.0, 1], ["Outros", "Diversos", -5.0, 1]],
    weekday=[["Sunday   ", -80.0, 2], ["Monday   ", -150.5, 4], ["Wednesday", -1004.0, 9]],
    pots=[["Alimentação", 800.0, "MENSAL", -400.1], ["Lazer", 0, "MENSAL", -120.5], ["Reserva", 333.33, "SEMANAL", 0]],
    top=[["Aluguel", -250.0, "2026-10-05", "Luz"], ["Mercado", -120.0, "2026-10-03", "Supermercado"]],
    fixed=[["Netflix", -55.9, "MENSAL", 5], ["Aluguel", -1500.0, "MENSAL", 10], ["Internet", -99.9, "MENSAL", 12], ["Academia", -120.0, "MENSAL", 1],
           ["Seguro", -300.0, "ANUAL", 20], ["Curso", -80.0, "MENSAL", 3]],
)
gs("analise_completa", "como estão minhas finanças", intent="Análise Inteligente", llm={"analysis": LLM_OK}, **ANALYSIS_FULL)
gs("analise_sem_dados", "análise dos meus gastos", intent="Análise Inteligente", llm={"analysis": "Sem dados."})
gs("analise_so_mes_atual", "insights", intent="Análise Inteligente", llm={"analysis": "ok"}, comparison=[["2026-10", -10.0]])
gs("analise_so_mes_anterior", "insights", intent="Análise Inteligente", llm={"analysis": "ok"}, comparison=[["2026-09", -77.7]])
gs("analise_mes_anterior_zero", "insights", intent="Análise Inteligente", llm={"analysis": "ok"}, comparison=[["2026-10", -10.0], ["2026-09", 0]])
gs("analise_gasto_positivo_e_arredondamento", "insights", intent="Análise Inteligente", llm={"analysis": "ok"},
   comparison=[["2026-10", 0.125], ["2026-09", 0.375]],
   by_category=[["A", "B", 1234567.891, 2], ["C", "D", 0.005, 1], ["E", "F", 2.675, 1]],
   pots=[["P1", 3, "MENSAL", 1], ["P2", 7, "ANUAL", 4.9], ["P3", 200, "QUINZENAL", 0.05]])
gs("analise_variacao_negativa", "insights", intent="Análise Inteligente", llm={"analysis": "ok"}, comparison=[["2026-10", -80.0], ["2026-09", -100.0]])
gs("analise_resposta_com_espacos", "insights", intent="Análise Inteligente", llm={"analysis": "\n\n  texto com espaços  \n\t"}, **ANALYSIS_FULL)
gs("analise_gemini_falha", "insights", intent="Análise Inteligente", llm={"analysis": {"__raise__": "429 Quota exceeded"}}, **ANALYSIS_FULL)
gs("analise_banco_falha_categorias", "insights", intent="Análise Inteligente", llm={"analysis": "ok"}, db_fail=["by_category"], **ANALYSIS_FULL)
gs("analise_banco_falha_potes", "insights", intent="Análise Inteligente", llm={"analysis": "ok"}, db_fail=["pots"], **ANALYSIS_FULL)

# ---------------------------------------------------------------- Comparacao Mensal
MC_FULL = [
    ["2026-10", "Moradia", -1500.0], ["2026-09", "Moradia", -1400.0], ["2026-10", "Alimentação", -700.0], ["2026-09", "Alimentação", -900.0],
    ["2026-09", "Lazer", -300.0], ["2026-10", "Transporte", -250.0], ["2026-09", "Transporte", -200.0], ["2026-10", "Saúde", -60.0],
    ["2026-09", "Saúde", -55.0], ["2026-10", "Educação", -45.5], ["2026-09", "Educação", -40.0], ["2026-10", "Pets", -12.5], ["2026-09", "Pets", -11.0],
]
gs("comparacao_completa", "comparar com o mês passado", intent="Comparação Mensal", mc=MC_FULL)
gs("comparacao_sem_dados", "comparar com o mês passado", intent="Comparação Mensal", mc=[])
gs("comparacao_so_mes_atual", "gastei mais?", intent="Comparação Mensal", mc=[["2026-10", "Moradia", -1500.0]])
gs("comparacao_so_mes_anterior", "gastei mais?", intent="Comparação Mensal", mc=[["2026-09", "Moradia", -1500.0]])
gs("comparacao_limiares", "gastei mais?", intent="Comparação Mensal",
   mc=[["2026-10", "A", -110.0], ["2026-09", "A", -100.0], ["2026-10", "B", -89.9], ["2026-09", "B", -100.0], ["2026-10", "C", -105.0],
       ["2026-09", "C", -100.0], ["2026-10", "D", -85.0], ["2026-09", "D", -100.0], ["2026-10", "E", -100.0], ["2026-09", "E", -100.0]])
gs("comparacao_valores_positivos", "gastei mais?", intent="Comparação Mensal",
   mc=[["2026-10", "A", 120.0], ["2026-09", "A", 100.0], ["2026-10", "B", 30.0], ["2026-09", "B", 40.0]])
gs("comparacao_anterior_soma_zero", "gastei mais?", intent="Comparação Mensal",
   mc=[["2026-10", "A", 50.0], ["2026-09", "A", 10.0], ["2026-09", "B", -10.0]])
gs("comparacao_banco_falha", "gastei mais?", intent="Comparação Mensal", mc=MC_FULL, db_fail=["mc"])

# ---------------------------------------------------------------- Previsao de Gastos
FORECAST_BASE = dict(
    f_monthly=None,
    f_fixed=[["Aluguel", -1500.0, "MENSAL", 10, "FIXO", None, None], ["Tênis", -150.0, "MENSAL", 15, "PARCELADO", 6, 2],
             ["Luz", -180.0, "MENSAL", 20, "LEMBRETE_VARIAVEL", None, None]],
    f_mtd=[-420.5, 9],
    f_pending=[["Aluguel", -1500.0, 10, "Conta Corrente", "Moradia"], ["Cartão Inter", -300.0, 25, "Cartão de Crédito", "Financeiro"],
               ["Salário", 5000.0, 5, "Conta Corrente", "Renda"]],
)
HISTORY_6 = [["2026-10", -420.5], ["2026-09", -2100.0], ["2026-08", -1800.75], ["2026-07", -2500.0], ["2026-06", -1900.0], ["2026-05", -2300.0], ["2026-04", -1200.0]]


def forecast(name, **over):
    cfg = {k: v for k, v in FORECAST_BASE.items() if k != "f_monthly"}
    cfg["monthly"] = HISTORY_6
    cfg.update(over)
    llm = cfg.pop("llm", {"forecast": "Projeção ok"})
    gs(name, cfg.pop("texto", "quanto vou gastar este mês"), intent="Previsão de Gastos", llm=llm, **cfg)


forecast("previsao_completa")
forecast("previsao_sem_historico_quebra", monthly=[])
forecast("previsao_sem_pendentes", f_pending=[])
forecast("previsao_so_despesas_normais", f_pending=[["Aluguel", -1500.0, 10, "Conta Corrente", "Moradia"]])
forecast("previsao_so_cartao", f_pending=[["Cartão Inter", -300.0, 25, "Cartão de Crédito", "Financeiro"]])
forecast("previsao_so_receitas", f_pending=[["Salário", 5000.0, 5, "Conta Corrente", "Renda"]])
forecast("previsao_receita_no_cartao", f_pending=[["Estorno", 40.0, None, "Cartão de Crédito", "Renda"]])
forecast("previsao_sem_contas_fixas", f_fixed=[])
forecast("previsao_mais_de_cinco_fixas", f_fixed=[["F%d" % i, -(i + 1) * 10.5, "MENSAL", i + 1, "FIXO", None, None] for i in range(8)])
forecast("previsao_sem_gasto_no_mes", f_mtd=[None, 0])
forecast("previsao_gasto_zero", f_mtd=[0, 1])
forecast("previsao_historico_curto", monthly=[["2026-10", -420.5]])
forecast("previsao_historico_positivo", monthly=[["2026-10", 100.0], ["2026-09", 300.0]], f_mtd=[50.0, 3], f_pending=[["X", 20.0, 3, "Conta Corrente", "Moradia"]])
forecast("previsao_pendentes_dominam", monthly=[["2026-10", -10.0], ["2026-09", -20.0]], f_mtd=[-5.0, 1],
         f_pending=[["Grande", 900.0, 28, "Conta Corrente", "Moradia"], ["Outra", 100.5, 29, "Cartão de Crédito", "Lazer"]])
forecast("previsao_media_domina", monthly=[["2026-10", 500.0], ["2026-09", 600.0]], f_mtd=[7.0, 1], f_pending=[])
forecast("previsao_arredondamento", monthly=[["2026-10", 100.125], ["2026-09", 200.375]], f_mtd=[33.335, 2],
         f_pending=[["X", 0.005, 3, "Conta Corrente", "Moradia"], ["Y", 2.675, 4, "Conta Corrente", "Lazer"]])
forecast("previsao_mais_de_seis_meses", monthly=HISTORY_6 + [["2026-03", -50.0]])
forecast("previsao_gemini_falha", llm={"forecast": {"__raise__": "boom"}})
forecast("previsao_resposta_com_espacos", llm={"forecast": "  \n texto \n  "})
forecast("previsao_banco_falha", db_fail=["f_pending"])

# ---------------------------------------------------------------- Grafico de Gastos
CHART_PIE = '{"tipo_grafico": "pizza", "periodo_dias": 30}'
PIE_ROWS = [["Moradia", 1500.0], ["Alimentação", 820.55], ["Transporte", 250.0], ["Lazer", 0.0], ["Outros", 12.345]]
BAR_ROWS = [["2026-05-01", 1200.5, 5000.0], ["2026-06-01", 1800.0, 5000.0], ["2026-07-01", 2100.25, 0], ["2026-08-01", 0, 5200.5],
            ["2026-09-01", 1999.99, 5000.0], ["2026-10-01", 420.5, 0]]
LINE_ROWS = [["2026-08-02", -50.0], ["2026-08-15", 4950.0], ["2026-08-15", 4950.0], ["2026-09-01", 3000.5], ["2026-10-05", 2579.5]]


def chart(name, llm_reply, texto="gráfico dos meus gastos", numero=None, **cfg):
    kwargs = dict(intent="Gráfico de Gastos", llm={"extract_chart_type": llm_reply}, **cfg)
    if numero is not None:
        kwargs["numero"] = numero
    gs(name, texto, **kwargs)


chart("grafico_pizza_padrao", CHART_PIE, pie_rows=PIE_ROWS)
chart("grafico_pizza_sete_dias", '{"tipo_grafico": "pizza", "periodo_dias": 7}', pie_rows=PIE_ROWS)
chart("grafico_pizza_cerca_de_codigo", '```json\n{"tipo_grafico": "pizza", "periodo_dias": 15}\n```', pie_rows=PIE_ROWS)
chart("grafico_pizza_sem_periodo_usa_30", '{"tipo_grafico": "pizza"}', pie_rows=PIE_ROWS)
chart("grafico_sem_tipo_vira_pizza", '{"periodo_dias": 10}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_nulo", '{"tipo_grafico": "pizza", "periodo_dias": null}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_decimal", '{"tipo_grafico": "pizza", "periodo_dias": 7.5}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_inteiro_decimal", '{"tipo_grafico": "pizza", "periodo_dias": 30.0}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_texto", '{"tipo_grafico": "pizza", "periodo_dias": "30"}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_booleano", '{"tipo_grafico": "pizza", "periodo_dias": true}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_negativo", '{"tipo_grafico": "pizza", "periodo_dias": -3}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_enorme", '{"tipo_grafico": "pizza", "periodo_dias": 99999999999}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_fora_do_calendario", '{"tipo_grafico": "pizza", "periodo_dias": 800000}', pie_rows=PIE_ROWS)
chart("grafico_pizza_periodo_lista", '{"tipo_grafico": "pizza", "periodo_dias": [30]}', pie_rows=PIE_ROWS)
chart("grafico_pizza_sem_dados", CHART_PIE, pie_rows=[])
chart("grafico_pizza_so_zeros", CHART_PIE, pie_rows=[["A", 0.0], ["B", 0]])
chart("grafico_pizza_categoria_vazia_ignorada", CHART_PIE, pie_rows=[["", 10.0], ["Moradia", 20.0]])
chart("grafico_pizza_banco_falha", CHART_PIE, pie_rows=PIE_ROWS, db_fail=["pie"])
chart("grafico_barras", '{"tipo_grafico": "barras", "num_meses": 6}', bar_rows=BAR_ROWS)
chart("grafico_barras_tres_meses", '{"tipo_grafico": "barras", "num_meses": 3}', bar_rows=BAR_ROWS[3:])
chart("grafico_barras_sem_periodo", '{"tipo_grafico": "barras"}', bar_rows=BAR_ROWS)
chart("grafico_barras_meses_decimal", '{"tipo_grafico": "barras", "num_meses": 2.5}', bar_rows=BAR_ROWS)
chart("grafico_barras_meses_texto", '{"tipo_grafico": "barras", "num_meses": "6"}', bar_rows=BAR_ROWS)
chart("grafico_barras_meses_nulo", '{"tipo_grafico": "barras", "num_meses": null}', bar_rows=BAR_ROWS)
chart("grafico_barras_sem_dados", '{"tipo_grafico": "barras", "num_meses": 6}', bar_rows=[])
chart("grafico_barras_banco_falha", '{"tipo_grafico": "barras", "num_meses": 6}', bar_rows=BAR_ROWS, db_fail=["bar"])
chart("grafico_linha", '{"tipo_grafico": "linha", "num_meses": 6}', saldo_inicial=1500.25, line_rows=LINE_ROWS)
chart("grafico_linha_sem_saldo_inicial", '{"tipo_grafico": "linha", "num_meses": 4}', saldo_inicial=None, line_rows=LINE_ROWS)
chart("grafico_linha_um_mes", '{"tipo_grafico": "linha", "num_meses": 1}', saldo_inicial=0, line_rows=LINE_ROWS[-1:])
chart("grafico_linha_sem_dados", '{"tipo_grafico": "linha", "num_meses": 6}', saldo_inicial=10, line_rows=[])
chart("grafico_linha_banco_falha_saldo", '{"tipo_grafico": "linha", "num_meses": 6}', saldo_inicial=10, line_rows=LINE_ROWS, db_fail=["initial_balance"])
chart("grafico_linha_meses_booleano", '{"tipo_grafico": "linha", "num_meses": true}', saldo_inicial=10, line_rows=LINE_ROWS)
chart("grafico_tipo_desconhecido", '{"tipo_grafico": "rosca", "periodo_dias": 30}', pie_rows=PIE_ROWS)
chart("grafico_tipo_nulo", '{"tipo_grafico": null}', pie_rows=PIE_ROWS)
chart("grafico_tipo_numero", '{"tipo_grafico": 5}', pie_rows=PIE_ROWS)
chart("grafico_tipo_maiusculo_nao_casa", '{"tipo_grafico": "Pizza"}', pie_rows=PIE_ROWS)
chart("grafico_envio_falha", CHART_PIE, pie_rows=PIE_ROWS, send_ok=False)
chart("grafico_envio_lanca", CHART_PIE, pie_rows=PIE_ROWS, send_raise=True)
chart("grafico_barras_envio_falha", '{"tipo_grafico": "barras"}', bar_rows=BAR_ROWS, send_ok=False)
chart("grafico_sem_numero", CHART_PIE, numero="@s.whatsapp.net", pie_rows=PIE_ROWS)
chart("grafico_json_invalido", "nao e json", pie_rows=PIE_ROWS)
chart("grafico_json_lista", "[1, 2]", pie_rows=PIE_ROWS)
chart("grafico_gemini_cota", {"__raise__": "429 Quota exceeded"}, pie_rows=PIE_ROWS)

# ---------------------------------------------------------------- grava e restaura
GA_OUT = os.path.join(HERE, "whatsapp-analytics-scenarios.json")
with open(GA_OUT, "w", encoding="utf-8", newline="\n") as _g_fh:
    json.dump({"api_key": API_KEY, "number": NUMBER, "today": "2026-10-07", "scenarios": G_SCENARIOS}, _g_fh, ensure_ascii=False, indent=1)
_REAL_STDOUT.write("ok %s %d\n" % (GA_OUT, len(G_SCENARIOS)))

for _k, _v in _g_saved_modules.items():
    if _v is _G_MISSING:
        sys.modules.pop(_k, None)
    else:
        sys.modules[_k] = _v
_gS.CLOCK.now = _g_saved_clock
prompt_label = _g_saved_label  # noqa: F811
for _k, _v in _g_saved_services_attrs.items():
    if _v is _G_MISSING:
        delattr(sys.modules["app.services"], _k)
    else:
        setattr(sys.modules["app.services"], _k, _v)
for _k, _v in _g_saved_utils.items():
    if _v is _G_MISSING:
        delattr(sys.modules["app.utils"], _k)
    else:
        setattr(sys.modules["app.utils"], _k, _v)
REAL_INTENTS.clear()
REAL_INTENTS.update(_g_saved_real_intents)
