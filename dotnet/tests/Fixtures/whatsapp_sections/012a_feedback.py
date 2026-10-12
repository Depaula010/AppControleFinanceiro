# -*- coding: utf-8 -*-
"""Secao do gerador de fixtures (escopo de generate_whatsapp_fixtures.py): formatadores da 012a. Saida propria: whatsapp-financial-fixtures.json."""
FIN = globals().setdefault("FIN", {})

_fb = load("transaction_feedback_service.py", source("services", "transaction_feedback_service.py"))

_pots = [None]
for pct, rest, per in [(0.0, 500.0, "Semanal"), (69.99, 150.0, "Mensal"), (70.0, 120.0, "Quinzenal"), (89.99, 10.01, "Anual"), (90.0, 0.125, "Mensal"), (150.0, -250.5, "Semanal")]:
    _pots.append({"nome_pote": "Alimentação", "valor_limite": 500.0, "gasto_periodo": 0.0, "saldo_restante": rest, "percentual_usado": pct, "periodicidade": per})
_foots = {
    "credito": [None, {"nome_conta": "Cartão Inter", "dia_fechamento": 15, "valor_total": 1250.456, "dias_ate_fechar": 3},
                {"nome_conta": "Cartão", "dia_fechamento": None, "valor_total": 0.0, "dias_ate_fechar": 0}],
    "corrente": [None, {"nome_conta": "Nubank", "saldo_disponivel": 890.505}, {"nome_conta": "Nubank", "saldo_disponivel": -12.345}],
}
_txs = [
    {"descricao": "Padaria", "valor": -50.0, "nome_conta": "Nubank"},
    {"descricao": "Salário", "valor": 4500.125, "nome_conta": "Nubank"},
    {"descricao": None, "valor": -0.005, "nome_conta": "Cartão Inter"},
    {"descricao": "Tênis", "valor": -1234567.891, "nome_conta": "Carteira"},
]
_fb_cases = []
for tx in _txs:
    for pot in _pots:
        for kind, foots in _foots.items():
            for foot in foots:
                for show in (True, False):
                    _fb_cases.append({
                        "tx": tx, "pot": pot, "footer": foot, "kind": kind, "show": show,
                        "text": _fb["formatar_mensagem_feedback"](tx, pot, foot, kind, show),
                    })
FIN["feedback_cases"] = _fb_cases
FIN["emoji_cases"] = [{"pct": p, "emoji": _fb["get_emoji_status"](p)} for p in [0, 69.99, 70, 89.999, 90, 100, 250]]


class _AlertConn:
    def __init__(self, row):
        self.row = row

    def execute(self, sql, params=None):
        class R:
            def __init__(s, row):
                s.row = row

            def fetchone(s):
                return s.row
        return R(self.row)


_alert_cases = []
for row in [None, (False, 0), (True, 0), (True, 70), (True, 90), (False, 90)]:
    for pct in (0.0, 69.0, 70.0, 95.5):
        _alert_cases.append({"row": row, "pct": pct, "show": _fb["deve_exibir_alerta"](_AlertConn(row), 1, pct)})
FIN["alert_cases"] = _alert_cases
FIN["mention_names"] = [{"message": c["message"], "name": c["query"]} for c in out["mention_cases"]]

with open(os.path.join(HERE, "whatsapp-financial-fixtures.json"), "w", encoding="utf-8", newline="\n") as _fh:
    json.dump(FIN, _fh, ensure_ascii=False, indent=1)
