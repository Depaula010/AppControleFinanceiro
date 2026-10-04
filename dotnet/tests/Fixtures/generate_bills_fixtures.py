# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/bills-fixtures.json com a implementação de REFERÊNCIA do Flask (spec 005, plan §6).

Duas seções:
  (a) round(x, 2) do Python para ~300 valores (PyRound.Round2 precisa dar o mesmo; Math.Round(double, 2) não garante);
  (b) get_reserva_status (app/services/finance/emergency_reserve_service.py) com conexão SIMULADA, para combinações de
      periodicidade e meses (1 a 36, inclui 50 => round(216.5) = 216). O módulo é carregado por texto, sem importar o
      pacote app (que puxa dependências de produção): só a linha `from ._database import ...` é trocada por stubs.

Uso: python generate_bills_fixtures.py
Os números são FICTÍCIOS. A saída é versionada; o CI não precisa de Python.
"""
import json
import os
import random

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "bills-fixtures.json")
SERVICE = os.path.join(HERE, "..", "..", "..", "app", "services", "finance", "emergency_reserve_service.py")


# (a) round(x, 2) -------------------------------------------------------------------------------
fixed = [0.125, 2.675, 1.115, 0.5, 0.005, 0.015, 0.025, 1.005, 10.005, 100.125, 0.375, 0.625, 1234.565,
         -0.125, -2.675, -1.115, -0.5, -0.005, -1.005, 0.0, -0.0, 1e-9, 0.994999, 0.995, 99.995, 2.5, 3.5,
         1e15 + 0.125, 123456789012.345, 4503599627370496.5, 311.3, 3020.0, 503.3333333333333, 1445.9999999999998]
rng = random.Random(20261004)
sampled = []
for _ in range(120):
    sampled.append(round(rng.uniform(-5000, 50000), rng.choice([2, 3, 4, 6])))
for _ in range(60):
    sampled.append(rng.randint(0, 2_000_000) / 1000.0)      # terminam em .xxx, muitos empates visuais
for _ in range(60):
    sampled.append(rng.randint(0, 400_000) * 4.33)           # valores de semanas
for _ in range(40):
    sampled.append(rng.randint(0, 100_000) * 2.16)
values = fixed + sampled
round_cases = [{"value": repr(v), "rounded": repr(round(v, 2))} for v in values]


# (b) get_reserva_status com conexão simulada ---------------------------------------------------
with open(SERVICE, encoding="utf-8") as fh:
    source = fh.read()
source = source.replace("from ._database import text, Connection", "text = lambda s: s\nConnection = object")
namespace = {}
exec(compile(source, "emergency_reserve_service.py", "exec"), namespace)
get_reserva_status = namespace["get_reserva_status"]


class Row:
    def __init__(self, **kw):
        self.__dict__.update(kw)


class FakeResult:
    def __init__(self, rows):
        self.rows = rows

    def fetchone(self):
        return self.rows[0] if self.rows else None

    def fetchall(self):
        return self.rows


class FakeConn:
    def __init__(self, meses, totals):
        self.meses, self.totals = meses, totals

    def execute(self, sql, params):
        if "meses_reserva_emergencia" in sql:
            return FakeResult([] if self.meses is None else [Row(meses=self.meses)])
        return FakeResult([Row(periodicidade=p, total_periodo=t) for (p, t) in self.totals])


PERIODS = ["ANUAL", "DIARIA", "MENSAL", "QUINZENAL", "SEMANAL"]
reserve_cases = []
for meses in list(range(1, 37)) + [50, 0]:
    for _ in range(3):
        picked = rng.sample(PERIODS, rng.randint(1, 5))
        totals = [(p, round(rng.uniform(1, 5000), 2)) for p in sorted(picked)]
        monthly, total, months = get_reserva_status(FakeConn(meses, totals), 1)
        reserve_cases.append({
            "months": meses,
            "totals": [{"periodicity": p, "total": repr(t)} for (p, t) in totals],
            "monthly_equivalent": repr(monthly),
            "total": repr(total),
            "months_result": months,
        })
# usuário sem linha: 6 meses; nada marcado: tudo zero
monthly, total, months = get_reserva_status(FakeConn(None, []), 1)
reserve_cases.append({"months": None, "totals": [], "monthly_equivalent": repr(monthly), "total": repr(total), "months_result": months})

# fator por periodicidade para N de 1 a 36 (round do Python em SEMANAL)
factors = [{"months": n, "weekly": round(n * 4.33), "biweekly": n * 2, "daily": n * 30, "monthly": n} for n in range(1, 37)]
factors.append({"months": 50, "weekly": round(50 * 4.33), "biweekly": 100, "daily": 1500, "monthly": 50})

with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({"round2": round_cases, "reserve": reserve_cases, "factors": factors}, fh, ensure_ascii=False, indent=1)
    fh.write("\n")
print("escrito", OUT, len(round_cases), "arredondamentos,", len(reserve_cases), "reservas")
