# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/gemini-fixtures.json (trilha 010c da spec 010) com a referencia do Flask:
chaves de cache do Gemini (gemini_cache_service._generate_cache_key, reproduzida aqui sem importar o pacote app)
e a entrada que redis_service.set_with_ttl grava (json.dumps do dict {text, prompt_feedback, cached_at}).

Uso: python generate_gemini_fixtures.py
Todos os valores sao FICTICIOS. A saida e versionada; o CI nao precisa de Python.
"""
import hashlib
import json
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "gemini-fixtures.json")

PROMPTS = [
    "abc",
    "",
    "Qual a intencao? ação 😀\nsegunda linha \"aspas\"",
    "Minhas subcategorias são: [{\"id\": 1, \"nome\": \"Mercado\"}]",
]


def cache_key(prompt, key_prefix, usuario_id=None):
    prompt_hash = hashlib.md5(prompt.encode("utf-8")).hexdigest()[:12]
    user_part = str(usuario_id) if usuario_id else "global"
    return f"gemini_cache:{key_prefix}:{user_part}:{prompt_hash}"


keys = []
for p in PROMPTS:
    keys.append({"prompt": p, "prefix": "intent", "user_id": None, "key": cache_key(p, "intent")})
    keys.append({"prompt": p, "prefix": "category", "user_id": 7, "key": cache_key(p, "category", 7)})

entry = {"text": "Mercado ç", "prompt_feedback": "", "cached_at": "2026-10-02T10:00:00.123456"}
entries = [{"value": entry, "json": json.dumps(entry)}]

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({"cache_keys": keys, "cache_entries": entries}, fh, ensure_ascii=False, indent=2)
    fh.write("\n")
print("escrito", OUT)
