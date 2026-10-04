# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/weather-routes-fixtures.json com a implementacao de REFERENCIA do Flask (spec 010 §8, trilha 010e).

Carrega app/services/weather_service.py por caminho de arquivo (importlib, sem importar o pacote app) e instancia com
__new__ para nao depender de chaves. Os calculos de rota (int(), round()) sao os mesmos operadores de
travel_time_service.py:166-167.

Uso: python generate_weather_routes_fixtures.py
Tudo FICTICIO. A saida e versionada; o CI nao precisa de Python.
"""
import importlib.util
import json
import os
import random

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "weather-routes-fixtures.json")
SERVICE = os.path.normpath(os.path.join(HERE, "..", "..", "..", "app", "services", "weather_service.py"))

spec = importlib.util.spec_from_file_location("weather_service_ref", SERVICE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
service = module.WeatherService.__new__(module.WeatherService)

translations = [
    {"english": en, "portuguese": pt, "emoji": emoji}
    for en, (pt, emoji) in module.WeatherService.WEATHER_TRANSLATIONS.items()
]

BASE = {"temperatura": 24, "condicao": "Ensolarado", "emoji": "☀️", "sensacao_termica": 24, "umidade": 65,
        "chance_chuva": None, "descricao_completa": "24°C, Ensolarado ☀️"}


def case(**changes):
    data = dict(BASE, **changes)
    return {"input": data, "output": service.format_weather_for_briefing(data)}


formats = [
    case(),
    case(sensacao_termica=26),
    case(sensacao_termica=27),
    case(sensacao_termica=21),
    case(sensacao_termica=20),
    case(chance_chuva=0),
    case(chance_chuva=29),
    case(chance_chuva=30),
    case(chance_chuva=59),
    case(chance_chuva=60),
    case(chance_chuva=100),
    case(sensacao_termica=30, chance_chuva=60),
    case(temperatura=-2, sensacao_termica=-6, descricao_completa="-2°C, Neblina 🌫️", chance_chuva=45),
]
formats.append({"input": None, "output": service.format_weather_for_briefing(None)})
formats.append({"input": {}, "output": service.format_weather_for_briefing({})})

truncation = [{"value": v, "int": int(v)} for v in [24.9, -0.5, 0, 0.99, -3.7, 30.0, -0.0, 12.5, 100.4]]

rng = random.Random(10)
distances = [0, 1, 49, 50, 51, 12549, 12450, 12550, 12350, 12650, 100, 150, 250, 350, 450, 550, 650, 2759, 999999]
distances += [rng.randint(0, 900000) for _ in range(300)]
distances += [rng.randint(0, 900000) + rng.random() for _ in range(100)]
rounding = [{"meters": m, "km": round(m / 1000, 1)} for m in distances]

durations = [0, 1, 59, 60, 61, 2759, 2760, 3599.9]
durations += [rng.randint(0, 40000) + rng.random() for _ in range(100)]
duration_cases = [{"seconds": s, "minutes": int(s / 60)} for s in durations]

with open(OUT, "w", encoding="utf-8", newline="\n") as handle:
    json.dump({"translations": translations, "formats": formats, "truncation": truncation,
               "distance_rounding": rounding, "duration_truncation": duration_cases},
              handle, ensure_ascii=False, indent=1)
    handle.write("\n")
print("ok", len(translations), len(formats), len(rounding))
