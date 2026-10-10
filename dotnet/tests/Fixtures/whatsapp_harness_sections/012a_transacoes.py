# -*- coding: utf-8 -*-
"""
Secao do harness (executada no escopo de whatsapp_handler_harness.py): infraestrutura e cenarios das sub-entregas
012a (transacoes), 012b (pagamentos) e 012c (consultas financeiras). A saida vai para tests/Fixtures/whatsapp-financial-scenarios.json
(arquivo proprio, para nao colidir com o das demais sub-entregas).

Aqui sao REAIS: base_intent.py, transaction_intents.py, os trechos de finance_service.py que escolhem a conta
(extract_mentioned_account, resolve_account_for_transaction, choose_account_for_transaction, get_user_default_accounts),
gemini_service.py (extract_*_params, extract_transaction_details, categorize_transaction, ... com o modelo roteirizado),
FixedBillsService.find_matching_bill e a mensagem de confirmacao. O que escreve no banco e simulado e registra a chamada.
Todos os valores sao FICTICIOS.
"""
import datetime as _dt
import decimal as _decimal

# ---------------------------------------------------------------- data fixa (date.today())
FIXED_TODAY = _dt.date(2026, 10, 7)


class FrozenDate(_dt.date):
    @classmethod
    def today(cls):
        return cls(FIXED_TODAY.year, FIXED_TODAY.month, FIXED_TODAY.day)


_proxy = types.ModuleType("datetime")
_proxy.__dict__.update(_dt.__dict__)
_proxy.date = FrozenDate
sys.modules["datetime"] = _proxy
handler_ns["date"] = FrozenDate


# ---------------------------------------------------------------- Redis que pode falhar
class FailingRedis(FakeRedis):
    def set_with_ttl(self, key, value, ttl_seconds=300):
        self.writes.append(["set", key, ttl_seconds])
        return False


_BaseEnv = Env


class Env(_BaseEnv):  # noqa: F811  (run() resolve `Env` pelo nome, em tempo de chamada)
    def __init__(self, cfg):
        super().__init__(cfg)
        self.seq = {}
        if cfg.get("redis_fail"):
            self.redis = FailingRedis(cfg.get("redis"))


# ---------------------------------------------------------------- conexao que serve as consultas reais
class Row:
    def __init__(self, **kw):
        self.__dict__.update(kw)


class Result:
    def __init__(self, rows):
        self.rows = list(rows)

    def fetchall(self):
        return list(self.rows)

    def fetchone(self):
        return self.rows[0] if self.rows else None


def _accounts():
    return [Row(id=a["id"], nome_conta=a["nome_conta"], tipo_conta=a["tipo_conta"]) for a in ENV.cfg.get("accounts", [])]


class FinConn(FakeConn):
    def execute(self, sql, params=None):
        s = " ".join(str(sql).split())
        cfg = ENV.cfg
        if "FROM Contas WHERE usuario_id = :uid LIMIT 1" in s:
            return Result(_accounts()[:1])
        if s.endswith("FROM Contas WHERE usuario_id = :uid"):
            return Result(_accounts())
        if "FROM Contas WHERE id = :cid AND usuario_id = :uid" in s:
            return Result([a for a in _accounts() if a.id == params["cid"]])
        if "FROM Usuarios" in s and "conta_padrao_renda_id" in s:
            d = cfg.get("default_accounts") or {}
            return Result([Row(conta_padrao_renda_id=d.get("renda"), conta_padrao_despesa_id=d.get("despesa"))])
        if "FROM Agendamentos a" in s and "ILIKE :descricao" in s:
            sch = cfg.get("schedule")
            if not sch:
                return Result([])
            return Result([Row(id=sch["id"], descricao=sch["descricao"], valor_previsto=_decimal.Decimal(str(sch["valor_previsto"])),
                               conta_id=sch["conta_id"], subcategoria_id=sch["subcategoria_id"])])
        if "FROM Agendamentos a" in s and "tipo_agendamento IN" in s:
            return Result([Row(id=b["id"], descricao=b["descricao"], valor_previsto=(None if b["valor_previsto"] is None else _decimal.Decimal(str(b["valor_previsto"]))),
                               dia_execucao=b["dia_execucao"], tipo_agendamento=b["tipo_agendamento"], conta_id=b["conta_id"], categoria=b["categoria"])
                           for b in cfg.get("bills", [])])
        raise AssertionError("SQL nao simulado: " + s[:90])


class FinEngine:
    def connect(self):
        return FinConn()


handler_ns["db_engine"] = FinEngine()
sys.modules["app"].db_engine = handler_ns["db_engine"]


# ---------------------------------------------------------------- servicos simulados com roteiro (sequencias e excecoes)
class SeqRec:
    def __init__(self, name):
        self._name = name

    def __getattr__(self, method):
        label = "%s.%s" % (self._name, method)

        def call(*args, **kwargs):
            ENV.call(label)
            result = ENV.cfg.get("returns", {}).get(label)
            if isinstance(result, dict) and "__seq__" in result:
                index = ENV.seq.get(label, 0)
                ENV.seq[label] = index + 1
                items = result["__seq__"]
                result = items[index] if index < len(items) else items[-1]
            if isinstance(result, dict) and result.get("__raise__"):
                raise Exception(result["__raise__"])
            if isinstance(result, dict) and "__tuple__" in result:
                return tuple(result["__tuple__"])
            return result
        return call


_fs_ns = load("finance_service.py", source("services", "finance_service.py"))
_REAL_FINANCE = {"extract_mentioned_account", "get_user_default_accounts", "choose_account_for_transaction", "resolve_account_for_transaction"}


class FinanceFacade:
    def __getattr__(self, name):
        if name in _REAL_FINANCE:
            return _fs_ns[name]
        if name == "get_user_accounts":
            def get_user_accounts(conn, usuario_id):
                ENV.call("finance_service.get_user_accounts")
                return [(a["id"], a["nome_conta"], a["tipo_conta"]) for a in ENV.cfg.get("accounts", [])]
            return get_user_accounts
        if name == "create_transaction":
            inner = getattr(SeqRec("finance_service"), name)

            def create_transaction(conn, uid, cid, scid, fid, descricao, *rest, **kw):
                result = inner(conn, uid, cid, scid, fid, descricao, *rest, **kw)
                if descricao is None:
                    raise Exception("null value in column \"descricao\" of relation \"transacoes\" violates not-null constraint")
                return result
            return create_transaction
        return getattr(SeqRec("finance_service"), name)


FINANCE = FinanceFacade()
sys.modules["app.services"].finance_service = FINANCE
stub_module("app.services.finance_service", get_user_accounts=FINANCE.get_user_accounts)  # from app.services.finance_service import ...
handler_ns["finance_service"] = FINANCE

_fb_ns = load("fixed_bills_service.py", source_fixed_bills(), {"_fold099": _fold099})
_aq_ns = load("agendamentos_queries.py", source("services", "queries", "agendamentos_queries.py"))
stub_module("app.services.queries")
stub_module("app.services.queries.agendamentos_queries", AgendamentosQueries=_aq_ns["AgendamentosQueries"])


class _FixedBillsProxy:
    find_matching_bill = staticmethod(_fb_ns["FixedBillsService"].find_matching_bill)

    @staticmethod
    def settle_fixed_bill(*args, **kwargs):
        return getattr(SeqRec("FixedBillsService"), "settle_fixed_bill")(*args, **kwargs)


handler_ns["FixedBillsService"] = _FixedBillsProxy


# ---------------------------------------------------------------- Gemini real com modelo roteirizado
def prompt_label(prompt):
    if prompt.startswith("Analise a mensagem de transferência"):
        return "extract_transfer_details"
    if prompt.startswith("Analise a mensagem de pagamento de fatura"):
        return "extract_fatura_payment_details"
    if prompt.startswith("Analise a pergunta sobre consulta de saldo"):
        return "extract_saldo_query"
    if prompt.startswith("Analise a pergunta sobre consulta de fatura"):
        return "extract_fatura_query"
    if "Minhas subcategorias são" in prompt[:60]:
        return "categorize_transaction"
    if prompt.startswith('Analise a mensagem: "') and "O tipo é:" in prompt:
        return "extract_transaction_details"
    if prompt.startswith('Analise a mensagem: "') and "Extraia APENAS os itens" in prompt:
        return "extract_payment_items"
    if prompt.startswith('Analise a pergunta: "') and "Identifique o período" in prompt:
        return "extract_period_query"
    if prompt.startswith('Analise a pergunta: "') and "nome_categoria" in prompt:
        return "extract_category_query"
    return "desconhecido"


class _Reply:
    def __init__(self, text):
        self.text = text
        self.prompt_feedback = ""


class ScriptedModel:
    def generate_content(self, prompt):
        label = prompt_label(prompt)
        ENV.call("gemini_service." + label)
        script = ENV.cfg.get("llm", {}).get(label)
        if script is None:
            raise AssertionError("Gemini nao roteirizado: " + label)
        if isinstance(script, dict) and script.get("__raise__"):
            raise Exception(script["__raise__"])
        return _Reply(script)


class _CacheStub:
    def cached_generate_content(self, model, prompt, cfg):
        return model.generate_content(prompt)


class _KeysStub:
    @staticmethod
    def obter_chave_api(usuario_id, provedor):
        return "chave-de-teste", "sistema"


for _mod in ("google", "google.generativeai"):
    sys.modules.setdefault(_mod, types.ModuleType(_mod))
sys.modules["google"].generativeai = sys.modules["google.generativeai"]
stub_module("app.services.gemini_cache_service", gemini_cache_service=_CacheStub())
stub_module("app.services.gerenciador_chaves_api", GerenciadorChavesApi=_KeysStub)

_gemini_ns = load("gemini_service.py", source("services", "gemini_service.py").replace("from app import gemini_model", "gemini_model = _MODEL"),
                  {"_MODEL": ScriptedModel()})


class HarnessGemini:
    """get_message_intent roteirizado (como no harness base); o resto e o gemini_service real sobre o modelo roteirizado."""

    def get_message_intent(self, texto, usuario_id=None):
        ENV.call("gemini_service.get_message_intent")
        intent = ENV.cfg.get("intent")
        if isinstance(intent, dict) and intent.get("__raise__"):
            raise Exception(intent["__raise__"])
        return intent

    def __getattr__(self, name):
        return _gemini_ns[name]


GEMINI = HarnessGemini()
sys.modules["app.services"].gemini_service = GEMINI
handler_ns["gemini_service"] = GEMINI

# ---------------------------------------------------------------- intents reais de transacao
_base_ns = load("base_intent.py", source("routes", "webhooks", "intents", "base_intent.py"))
_ti_src = source("routes", "webhooks", "intents", "transaction_intents.py").replace("from .base_intent import ConfirmationRequiredIntent", "")
_ti_ns = load("transaction_intents.py", _ti_src, {"ConfirmationRequiredIntent": _base_ns["ConfirmationRequiredIntent"]})

REAL_INTENTS = {
    "Renda": _ti_ns["RendaIntent"],
    "Despesa": _ti_ns["DespesaIntent"],
    "Transferência": _ti_ns["TransferenciaIntent"],
}
_stub_route_intent = sys.modules["app.routes.webhooks.intents"].route_intent


def route_intent_fin(intent_name, usuario_id, mensagem, conn, numero_whatsapp=None):
    cls = REAL_INTENTS.get(intent_name)
    if cls is None:
        return _stub_route_intent(intent_name, usuario_id, mensagem, conn, numero_whatsapp)
    ENV.call("route_intent:" + intent_name)
    return cls(usuario_id=usuario_id, mensagem=mensagem, conn=conn, numero_whatsapp=numero_whatsapp).handle()


sys.modules["app.routes.webhooks.intents"].route_intent = route_intent_fin
sys.modules["app.routes.webhooks.intents"].INTENT_REGISTRY["Transferência"] = REAL_INTENTS["Transferência"]
# o handler importa route_intent dentro de cada ramo (from ... import route_intent): le o atributo do modulo em tempo de chamada

class _FixedUuid:
    @staticmethod
    def uuid4():
        return "ab12cd34-0000-4000-8000-000000000000"


confirmation_ns["uuid"] = _FixedUuid  # str(uuid.uuid4())[:8] => "ab12cd34"

FIN_SCENARIOS = []


def fin(name, group, texto, **kw):
    FIN_SCENARIOS.append(scenario(name, group, texto, **kw))


# ---------------------------------------------------------------- dados comuns
ACC = [
    {"id": 1, "nome_conta": "Nubank", "tipo_conta": "Conta Corrente"},
    {"id": 2, "nome_conta": "Cartão Inter", "tipo_conta": "Cartão de Crédito"},
    {"id": 3, "nome_conta": "Carteira", "tipo_conta": "Dinheiro"},
]
CATS_D = [
    {"id": 10, "nome_sub": "Supermercado", "nome_macro": "Alimentação"},
    {"id": 11, "nome_sub": "Padaria", "nome_macro": "Alimentação"},
    {"id": 12, "nome_sub": "Luz", "nome_macro": "Moradia"},
    {"id": 59, "nome_sub": "Outros", "nome_macro": "Despesas Gerais"},
]
CATS_R = [
    {"id": 70, "nome_sub": "Salário", "nome_macro": "Receitas"},
    {"id": 71, "nome_sub": "Freelance", "nome_macro": "Receitas"},
    {"id": 79, "nome_sub": "Outros", "nome_macro": "Receitas Gerais"},
]


def tx_returns(income=False, **extra):
    r = {
        "finance_service.get_user_categories": CATS_R if income else CATS_D,
        "finance_service.get_fallback_category_id": 79 if income else 59,
    }
    r.update(extra)
    return r


def js(**kw):
    return json.dumps(kw, ensure_ascii=False)


# ---------------------------------------------------------------- 012a: Renda e Despesa
fin("despesa_simples", "012a", "gastei 50 na padaria", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=50.0, descricao_bruta="padaria"), "categorize_transaction": "11"}, returns=tx_returns())
fin("despesa_valor_inteiro_e_cerca", "012a", "gastei 80 no mercado", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": "```json\n" + js(valor_decimal=80, descricao_bruta="mercado") + "\n```", "categorize_transaction": " `10` "},
    returns=tx_returns())
fin("despesa_categoria_invalida_vira_outros", "012a", "gastei 20 no sorvete", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=20.5, descricao_bruta="sorvete"), "categorize_transaction": "999"}, returns=tx_returns())
fin("despesa_conta_mencionada_cartao", "012a", "gastei 30 no cartão inter", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=30, descricao_bruta="lanche"), "categorize_transaction": "10"},
    returns=tx_returns(**{"finance_service.get_or_create_fatura": 77}))
fin("despesa_cartao_sem_fatura", "012a", "gastei 30 no cartão inter", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=30, descricao_bruta="lanche"), "categorize_transaction": "10"},
    returns=tx_returns(**{"finance_service.get_or_create_fatura": None}))
fin("despesa_conta_padrao", "012a", "gastei 15 em pão", intent="Despesa", accounts=ACC, default_accounts={"renda": 1, "despesa": 3},
    llm={"extract_transaction_details": js(valor_decimal=15, descricao_bruta="pão"), "categorize_transaction": "11"}, returns=tx_returns())
fin("despesa_conta_padrao_inexistente", "012a", "gastei 15 em pão", intent="Despesa", accounts=ACC, default_accounts={"renda": 1, "despesa": 99},
    llm={"extract_transaction_details": js(valor_decimal=15, descricao_bruta="pão"), "categorize_transaction": "11"}, returns=tx_returns())
fin("despesa_mencao_sem_acerto_usa_padrao", "012a", "gastei 15 no xyzzy", intent="Despesa", accounts=ACC, default_accounts={"renda": None, "despesa": 3},
    llm={"extract_transaction_details": js(valor_decimal=15, descricao_bruta="pão"), "categorize_transaction": "11"}, returns=tx_returns())
fin("despesa_sem_valor", "012a", "gastei na padaria", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="padaria")}, returns=tx_returns())
fin("despesa_valor_zero", "012a", "gastei 0", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=0, descricao_bruta="padaria")}, returns=tx_returns())
fin("despesa_valor_negativo", "012a", "gastei -5", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=-5, descricao_bruta="padaria")}, returns=tx_returns())
fin("despesa_valor_baixo", "012a", "gastei 0,001", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=0.001, descricao_bruta="padaria")}, returns=tx_returns())
fin("despesa_valor_texto", "012a", "gastei cinquenta", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal="cinquenta", descricao_bruta="padaria")}, returns=tx_returns())
fin("despesa_descricao_nula", "012a", "gastei 50", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=50, descricao_bruta=None), "categorize_transaction": "59"}, returns=tx_returns())
fin("despesa_sem_chave_descricao", "012a", "gastei 50", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=50), "categorize_transaction": "59"}, returns=tx_returns())
fin("despesa_json_invalido", "012a", "gastei 50", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": "isto nao e json"}, returns=tx_returns())
fin("despesa_parcelada", "012a", "comprei em 3x de 90", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=90, descricao_bruta="tenis", parcelamento=3), "categorize_transaction": "10"},
    returns=tx_returns())
fin("despesa_parcelada_cartao", "012a", "comprei no cartão inter em 3x de 90", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=90, descricao_bruta="tenis", parcelamento=3), "categorize_transaction": "10"},
    returns=tx_returns(**{"finance_service.get_or_create_fatura": 77}))
for _label, _parcelas in [("um", 1), ("muitas", 49), ("texto", "3"), ("decimal", 3.0), ("verdadeiro", True), ("lista", [3])]:
    fin("despesa_parcelas_invalidas_" + _label, "012a", "comprei em parcelas", intent="Despesa", accounts=ACC,
        llm={"extract_transaction_details": js(valor_decimal=90, descricao_bruta="tenis", parcelamento=_parcelas)}, returns=tx_returns())
fin("despesa_parcelas_falsas_ignoradas", "012a", "comprei", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=90, descricao_bruta="tenis", parcelamento=0), "categorize_transaction": "10"}, returns=tx_returns())
fin("despesa_agendamento", "012a", "conta de luz", intent="Despesa", accounts=ACC,
    schedule={"id": 5, "descricao": "Conta de Luz", "valor_previsto": 120.5, "conta_id": 3, "subcategoria_id": 12},
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="luz")}, returns=tx_returns())
fin("despesa_agendamento_valor_zero", "012a", "conta de luz", intent="Despesa", accounts=ACC,
    schedule={"id": 5, "descricao": "Conta de Luz", "valor_previsto": 0, "conta_id": 3, "subcategoria_id": 12},
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="luz")}, returns=tx_returns())
fin("despesa_agendamento_conta_cartao", "012a", "conta de luz", intent="Despesa", accounts=ACC,
    schedule={"id": 5, "descricao": "Conta de Luz", "valor_previsto": 99.9, "conta_id": 2, "subcategoria_id": 12},
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="luz")},
    returns=tx_returns(**{"finance_service.get_or_create_fatura": 77}))
fin("despesa_agendamento_conta_inexistente", "012a", "conta de luz", intent="Despesa", accounts=ACC,
    schedule={"id": 5, "descricao": "Conta de Luz", "valor_previsto": 120.5, "conta_id": 99, "subcategoria_id": 12},
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="luz")}, returns=tx_returns())
fin("despesa_sem_agendamento_sem_valor", "012a", "conta de luz", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="luz")}, returns=tx_returns())
fin("despesa_sem_contas", "012a", "gastei 50 na padaria", intent="Despesa", accounts=[],
    llm={"extract_transaction_details": js(valor_decimal=50, descricao_bruta="padaria")}, returns=tx_returns())
fin("despesa_redis_fora", "012a", "gastei 50 na padaria", intent="Despesa", accounts=ACC, redis_fail=True,
    llm={"extract_transaction_details": js(valor_decimal=50, descricao_bruta="padaria"), "categorize_transaction": "11"}, returns=tx_returns())
fin("despesa_conta_inventada_pelo_modelo", "012a", "gastei 50 na padaria", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=50, descricao_bruta="padaria", conta="com o nubank"), "categorize_transaction": "11"},
    returns=tx_returns())
fin("despesa_data_inventada_pelo_modelo", "012a", "gastei 50 na padaria", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=50, descricao_bruta="padaria", data="2026-10-01"), "categorize_transaction": "11"},
    returns=tx_returns())
fin("despesa_cota_do_gemini", "012a", "gastei 50", intent="Despesa", accounts=ACC,
    llm={"extract_transaction_details": {"__raise__": "429 Quota exceeded"}}, returns=tx_returns())

fin("renda_simples", "012a", "recebi 500 de freelance", intent="Renda", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=500.0, descricao_bruta="freelance"), "categorize_transaction": "71"},
    returns=tx_returns(income=True))
fin("renda_conta_mencionada", "012a", "recebi 1200 no nubank", intent="Renda", accounts=ACC, default_accounts={"renda": 3, "despesa": None},
    llm={"extract_transaction_details": js(valor_decimal=1200, descricao_bruta="salário"), "categorize_transaction": "70"},
    returns=tx_returns(income=True))
fin("renda_conta_padrao", "012a", "recebi 1200", intent="Renda", accounts=ACC, default_accounts={"renda": 3, "despesa": None},
    llm={"extract_transaction_details": js(valor_decimal=1200, descricao_bruta="salário"), "categorize_transaction": "70"},
    returns=tx_returns(income=True))
fin("renda_sem_valor", "012a", "recebi", intent="Renda", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="salário")}, returns=tx_returns(income=True))
fin("renda_agendamento", "012a", "recebi meu salário", intent="Renda", accounts=ACC,
    schedule={"id": 8, "descricao": "Salário", "valor_previsto": 4500, "conta_id": 1, "subcategoria_id": 70},
    llm={"extract_transaction_details": js(valor_decimal=None, descricao_bruta="salário")}, returns=tx_returns(income=True))
fin("renda_ignora_parcelamento", "012a", "recebi 500", intent="Renda", accounts=ACC,
    llm={"extract_transaction_details": js(valor_decimal=500, descricao_bruta="freela", parcelamento=3), "categorize_transaction": "71"},
    returns=tx_returns(income=True))

# ---------------------------------------------------------------- 012a: Transferência (sempre falha para quem tem conta)
fin("transferencia_com_contas", "012a", "transferir 100 do nubank para a carteira", intent="Transferência", accounts=ACC, deviation="151")
fin("transferencia_sem_contas_sem_valor", "012a", "transferir", intent="Transferência", accounts=[],
    llm={"extract_transfer_details": js(valor_decimal=None, conta_origem="Nubank", conta_destino="Carteira")})
fin("transferencia_sem_contas_sem_origem", "012a", "transferir 10", intent="Transferência", accounts=[],
    llm={"extract_transfer_details": js(valor_decimal=10, conta_origem=None, conta_destino="Carteira")})
fin("transferencia_sem_contas_sem_destino", "012a", "transferir 10", intent="Transferência", accounts=[],
    llm={"extract_transfer_details": js(valor_decimal=10, conta_origem="Nubank", conta_destino="")})
fin("transferencia_sem_contas_nao_encontradas", "012a", "transferir 10 do nubank para a carteira", intent="Transferência", accounts=[],
    llm={"extract_transfer_details": js(valor_decimal=10, conta_origem="Nubank", conta_destino="Carteira")},
    returns={"finance_service.get_account_by_name": None})

# ---------------------------------------------------------------- 012a: Pagamento Fatura (inline)
INCOMPLETE = "valor_decimal"
fin("fatura_dados_incompletos", "012a", "pagamento da fatura", intent="Pagamento Fatura", accounts=ACC,
    llm={"extract_fatura_payment_details": js(valor_decimal=500, conta_origem=None, conta_cartao="Cartão Inter")})
fin("fatura_valor_zero", "012a", "pagamento da fatura", intent="Pagamento Fatura", accounts=ACC,
    llm={"extract_fatura_payment_details": js(valor_decimal=0, conta_origem="Nubank", conta_cartao="Cartão Inter")})
fin("fatura_contas_nao_encontradas", "012a", "pagamento 500 da fatura do inter com o nubank", intent="Pagamento Fatura", accounts=ACC,
    llm={"extract_fatura_payment_details": js(valor_decimal=500, conta_origem="Nubank", conta_cartao="Inter")},
    returns={"finance_service.get_account_by_name": {"__seq__": [1, None]}})
fin("fatura_sucesso", "012a", "pagamento 500,5 da fatura do inter com o nubank", intent="Pagamento Fatura", accounts=ACC,
    llm={"extract_fatura_payment_details": js(valor_decimal=500.5, conta_origem="Nubank", conta_cartao="Cartão Inter")},
    returns={"finance_service.get_account_by_name": {"__seq__": [1, 2]}, "finance_service.create_fatura_payment": "Cartão Inter"})
fin("fatura_valor_texto", "012a", "pagamento 500 da fatura", intent="Pagamento Fatura", accounts=ACC,
    llm={"extract_fatura_payment_details": js(valor_decimal="500", conta_origem="Nubank", conta_cartao="Cartão Inter")},
    returns={"finance_service.get_account_by_name": {"__seq__": [1, 2]}, "finance_service.create_fatura_payment": "Cartão Inter"})
fin("fatura_cota_do_gemini", "012a", "pagamento da fatura", intent="Pagamento Fatura", accounts=ACC,
    llm={"extract_fatura_payment_details": {"__raise__": "429 Quota exceeded"}})

# ---------------------------------------------------------------- 012a: resposta a transacao pendente (passo 6)
PENDING = {
    "usuario_id": 7, "conta_id": 1, "conta_nome": "Nubank", "conta_tipo": "Conta Corrente", "categoria_id": 11, "fatura_id": None,
    "descricao": "padaria", "descricao_final": "Padaria do Zé", "valor_db": -50.0, "valor_original": 50.0, "valor_total": None, "num_parcelas": None,
    "tipo_transacao": "Despesa", "tipo_pagamento": "debito", "data_transacao": "2026-10-07", "origem": "whatsapp",
}
PENDING_INCOME = dict(PENDING, categoria_id=70, descricao="salário", descricao_final="Salário", valor_db=4500.0, valor_original=4500.0, tipo_transacao="Renda")
PENDING_NO_FINAL = dict(PENDING, descricao_final=None)
PENDING_NO_KEY = {k: v for k, v in PENDING.items() if k != "descricao_final"}
PENDING_INSTALL = dict(PENDING, valor_db=-30.0, valor_original=30.0, valor_total=90.0, num_parcelas=3, descricao_final=None, descricao="tenis")
PENDING_INSTALL_FINAL = dict(PENDING_INSTALL, descricao_final="Tênis")
PENDING_AWAIT = dict(PENDING, awaiting_category_selection=True, categoria_map={"1": 10, "2": 11, "3": 59})
LAST = "last_pending:" + NUMBER
KEY = "pending_tx:" + NUMBER + ":ab12cd34"
SAVE_RETURNS = {"finance_service.create_transaction": 321, "finance_service.create_parcelamento_agendamento": 55}


def reply(name, texto, pending, **kw):
    redis = {}
    if pending is not None:
        redis[KEY] = pending
        redis[LAST] = "ab12cd34"
    fin(name, "012a", texto, redis=redis, **kw)


reply("resposta_ok_grava_despesa", "OK", PENDING, returns=SAVE_RETURNS)
reply("resposta_ok_minusculo_com_espacos", "  ok  ", PENDING, returns=SAVE_RETURNS)
reply("resposta_confirmar_grava_renda", "confirmar", PENDING_INCOME, returns=SAVE_RETURNS)
reply("resposta_sim_nao_e_de_transacao", "sim", PENDING, intent="Menu de Ajuda")
reply("resposta_s_nao_e_de_transacao", "s", PENDING, intent="Menu de Ajuda")
reply("resposta_ok_sem_descricao_final_falha", "OK", PENDING_NO_FINAL, deviation="150", returns=SAVE_RETURNS)
reply("resposta_ok_sem_chave_descricao_final", "OK", PENDING_NO_KEY, returns=SAVE_RETURNS)
reply("resposta_ok_parcelada", "OK", PENDING_INSTALL_FINAL, returns=SAVE_RETURNS)
reply("resposta_ok_parcelada_sem_descricao_final", "OK", PENDING_INSTALL, deviation="150", returns=SAVE_RETURNS)
reply("resposta_trocar_despesa", "TROCAR", PENDING, returns={"finance_service.get_user_categories": CATS_D})
reply("resposta_trocar_renda", "trocar", PENDING_INCOME, returns={"finance_service.get_user_categories": CATS_R})
reply("resposta_trocar_com_chave_extra", "TROCAR", dict(PENDING, extra={"k": [1, "ç"]}), returns={"finance_service.get_user_categories": CATS_D})
reply("resposta_numero_escolhe_categoria", "2", PENDING_AWAIT, returns=SAVE_RETURNS)
reply("resposta_numero_com_espacos", " 3 ", PENDING_AWAIT, returns=SAVE_RETURNS)
reply("resposta_numero_fora_do_mapa", "9", PENDING_AWAIT)
reply("resposta_texto_na_escolha", "OK", PENDING_AWAIT)
reply("resposta_cancelar_na_escolha", "cancelar", PENDING_AWAIT)
reply("resposta_cancelar", "CANCELAR", PENDING)
reply("resposta_cancela_minusculo", "cancela", PENDING)
reply("resposta_nao_acentuado_e_do_evento", "não", PENDING, intent="Menu de Ajuda")
reply("resposta_comando_desconhecido", "confirmar agora", PENDING)
reply("resposta_pendencia_expirada", "ok", None)
fin("resposta_expirada_com_last_pending", "012a", "ok", redis={LAST: "ab12cd34"})
fin("resposta_ok_sem_pendencia", "012a", "ok")
fin("resposta_confirmar_sem_pendencia", "012a", "CONFIRMAR")
fin("resposta_cancelar_sem_pendencia_aviso", "012a", "cancelar")
fin("resposta_numero_sem_pendencia_vai_ao_gemini", "012a", "5", intent="Menu de Ajuda")
fin("resposta_trocar_sem_pendencia_vai_ao_gemini", "012a", "trocar", intent="Menu de Ajuda")
fin("resposta_frase_com_ok_sem_pendencia", "012a", "ok obrigado", intent="Menu de Ajuda")
