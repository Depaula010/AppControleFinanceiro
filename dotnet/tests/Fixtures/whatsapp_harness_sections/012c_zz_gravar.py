# -*- coding: utf-8 -*-
"""Grava os cenarios das sub-entregas 012a, 012b e 012c (ultimo arquivo desta familia de secoes)."""
FIN_OUT = os.path.join(HERE, "whatsapp-financial-scenarios.json")
with open(FIN_OUT, "w", encoding="utf-8", newline="\n") as _fh:
    json.dump({"api_key": API_KEY, "number": NUMBER, "today": FIXED_TODAY.isoformat(), "scenarios": FIN_SCENARIOS}, _fh, ensure_ascii=False, indent=1)
_REAL_STDOUT.write("ok %s %d\n" % (FIN_OUT, len(FIN_SCENARIOS)))
