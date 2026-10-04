# -*- coding: utf-8 -*-
"""
Gera tests/Fixtures/google-fixtures.json e src/MeuSecretario.Api/Google/GooglePages.cs a partir do codigo REFERENCIA
do Flask (spec 010d). Nao importa o pacote `app` (exigiria variaveis de ambiente): le o fonte com `ast` e carrega
google_calendar_service.py por caminho de arquivo.

Secoes:
  (a) textos de WhatsApp: token revogado (com e sem link) e "conectado" (B.5), de google_calendar_oauth_service.py e
      calendar_handler.py, renderizados com marcadores {connect_url} e {nome};
  (b) as 4 paginas HTML do calendar_handler.py (B.1 a B.4), byte a byte, em GooglePages.cs;
  (c) CalendarFormatter (google_calendar_service.py): strip_html, format_time, dia e periodo.

Uso: python generate_google_fixtures.py
Chaves, numeros e textos sao FICTICIOS. A saida e versionada; o CI nao precisa de Python.
"""
import ast
import importlib.util
import json
import os
import types
from datetime import date, datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT_JSON = os.path.join(HERE, "google-fixtures.json")
OUT_PAGES = os.path.join(ROOT, "dotnet", "src", "MeuSecretario.Api", "Google", "GooglePages.cs")
HANDLER = os.path.join(ROOT, "app", "services", "..", "routes", "webhooks", "handlers", "calendar_handler.py")
OAUTH_SERVICE = os.path.join(ROOT, "app", "services", "google_calendar_oauth_service.py")
CALENDAR_SERVICE = os.path.join(ROOT, "app", "services", "google_calendar_service.py")


def parse(path):
    with open(path, encoding="utf-8") as fh:
        return ast.parse(fh.read())


def find_function(tree, name):
    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name == name:
            return node
    raise KeyError(name)


def render(node, names):
    """Avalia um literal de string ou f-string com os nomes dados."""
    expression = ast.Expression(body=node)
    ast.fix_missing_locations(expression)
    return eval(compile(expression, "<fstring>", "eval"), {"str": str}, names)  # noqa: S307 (fonte confiavel do repositorio)


def string_nodes_returned(function):
    """Strings (ou f-strings) devolvidas por `return <str>, <status>` na ordem do fonte."""
    found = []
    for node in ast.walk(function):
        if isinstance(node, ast.Return) and isinstance(node.value, ast.Tuple) and node.value.elts:
            first = node.value.elts[0]
            if isinstance(first, (ast.Constant, ast.JoinedStr)) and (not isinstance(first, ast.Constant) or isinstance(first.value, str)):
                found.append((first, node.value.elts[1].value if len(node.value.elts) > 1 else None))
    return sorted(found, key=lambda pair: pair[0].lineno)


def assigned(function, target):
    return sorted(
        (n for n in ast.walk(function)
         if isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and t.id == target for t in n.targets)),
        key=lambda n: n.lineno,
    )


# (a) e (b) ---------------------------------------------------------------------------------------
handler_tree = parse(HANDLER)
oauth_tree = parse(OAUTH_SERVICE)

callback = find_function(handler_tree, "handle_oauth2callback")
connect = find_function(handler_tree, "handle_connect")

connect_pages = [render(node, {}) for node, status in string_nodes_returned(connect) if isinstance(node, ast.Constant) and "Ja Conectado" in node.value]
assert len(connect_pages) == 1, "pagina B.1 nao encontrada"

callback_strings = string_nodes_returned(callback)
page_denied = next(render(n, {}) for n, s in callback_strings if isinstance(n, ast.Constant) and "Autorizacao Negada" in n.value)
page_connected = next(render(n, {"nome_usuario": "{nome}"}) for n, s in callback_strings if s == 200 and isinstance(n, ast.JoinedStr))
page_error = next(render(n, {"e": Exception("{texto}")}) for n, s in callback_strings if s == 500)
assert "Conexao Autorizada" in page_connected and "<title>Erro</title>" in page_error

whatsapp_connected = None
for assign in assigned(callback, "mensagem"):
    whatsapp_connected = render(assign.value, {"usuario": types.SimpleNamespace(nome="{nome}")})

notify = find_function(oauth_tree, "_notify_token_revoked")
revoked = [render(a.value, {"connect_url": "{connect_url}"}) for a in assigned(notify, "mensagem")]
assert len(revoked) == 2, "esperava as duas variantes de token revogado"

# (c) CalendarFormatter ---------------------------------------------------------------------------
spec = importlib.util.spec_from_file_location("google_calendar_service_ref", CALENDAR_SERVICE)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
Formatter = module.GoogleCalendarService

STRIP_HTML = ["", None, "<p>Ola&nbsp;<b>mundo</b> &amp; cia &lt;3 &gt;</p>", "a \t\t b\n\n\n\nc", "  <br>  x  ", "sem tags"]
FORMAT_TIME = [
    "2026-10-02T10:30:00-03:00", "2026-10-02T10:30:00Z", "2026-10-02T10:30:00+00:00", "2026-10-02T09:05:00.000-03:00",
    "2026-10-02", "", None, "T", "2026-10-02T7:5",
]


def safe(call, argument):
    try:
        return call(argument)
    except Exception as error:  # a referencia nunca deveria lancar; se lancar, registre
        return "EXC:" + type(error).__name__


# "hoje" fixo: a referencia usa date.today(); trocamos `date` do modulo por uma subclasse com today() = 2026-10-02.
TODAY = date(2026, 10, 2)


class FixedDate(date):
    @classmethod
    def today(cls):
        return cls(TODAY.year, TODAY.month, TODAY.day)


module.date = FixedDate


def event(summary, start, end, location="", description="", all_day=False):
    return {"summary": summary, "start": start, "end": end, "location": location, "description": description, "all_day": all_day}


TIMED = event("Reuniao", "2026-10-02T10:00:00-03:00", "2026-10-02T11:30:00-03:00", "Sala 3", "<p>Pauta &amp; ata</p><br>Levar <b>notebook</b>")
ALL_DAY = event("Feriado", "2026-10-02", "2026-10-03", all_day=True)
START_ONLY = event("Ligacao", "2026-10-02T15:00:00Z", "")
PLAIN = event("Dentista", "2026-10-02T08:30:00+00:00", "2026-10-02T09:00:00+00:00", "", "<p></p>")

DAY_CASES = [
    {"name": "vazio hoje", "date": "2026-10-02", "events": []},
    {"name": "vazio amanha", "date": "2026-10-03", "events": []},
    {"name": "vazio outra data", "date": "2026-11-15", "events": []},
    {"name": "um evento com horario, local e descricao html", "date": "2026-10-02", "events": [TIMED]},
    {"name": "dia inteiro e sem fim", "date": "2026-10-04", "events": [ALL_DAY, START_ONLY]},
    {"name": "varios (sabado, domingo, terca)", "date": "2026-10-06", "events": [PLAIN, TIMED, ALL_DAY]},
    {"name": "quarta, quinta, sexta", "date": "2026-10-07", "events": [PLAIN]},
]
for case in DAY_CASES:
    d = date.fromisoformat(case["date"])
    case["output"] = Formatter.format_events_for_whatsapp(case["events"], d)
for extra in ("2026-10-08", "2026-10-09", "2026-10-10", "2026-10-11", "2026-10-12"):
    d = date.fromisoformat(extra)
    DAY_CASES.append({"name": "cabecalho " + extra, "date": extra, "events": [PLAIN], "output": Formatter.format_events_for_whatsapp([PLAIN], d)})

PERIOD_CASES = []
for name, start, end, by_date in [
    ("vazio", "2026-10-02", "2026-10-04", {}),
    ("dois dias", "2026-10-02", "2026-10-03", {"2026-10-02": [TIMED, ALL_DAY], "2026-10-03": [START_ONLY, PLAIN]}),
    ("fora de ordem", "2026-10-02", "2026-10-09", {"2026-10-09": [PLAIN], "2026-10-04": [event("Sem hora", "garbage", "")], "2026-10-05": [TIMED]}),
]:
    converted = {date.fromisoformat(k): v for k, v in by_date.items()}
    PERIOD_CASES.append({
        "name": name, "start": start, "end": end, "by_date": by_date,
        "output": Formatter.format_period_events_for_whatsapp(converted, date.fromisoformat(start), date.fromisoformat(end)),
    })

calendar = {
    "strip_html": [{"input": s, "output": safe(Formatter.strip_html, s)} for s in STRIP_HTML],
    "format_time": [{"input": s, "output": safe(Formatter.format_time, s)} for s in FORMAT_TIME],
    "today": TODAY.isoformat(),
    "day": DAY_CASES,
    "period": PERIOD_CASES,
}

with open(OUT_JSON, "w", encoding="utf-8", newline="\n") as fh:
    json.dump(
        {
            "pages": {
                "already_connected": connect_pages[0],
                "denied": page_denied,
                "connected": page_connected,
                "error": page_error,
            },
            "whatsapp": {
                "connected": whatsapp_connected,
                "token_revoked_with_link": revoked[0],
                "token_revoked_without_link": revoked[1],
            },
            "calendar": calendar,
        },
        fh,
        ensure_ascii=False,
        indent=2,
    )
    fh.write("\n")
print("escrito", OUT_JSON)


def csharp_literal(text):
    """Literal C# ordinario por linha (exato, sem depender de indentacao de raw string)."""
    lines = text.split("\n")
    parts = []
    for index, line in enumerate(lines):
        suffix = "\\n" if index < len(lines) - 1 else ""
        escaped = line.replace("\\", "\\\\").replace('"', '\\"')
        assert all(ord(c) < 128 for c in escaped), "pagina com caractere nao ASCII"
        parts.append('"' + escaped + suffix + '"')
    return (" +\n        ").join(parts)


header = """// <auto-generated>
// Gerado por tests/Fixtures/generate_google_fixtures.py a partir de app/routes/webhooks/handlers/calendar_handler.py.
// Nao edite a mao: rode o script. Cada pagina e byte a byte a do Flask (indentacao e quebras incluidas).
// Marcadores: {nome} em Connected e {texto} em Error. (spec 010 apendice B)
// </auto-generated>
namespace MeuSecretario.Api.Google;

public static class GooglePages
{
"""
body = ""
for name, text in [("AlreadyConnected", connect_pages[0]), ("Denied", page_denied), ("Connected", page_connected), ("Error", page_error)]:
    body += f"    public const string {name} =\n        {csharp_literal(text)};\n\n"
with open(OUT_PAGES, "w", encoding="utf-8", newline="\n") as fh:
    fh.write(header + body.rstrip("\n") + "\n}\n")
print("escrito", OUT_PAGES)
