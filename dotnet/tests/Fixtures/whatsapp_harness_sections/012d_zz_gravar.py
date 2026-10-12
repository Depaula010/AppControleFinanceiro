# -*- coding: utf-8 -*-
"""Grava os cenarios da sub-entrega 012d e restaura o estado que 012d_contas_fixas.py trocou (para as secoes seguintes)."""
BILLS_OUT = os.path.join(HERE, "whatsapp-fixedbills-scenarios.json")
with open(BILLS_OUT, "w", encoding="utf-8", newline="\n") as _fh:
    json.dump({"api_key": API_KEY, "number": NUMBER, "today": FIXED_TODAY.isoformat(), "scenarios": BILLS_SCENARIOS}, _fh, ensure_ascii=False, indent=1)
_REAL_STDOUT.write("ok %s %d\n" % (BILLS_OUT, len(BILLS_SCENARIOS)))

_proxy.date = _D_SAVED["date"]
handler_ns["date"] = _D_SAVED["handler_date"]
handler_ns["db_engine"] = _D_SAVED["handler_db"]
handler_ns["NightlyCheckinService"] = _D_SAVED["handler_nightly"]
sys.modules["app"].db_engine = _D_SAVED["app_db"]
sys.modules["app.services.fixed_bills_service"] = _D_SAVED["mod_fixed"]
sys.modules["app.services.nightly_checkin_service"] = _D_SAVED["mod_nightly"]
FinanceFacade.__getattr__ = _D_SAVED["facade_getattr"]
sys.modules["app.routes.webhooks.intents"].INTENT_REGISTRY["Contas Atrasadas"] = _D_SAVED["registry_atrasadas"]
REAL_INTENTS.clear()
REAL_INTENTS.update(_D_SAVED["real_intents"])
