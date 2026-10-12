# -*- coding: utf-8 -*-
"""Roteiro de fumaça do .NET contra um homol LOCAL (banco vazio criado por `--migrate`).

Uso:  BASE_URL=http://127.0.0.1:5099 python smoke.py
Cria um usuário de teste pela própria API e exercita as rotas de ponta a ponta. Não toca em produção.
Saída: uma linha por verificação (OK/FALHA) e um resumo; código de saída 1 se houver falha.
Usa só a biblioteca padrão. Nenhum segredo é lido ou gravado.
"""
import json
import os
import sys
import urllib.error
import urllib.request
from datetime import date, timedelta

BASE = os.environ.get("BASE_URL", "http://127.0.0.1:5099").rstrip("/")
results = []
token = None


def call(method, path, body=None, auth=True, raw=False, headers=None):
    data = None if body is None else json.dumps(body).encode("utf-8")
    req = urllib.request.Request(BASE + path, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if auth and token:
        req.add_header("Authorization", f"Bearer {token}")
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            status, text = r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        status, text = e.code, e.read().decode("utf-8")
    if raw:
        return status, text
    try:
        return status, json.loads(text)
    except ValueError:
        return status, text


def check(name, cond, detail=""):
    results.append((name, bool(cond)))
    print(("OK     " if cond else "FALHA  ") + name + ("" if cond else f"  -> {detail}"))
    return cond


# --- 1. saúde e cadastro ------------------------------------------------------------------------------------------
s, b = call("GET", "/api/health", auth=False)
check("GET /api/health 200", s == 200 and b.get("status") == "success", (s, b))

whatsapp = "5511999990001"
s, b = call("POST", "/auth/register", {"nome": "Usuário Homol", "whatsapp": whatsapp, "password": "senha-homol-123", "dia_vencimento": 20, "dia_fechamento": 13}, auth=False)
check("POST /auth/register 201", s == 201, (s, b))
s, b = call("POST", "/auth/register", {"nome": "Usuário Homol", "whatsapp": whatsapp, "password": "senha-homol-123", "dia_vencimento": 20, "dia_fechamento": 13}, auth=False)
check("cadastro duplicado recusado", s in (400, 409), (s, b))
s, b = call("POST", "/auth/login", {"whatsapp": whatsapp, "password": "senha-homol-123"}, auth=False)
check("POST /auth/login 200 com token", s == 200 and isinstance(b, dict) and b.get("token"), (s, b))
token = b.get("token") if isinstance(b, dict) else None
s, b = call("POST", "/auth/login", {"whatsapp": whatsapp, "password": "errada"}, auth=False)
check("login com senha errada 401", s == 401, (s, b))
s, b = call("GET", "/api/accounts", auth=False)
check("rota protegida sem token 401", s == 401, (s, b))

# --- 2. contas -----------------------------------------------------------------------------------------------------
s, b = call("GET", "/api/accounts")
check("GET /api/accounts lista as 3 contas padrão", s == 200 and len(b["data"]) == 3, (s, b))
s, b = call("POST", "/api/accounts", {"nome_conta": "Cartão Inter", "tipo_conta": "Cartão de Crédito", "saldo_inicial": 0,
                                       "limite_credito": 5000, "dia_fechamento": 13, "dia_vencimento": 20})
check("POST /api/accounts cartão 201", s == 201, (s, b))
s, b = call("GET", "/api/accounts")
accounts = {a["nome_conta"]: a["id"] for a in b["data"]}
card = accounts.get("Cartão Inter")
wallet = next((i for n, i in accounts.items() if n != "Cartão Inter"), None)
check("cartão aparece na lista", card is not None, accounts)
s, b = call("POST", "/api/accounts", {"nome_conta": "Cartão Inter", "tipo_conta": "Cartão de Crédito"})
check("nome de conta duplicado 400/409 (desvio 016)", s in (400, 409), (s, b))
s, b = call("POST", "/api/accounts", {"nome_conta": "X", "tipo_conta": "Inexistente"})
check("tipo de conta inválido 400", s == 400, (s, b))

# --- 3. categorias -------------------------------------------------------------------------------------------------
s, b = call("GET", "/api/categories")
check("GET /api/categories 200 com dados", s == 200 and b.get("data"), (s, str(b)[:200]))
subs = []
for g in (b.get("data") or []):
    for sc in g.get("subcategorias", []):
        subs.append((g.get("grupo"), g.get("macro_categoria"), sc.get("id"), sc.get("nome")))
sub_id = subs[0][2] if subs else None
check("achou subcategorias para lançar", sub_id is not None, str(b)[:300])

# --- 4. transações (desvios 065 a 071) -----------------------------------------------------------------------------
today = date.today()
s, b = call("POST", "/api/transactions", {"descricao": "Mercado", "valor": 150.5, "tipo": "Despesa", "conta_id": wallet,
                                           "subcategoria_id": sub_id, "data": today.isoformat()})
check("POST /api/transactions despesa em conta 201", s == 201, (s, b))
tx = b.get("data", {}).get("id") if isinstance(b, dict) else None
s, b = call("POST", "/api/transactions", {"descricao": "Notebook", "valor": 300, "tipo": "Despesa", "conta_id": card,
                                           "subcategoria_id": sub_id, "data": today.isoformat()})
check("POST /api/transactions despesa no cartão 201", s == 201, (s, b))
s, b = call("POST", "/api/transactions", {"descricao": "Salário", "valor": 5000, "tipo": "Receita", "conta_id": wallet,
                                           "subcategoria_id": sub_id, "data": today.isoformat()})
check("POST /api/transactions receita 201", s == 201, (s, b))
s, b = call("POST", "/api/transactions", {"descricao": "", "valor": "x", "tipo": "Despesa", "conta_id": wallet})
check("entrada inválida 400 (desvio 066)", s == 400, (s, b))
s, b = call("GET", "/api/transactions?limit=10")
check("GET /api/transactions lista 3", s == 200 and b["data"]["total"] == 3, (s, str(b)[:200]))
row = next((t for t in b["data"]["transactions"] if t["descricao"] == "Notebook"), {}) if s == 200 else {}
check("GET devolve conta_id e subcategoria_id (desvio 067)", row.get("conta_id") == card and "subcategoria_id" in row, row)
if tx:
    s, b = call("PUT", f"/api/transactions/{tx}", {"descricao": "Mercado atualizado", "valor": 99.9})
    check("PUT /api/transactions/{id} 200", s == 200, (s, b))
    s, b = call("DELETE", f"/api/transactions/{tx}")
    check("DELETE /api/transactions/{id} 200", s == 200, (s, b))
s, b = call("GET", "/api/transactions/recent")
check("GET /api/transactions/recent 200", s == 200, (s, str(b)[:200]))

# --- 4b. ciclo da fatura do cartão (desvio 065), conferido direto no banco de homol -----------------------------------
import subprocess


def sql(query):
    c = os.environ.get("HOMOL_PG_CONTAINER")
    if not c:
        return None
    r = subprocess.run(["docker", "exec", c, "psql", "-U", os.environ.get("HOMOL_PG_USER", "homol"),
                        "-d", "bot_whatsapp_homol", "-tAF|", "-c", query], capture_output=True, text=True)
    return r.stdout.strip()


before = date(today.year, today.month, 10)
after = date(today.year, today.month, 15)
for label, d in (("antes do fechamento (dia 10)", before), ("depois do fechamento (dia 15)", after)):
    s, b = call("POST", "/api/transactions", {"descricao": f"Compra {label}", "valor": 10, "tipo": "Despesa", "conta_id": card,
                                               "subcategoria_id": sub_id, "data": d.isoformat()})
    check(f"compra no cartão {label} 201", s == 201, (s, b))
rows = sql(f"select to_char(f.data_fechamento,'YYYY-MM-DD'), to_char(f.data_vencimento,'YYYY-MM-DD') "
           f"from transacoes t join faturas f on f.id=t.fatura_id where t.conta_id={card} and t.descricao like 'Compra %' order by t.data_transacao")
if rows is not None:
    lines = rows.splitlines()
    exp_b = (date(before.year, before.month, 13), date(before.year, before.month, 20))
    nm = date(after.year + (after.month // 12), after.month % 12 + 1, 1)
    exp_a = (date(nm.year, nm.month, 13), date(nm.year, nm.month, 20))
    check("compra antes do fechamento: fatura fecha dia 13 e vence dia 20 do mesmo mês",
          lines and lines[0] == f"{exp_b[0]}|{exp_b[1]}", lines)
    check("compra depois do fechamento: fatura do mês seguinte (13/20)",
          len(lines) > 1 and lines[1] == f"{exp_a[0]}|{exp_a[1]}", lines)

# --- 5. contas mensais (005) ---------------------------------------------------------------------------------------
s, b = call("POST", "/api/bills", {"descricao": "Aluguel", "tipo_agendamento": "FIXO", "periodicidade": "MENSAL",
                                    "subcategoria_id": sub_id, "conta_id": wallet, "dia_execucao": 5, "valor_previsto": 1500,
                                    "incluir_na_reserva": True})
check("POST /api/bills FIXO 201", s == 201, (s, b))
s, b = call("POST", "/api/bills", {"descricao": "Luz", "tipo_agendamento": "LEMBRETE_VARIAVEL", "periodicidade": "MENSAL",
                                    "subcategoria_id": sub_id, "conta_id": card, "dia_execucao": 10})
check("POST /api/bills LEMBRETE_VARIAVEL 201", s == 201, (s, b))
s, b = call("POST", "/api/contas-mensais", {"descricao": "Notebook 12x", "tipo_agendamento": "PARCELADO", "periodicidade": "MENSAL",
                                             "subcategoria_id": sub_id, "conta_id": card, "dia_execucao": 15,
                                             "valor_previsto": 300, "total_parcelas": 12})
check("POST /api/contas-mensais PARCELADO 201 (alias)", s == 201, (s, b))
s, b = call("GET", "/api/bills")
check("GET /api/bills lista 3", s == 200 and len(b["data"]) == 3, (s, str(b)[:200]))
s, b = call("GET", "/api/bills/summary")
check("GET /api/bills/summary 200", s == 200, (s, str(b)[:300]))

# --- 6. dashboard (006) --------------------------------------------------------------------------------------------
for path in ("/api/dashboard/summary", "/api/dashboard/charts?meses=3", "/api/dashboard/recent", "/api/dashboard/alerts",
             "/api/dashboard/stats", "/api/transacoes/recentes"):
    s, b = call("GET", path)
    check(f"GET {path} 200", s == 200, (s, str(b)[:200]))

# --- 7. configurações (007) ----------------------------------------------------------------------------------------
for path in ("/api/user/profile", "/api/user/settings", "/api/user/notifications", "/api/user/addresses",
             "/api/user/api-keys"):
    s, b = call("GET", path)
    check(f"GET {path} 200", s == 200, (s, str(b)[:200]))
s, b = call("PUT", "/api/user/notifications", {"morningBriefing": {"enabled": True, "time": "07:00"}, "eveningCheckIn": {"enabled": True, "time": "19:00"}, "financialAlerts": {"enabled": True, "daysBeforeDue": 2}})
check("PUT /api/user/notifications daysBeforeDue (desvio 250)", s == 200, (s, b))
s, b = call("GET", "/api/user/notifications")
check("daysBeforeDue lido de volta = 2", s == 200 and b["data"]["financialAlerts"]["daysBeforeDue"] == 2, (s, b))

# --- 8. alertas de calendário (014) e rotas que mudam por desvio ----------------------------------------------------
uid = None
s, b = call("GET", "/api/user/profile")
if s == 200:
    uid = (b.get("data") or b).get("id") if isinstance(b, dict) else None
if uid:
    s, b = call("GET", f"/calendar-alerts/config/{uid}")
    check("GET /calendar-alerts/config/{id} 200", s == 200, (s, b))
    s, b = call("GET", f"/calendar-alerts/config/{uid + 99}")
    check("config de outro usuário 403 (desvio 003)", s == 403, (s, b))

# --- 9. segurança (009-0) ------------------------------------------------------------------------------------------
s, b = call("GET", "/rota-que-nao-existe", auth=False, raw=True)
check("rota desconhecida 404", s == 404, (s, str(b)[:80]))
s, b = call("GET", "/admin/security-stats", auth=False)
check("/admin sem x-api-key 401", s == 401, (s, b))

# --- 10. rotas /admin com x-api-key (009a/b/c); a chave vem de ADMIN_API_KEY (o mesmo valor de API_SECRET_KEY) ---------
admin_key = os.environ.get("ADMIN_API_KEY")
if admin_key:
    h = {"x-api-key": admin_key}
    for path in ("/admin/security-stats", "/admin/gemini-cache-stats", "/admin/oauth-config-check",
                 "/admin/setup-database"):
        s, b = call("GET", path, auth=False, headers=h, raw=True)
        check(f"GET {path} com chave 200", s == 200, (s, str(b)[:120]))
    s, b = call("POST", "/admin/gemini-cache-clear", {}, auth=False, headers=h)
    check("POST /admin/gemini-cache-clear corpo vazio 200 (desvio 077)", s == 200, (s, b))
    s, b = call("POST", "/admin/security-blacklist-add", {"ip": "999.1.1.1"}, auth=False, headers=h)
    check("blacklist com IP inválido 400 (desvio 078)", s == 400, (s, b))
    s, b = call("GET", "/admin/get-notification-config/999999", auth=False, headers=h)
    check("usuário inexistente 404 (desvio 080)", s == 404, (s, b))
    s, b = call("POST", "/admin/run-motor-agendamentos", {}, auth=False, headers=h)
    check("POST /admin/run-motor-agendamentos 200", s == 200, (s, b))
else:
    print("(pulado: defina ADMIN_API_KEY para testar as rotas /admin)")

fails = [n for n, ok in results if not ok]
print(f"\n{len(results) - len(fails)}/{len(results)} verificações OK")
if fails:
    print("Falhas:", *fails, sep="\n  - ")
sys.exit(1 if fails else 0)
