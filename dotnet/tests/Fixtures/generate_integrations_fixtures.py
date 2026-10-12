# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/integrations-fixtures.json com a implementação de REFERÊNCIA do Flask (spec 010 §7 do plan).

Esqueleto da 010-0: só as seções que não dependem de importar o app (a) valores de json.dumps do Redis e
(b) hash12 do Gemini e MD5 do calendar_alert. As trilhas acrescentam as próprias seções neste mesmo script:
  (c) CalendarFormatter (google_calendar_service.py, carregado por caminho de arquivo com
      importlib.util.spec_from_file_location, SEM importar o pacote app);
  (d) WeatherService.format_weather_for_briefing e a tabela de tradução (idem, instanciando com __new__);
  (e) as 4 páginas HTML e a mensagem de WhatsApp do calendar_handler.py (via ast).

Uso: python generate_integrations_fixtures.py
Chaves, números e textos são FICTÍCIOS. A saída é versionada; o CI não precisa de Python.
"""
import hashlib
import json
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "integrations-fixtures.json")

# (a) o que set_with_ttl grava no Redis (json.dumps, ensure_ascii ligado) -----------------------
REDIS_VALUES = [
    "abc",
    True,
    3,
    "3",
    {"text": "ação", "x": [1, 2]},
    {"text": "ok 😀", "prompt_feedback": None, "cached_at": "2026-10-02T10:00:00"},
    '{"a":1}',  # codificação dupla do filtro de segurança (json.dumps(json.dumps(x)))
]
redis_json = [{"value": v, "json": json.dumps(v)} for v in REDIS_VALUES]

# (b) chaves de cache --------------------------------------------------------------------------
HASH_PROMPTS = ["abc", "Qual é o saldo? 😀", ""]
hash12 = [{"prompt": p, "hash12": hashlib.md5(p.encode("utf-8")).hexdigest()[:12]} for p in HASH_PROMPTS]

CALENDAR_ALERTS = [(7, "abc", "2026-10-02T10:00:00-03:00"), (12, "evt_ç", "2026-10-02")]
calendar_alert = [
    {"user_id": u, "event_id": e, "start": s, "key": "calendar_alert:" + hashlib.md5(f"{u}:{e}:{s}".encode("utf-8")).hexdigest()}
    for (u, e, s) in CALENDAR_ALERTS
]

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({"redis_json": redis_json, "hash12": hash12, "calendar_alert": calendar_alert}, fh, ensure_ascii=False, indent=2)
    fh.write("\n")
print("escrito", OUT)
