# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/redis-bot-fixtures.json (trilhas 010a e 010b da spec 010) com a referencia do Flask:
valores que redis_service.set_with_ttl grava (json.dumps, ensure_ascii ligado), textos crus de
set_if_not_exists e a sessao do bot (set_with_ttl(chave, session_id, 600)).

Uso: python generate_redis_bot_fixtures.py
Todos os valores sao FICTICIOS. A saida e versionada; o CI nao precisa de Python.
"""
import json
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "redis-bot-fixtures.json")

VALUES = [
    "abc",
    True,
    False,
    3,
    "3",
    None,
    {"a": 1},
    {"text": "ação", "x": [1, 2]},
    {"text": "ok 😀", "prompt_feedback": None, "cached_at": "2026-10-02T10:00:00"},
    "Saldo: R$ 1.234,56 \"quoted\" \\ barra\nlinha",
    '{"a":1}',  # codificacao dupla do filtro de seguranca: json.dumps(json.dumps(x))
    "2b1c9f7e-3a4d-4e55-9a7b-0c1d2e3f4a5b",  # bot_session_id:{nome}
]
redis_json = [{"value": v, "json": json.dumps(v)} for v in VALUES]

# Textos que nao sao JSON valido (valor cru de set_if_not_exists): get() devolve None sem lancar.
RAW = ["running", "processing", "", "not json {"]

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({"redis_json": redis_json, "raw_non_json": RAW}, fh, ensure_ascii=False, indent=2)
    fh.write("\n")
print("escrito", OUT)
