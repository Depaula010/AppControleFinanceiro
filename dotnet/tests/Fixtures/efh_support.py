# -*- coding: utf-8 -*-
"""
Apoio comum das sub-entregas 012e (calendario), 012f (configuracoes) e 012h (Drive) para os geradores de fixtures e o harness:

  - relogio congelado: `frozen_time(...)` troca `sys.modules['datetime']` por um modulo cujos `datetime`/`date` respondem `now()`/`today()`
    com o instante do cenario (o Flask le o relogio do container; o .NET le o IClock);
  - Google Calendar falso (`FakeGoogle`) com o mesmo recorte de tempo do Google (fim > timeMin e inicio < timeMax) e `q`;
  - banco falso minimo para os servicos de configuracao.

Todos os valores sao FICTICIOS.
"""
import contextlib
import datetime as _real_dt
import sys
import types
from zoneinfo import ZoneInfo

SP = ZoneInfo("America/Sao_Paulo")


class _Clock:
    now = _real_dt.datetime(2026, 10, 8, 14, 20, 0, tzinfo=SP)


CLOCK = _Clock()


class FrozenDatetime(_real_dt.datetime):
    @classmethod
    def now(cls, tz=None):
        base = CLOCK.now
        if tz is None:
            base = base.replace(tzinfo=None)
        else:
            base = base.astimezone(tz)
        return cls(base.year, base.month, base.day, base.hour, base.minute, base.second, base.microsecond, tzinfo=base.tzinfo)

    @classmethod
    def today(cls):
        return cls.now()

    @classmethod
    def utcnow(cls):
        return cls.now(_real_dt.timezone.utc).replace(tzinfo=None)


class FrozenDate(_real_dt.date):
    @classmethod
    def today(cls):
        base = CLOCK.now
        return cls(base.year, base.month, base.day)


def _proxy():
    module = types.ModuleType("datetime")
    for name in dir(_real_dt):
        if not name.startswith("__"):
            setattr(module, name, getattr(_real_dt, name))
    module.datetime = FrozenDatetime
    module.date = FrozenDate
    return module


@contextlib.contextmanager
def frozen_time(now):
    """`now`: datetime com fuso de Sao Paulo. Dentro do bloco, qualquer `from datetime import ...` recebe as classes congeladas."""
    saved_module = sys.modules["datetime"]
    saved_now = CLOCK.now
    CLOCK.now = now
    sys.modules["datetime"] = _proxy()
    try:
        yield
    finally:
        sys.modules["datetime"] = saved_module
        CLOCK.now = saved_now


def sp(year, month, day, hour=0, minute=0, second=0):
    return _real_dt.datetime(year, month, day, hour, minute, second, tzinfo=SP)


# ------------------------------------------------------------------ Google Calendar falso
def _event_bounds(event):
    def parse(part):
        if "dateTime" in part:
            text = part["dateTime"].replace("Z", "+00:00")
            return _real_dt.datetime.fromisoformat(text)
        return _real_dt.datetime.fromisoformat(part["date"]).replace(tzinfo=SP)

    return parse(event["start"]), parse(event["end"])


class _Exec:
    def __init__(self, fn):
        self._fn = fn

    def execute(self):
        return self._fn()


class FakeGoogle:
    """calendars: [{id, summary, selected, primary, events:[evento no formato da API]}]. `calls` guarda as chamadas de events().list/insert/delete."""

    def __init__(self, calendars, fail_calendar_list=False, fail_events_for=(), fail_insert=None):
        self.calendars = calendars
        self.fail_calendar_list = fail_calendar_list
        self.fail_events_for = set(fail_events_for)
        self.fail_insert = fail_insert
        self.calls = []

    def calendarList(self):
        outer = self

        class CalList:
            def list(self_inner):
                def run():
                    if outer.fail_calendar_list:
                        raise Exception("falha ao listar calendarios")
                    return {"items": [
                        {k: v for k, v in {"id": c["id"], "summary": c.get("summary"), "selected": c.get("selected", True),
                                           "primary": c.get("primary", False)}.items() if v is not None}
                        for c in outer.calendars
                    ]}
                return _Exec(run)

        return CalList()

    def events(self):
        outer = self

        class Events:
            def list(self_inner, calendarId, timeMin=None, timeMax=None, q=None, maxResults=None, singleEvents=None, orderBy=None):
                def run():
                    outer.calls.append(["list", calendarId, timeMin, timeMax, q, maxResults])
                    if calendarId in outer.fail_events_for:
                        raise Exception("falha no calendario")
                    calendar = next(c for c in outer.calendars if c["id"] == calendarId)
                    low = _real_dt.datetime.fromisoformat(timeMin) if timeMin else None
                    high = _real_dt.datetime.fromisoformat(timeMax) if timeMax else None
                    items = []
                    for event in calendar.get("events", []):
                        start, end = _event_bounds(event)
                        if low is not None and end <= low:
                            continue
                        if high is not None and start >= high:
                            continue
                        if q:
                            haystack = " ".join(str(event.get(k, "")) for k in ("summary", "description", "location")).lower()
                            if q.lower() not in haystack:
                                continue
                        items.append(event)
                    if maxResults:
                        items = items[:maxResults]
                    return {"items": items}
                return _Exec(run)

            def insert(self_inner, calendarId, body):
                def run():
                    outer.calls.append(["insert", calendarId, body])
                    if outer.fail_insert:
                        raise Exception(outer.fail_insert)
                    return {"id": "novo-evento-1", "htmlLink": "https://calendar.example/novo"}
                return _Exec(run)

            def get(self_inner, calendarId, eventId):
                def run():
                    outer.calls.append(["get", calendarId, eventId])
                    for calendar in outer.calendars:
                        if calendar["id"] == calendarId:
                            for event in calendar.get("events", []):
                                if event["id"] == eventId:
                                    return event
                    raise Exception("404 Not Found")
                return _Exec(run)

            def delete(self_inner, calendarId, eventId):
                def run():
                    outer.calls.append(["delete", calendarId, eventId])
                    for calendar in outer.calendars:
                        if calendar["id"] == calendarId:
                            for event in calendar.get("events", []):
                                if event["id"] == eventId:
                                    calendar["events"].remove(event)
                                    return {}
                    raise Exception("404 Not Found")
                return _Exec(run)

        return Events()


def timed(event_id, summary, start, end, location=None, description=None, offset="-03:00"):
    """Evento com horario: start/end como 'YYYY-MM-DDTHH:MM:SS'."""
    event = {"id": event_id, "summary": summary, "start": {"dateTime": start + offset}, "end": {"dateTime": end + offset}}
    if location is not None:
        event["location"] = location
    if description is not None:
        event["description"] = description
    return event


def all_day(event_id, summary, start, end):
    return {"id": event_id, "summary": summary, "start": {"date": start}, "end": {"date": end}}


def calendar(cal_id, summary, events, selected=True, primary=False):
    return {"id": cal_id, "summary": summary, "selected": selected, "primary": primary, "events": events}


# ------------------------------------------------------------------ banco falso (execute com texto SQL)
class _Result:
    def __init__(self, rows):
        self._rows = rows

    def fetchone(self):
        return self._rows[0] if self._rows else None

    def fetchall(self):
        return list(self._rows)


class Row:
    def __init__(self, **kwargs):
        self.__dict__.update(kwargs)
        self._values = list(kwargs.values())

    def __getitem__(self, index):
        return self._values[index]


class FakeDb:
    """`handler(sql_normalizado, params)` devolve a lista de linhas; `queries` guarda o SQL normalizado e os parametros."""

    def __init__(self, handler):
        self.handler = handler
        self.queries = []

    def _run(self, sql, params):
        normalized = " ".join(str(sql).split())
        self.queries.append([normalized, dict(params or {})])
        return _Result(self.handler(normalized, dict(params or {})))

    def connect(self):
        db = self

        class Conn:
            def __enter__(self_inner):
                return self_inner

            def __exit__(self_inner, *a):
                return False

            def begin(self_inner):
                return self_inner

            def commit(self_inner):
                pass

            def execute(self_inner, sql, params=None):
                return db._run(sql, params)

        return Conn()

    def begin(self):
        return self.connect()
