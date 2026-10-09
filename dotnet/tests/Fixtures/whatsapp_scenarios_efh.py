# -*- coding: utf-8 -*-
"""
Cenarios de paridade das sub-entregas 012e (confirmar/cancelar evento e "calcular rota") e 012f (Configurar Notificacoes) contra o
`WhatsAppHandler` REAL do Flask. Reaproveita o harness da 012-0 (importa `whatsapp_handler_harness`, que monta o handler com os servicos
simulados) e troca o `NotificationConfigService` pelo servico real sobre um banco falso em memoria.

Saida: tests/Fixtures/whatsapp-pipeline-scenarios-efh.json (lida por CalendarConfigParityTests).
Uso: python whatsapp_scenarios_efh.py   (so para regenerar; a saida e versionada). Todos os valores sao FICTICIOS.
"""
import datetime as _dt
import json
import os
import sys
import types

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import whatsapp_handler_harness as H  # noqa: E402  (executa o harness base e regrava o json dele; saida identica)
import efh_support as S  # noqa: E402

_REAL_STDOUT = sys.stdout
sys.stdout = open(os.devnull, "w", encoding="utf-8")
OUT = os.path.join(HERE, "whatsapp-pipeline-scenarios-efh.json")
NUMBER = H.NUMBER

# ------------------------------------------------------------------ NotificationConfigService real com banco falso
NOTIF = {"row": None, "fail_update": False}


def _default_row():
    return {"resumo_matinal_ativo": True, "resumo_matinal_hora": _dt.time(7, 0), "alertas_financeiros_ativos": True,
            "checkin_noturno_ativo": True, "checkin_noturno_hora": _dt.time(20, 0)}


def _row(columns):
    return S.Row(**{c: NOTIF["row"][c] for c in columns})


def _notif_db(sql, params):
    if sql.startswith("SELECT resumo_matinal_ativo"):
        return [_row(["resumo_matinal_ativo", "resumo_matinal_hora", "alertas_financeiros_ativos", "checkin_noturno_ativo", "checkin_noturno_hora"])] \
            if NOTIF["row"] else []
    if sql.startswith("INSERT INTO NotificationConfigs"):
        NOTIF["row"] = _default_row()
        return [_row(["resumo_matinal_ativo", "resumo_matinal_hora", "alertas_financeiros_ativos", "checkin_noturno_ativo", "checkin_noturno_hora"])]
    if sql.startswith("UPDATE NotificationConfigs"):
        if NOTIF["fail_update"]:
            raise Exception("boom segredo")
        prefix = "resumo_matinal" if "resumo_matinal" in sql.split("RETURNING")[0] else "checkin_noturno"
        if "ativo" in params:
            NOTIF["row"][prefix + "_ativo"] = params["ativo"]
        if "hora" in params:
            NOTIF["row"][prefix + "_hora"] = params["hora"]
        return [_row([prefix + "_ativo", prefix + "_hora"])]
    raise AssertionError("SQL inesperado: " + sql)


NDB = S.FakeDb(_notif_db)
H.app.db_engine = NDB
if "sqlalchemy" not in sys.modules:
    sys.modules["sqlalchemy"] = types.ModuleType("sqlalchemy")
sys.modules["sqlalchemy"].text = lambda s: s
_ncs_ns = H.load("notification_config_service.py", H.source("services", "notification_config_service.py"))
H.handler_ns["NotificationConfigService"] = _ncs_ns["NotificationConfigService"]


def snapshot_notif():
    row = NOTIF["row"]
    if row is None:
        return None
    return {k: (v.strftime("%H:%M:%S") if isinstance(v, _dt.time) else v) for k, v in row.items()}


def scenario(name, group, texto, notif="none", fail_update=False, deviation=None, **cfg):
    """notif: 'none' (sem linha), 'default' ou um dict parcial sobre o padrao (horas como 'HH:MM:SS')."""
    if notif == "none":
        NOTIF["row"] = None
    else:
        row = _default_row()
        if isinstance(notif, dict):
            for key, value in notif.items():
                row[key] = _dt.datetime.strptime(value, "%H:%M:%S").time() if key.endswith("_hora") else value
        NOTIF["row"] = row
    NOTIF["fail_update"] = fail_update
    item = H.scenario(name, group, texto, deviation=deviation, **cfg)
    item["notif_initial"] = notif
    item["fail_update"] = fail_update
    item["result"]["notif"] = snapshot_notif()
    return item


SCENARIOS = []
add = SCENARIOS.append

# ------------------------------------------------------------------ 012e: confirmar e cancelar evento
KEY = "pending_event:%s:ab12cd34" % NUMBER
EVENT = {"usuario_id": 7, "titulo": "Reunião", "data_evento": "2026-10-09", "hora_inicio": "14:00", "hora_fim": None, "descricao": "Pauta",
         "localizacao": "Av Paulista 1000"}
EVENT_NO_PLACE = dict(EVENT, localizacao=None)
CREATED = {"create_event": [True, "Evento 'Reunião' criado com sucesso!", "g1"]}

for word in ["sim", "Sim", " confirmar ", "confirma", "OK", "s"]:
    add(scenario("evento_confirma_" + word.strip().lower(), "012e", word, redis={KEY: EVENT}, **CREATED))
add(scenario("evento_confirma_falha_da_api", "012e", "sim", deviation="008", redis={KEY: EVENT},
             create_event=[False, "Erro ao criar evento: quotaExceeded", None]))
add(scenario("evento_confirma_google_nao_conectado", "012e", "sim", redis={KEY: EVENT},
             create_event=[False, "Erro ao criar evento: Usuário não conectou Google Calendar", None]))
for word in ["não", "nao", "cancelar", "cancela", "desistir", "n", "NÃO"]:
    add(scenario("evento_cancela_" + word.lower(), "012e", word, redis={KEY: EVENT}))
add(scenario("evento_dois_pendentes_pega_o_ultimo", "012e", "sim",
             redis={"pending_event:%s:aaaaaaaa" % NUMBER: dict(EVENT, titulo="Primeiro"), "pending_event:%s:bbbbbbbb" % NUMBER: dict(EVENT, titulo="Segundo")},
             create_event=[True, "Evento 'Segundo' criado com sucesso!", "g2"]))
add(scenario("sim_sem_evento_segue_para_o_gemini", "012e", "sim", intent="Menu de Ajuda"))
add(scenario("nao_sem_evento_segue_para_o_gemini", "012e", "não", intent="Menu de Ajuda"))
add(scenario("cancelar_sem_nada", "012e", "cancelar"))
add(scenario("cancelar_com_evento", "012e", "Cancelar", redis={KEY: EVENT}))
add(scenario("texto_comum_com_evento_pendente_segue", "012e", "quanto gastei?", redis={KEY: EVENT}, intent="Menu de Ajuda"))

# ------------------------------------------------------------------ 012e: calcular rota
HOME = {"label": "casa", "endereco": "Rua A, 10, Centro", "lat": -19.9, "lon": -43.9}
WORK = {"label": "trabalho", "endereco": "Av B, 20", "lat": -19.8, "lon": -43.8}
OTHER = {"label": "outro", "endereco": "Praça C", "lat": -19.7, "lon": -43.7}
GEO = {"geocode": [-23.56, -46.65, "Avenida Paulista, 1000, São Paulo"]}
TRAVEL = {"travel": {"duration_minutes": 45, "distance_km": 12.5, "route_summary": None}}
for text in ["calcular rota", "Sim, calcular rota", "quero calcular a rota"]:
    add(scenario("rota_sem_evento_" + str(len(SCENARIOS)), "012e", text))
add(scenario("rota_sem_local", "012e", "calcular rota", redis={KEY: EVENT_NO_PLACE}))
add(scenario("rota_local_vazio", "012e", "calcular rota", redis={KEY: dict(EVENT, localizacao="")}))
add(scenario("rota_sem_enderecos", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[]))
add(scenario("rota_um_endereco", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[HOME], **GEO, **TRAVEL))
add(scenario("rota_um_endereco_trabalho", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[WORK], **GEO, **TRAVEL))
add(scenario("rota_distancia_inteira", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[HOME], **GEO,
             travel={"duration_minutes": 5, "distance_km": 10.0, "route_summary": None}))
add(scenario("rota_destino_sem_rotulo", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[HOME], geocode=[-23.56, -46.65, None], **TRAVEL))
add(scenario("rota_destino_nao_localizado", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[HOME], geocode=[None, None, None]))
add(scenario("rota_nao_calculada", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[HOME], **GEO, travel=None))
add(scenario("rota_varios_enderecos", "012e", "calcular rota", redis={KEY: EVENT}, addresses=[HOME, WORK, OTHER]))
add(scenario("rota_sim_nao_confirma", "012e", "sim, calcular rota", redis={KEY: EVENT}, addresses=[HOME, WORK]))

# ------------------------------------------------------------------ 012f: Configurar Notificações (inline)
def notif(name, text, **kw):
    add(scenario(name, "012f", text, intent="Configurar Notificações", **kw))


notif("resumo_ativar_com_hora", "ativar resumo matinal às 8h", notif="default")
notif("resumo_ativar_sem_hora", "ativar o resumo matinal", notif={"resumo_matinal_ativo": False})
notif("resumo_ligar_com_minutos", "ligar o resumo matinal 7:30", notif="default")
notif("resumo_desativar_vira_ativar", "desativar resumo matinal", notif="default")
notif("resumo_desligar_vira_ativar", "desligar o resumo", notif={"resumo_matinal_ativo": False})
notif("resumo_configurar_hora", "mudar o resumo matinal para as 9h30", notif="default")
notif("resumo_configurar_sem_hora", "resumo matinal", notif={"resumo_matinal_ativo": False, "resumo_matinal_hora": "06:15:00"})
notif("resumo_briefing", "configurar briefing 6h", notif="default")
notif("resumo_sem_linha_cria", "ativar resumo matinal 8h")
notif("resumo_hora_invalida", "ativar resumo matinal 25h", notif="default")
notif("resumo_minuto_invalido", "resumo matinal 8:75", notif="default")
notif("resumo_hora_de_dois_digitos", "resumo matinal 99h", notif="default")
notif("resumo_banco_falha", "ativar resumo matinal 8h", notif="default", fail_update=True)
notif("checkin_ativar_com_hora", "ativar check-in noturno às 21h", notif="default")
notif("checkin_ativar_sem_hora", "ligar checkin", notif={"checkin_noturno_ativo": False})
notif("checkin_desligar_vira_ativar", "desligar o check-in noturno", notif="default")
notif("checkin_off_desativa", "check-in off", notif="default")
notif("checkin_off_com_ativar_ganha_ativar", "ativar checkin off", notif="default")
notif("checkin_configurar_hora", "check in noturno 19:30", notif="default")
notif("checkin_limite_inferior", "checkin 18h", notif="default")
notif("checkin_limite_superior", "checkin 23:00", notif="default")
notif("checkin_abaixo_do_limite", "checkin 17h", notif="default")
notif("checkin_acima_do_limite", "checkin 23:30", notif="default")
notif("checkin_hora_invalida", "checkin 24h", notif="default")
notif("checkin_contas_pendentes", "contas pendentes 22h", notif="default")
notif("checkin_banco_falha", "ativar checkin 21h", notif="default", fail_update=True)
notif("resumo_vence_checkin", "resumo matinal e check-in noturno 8h", notif="default")
notif("sem_linha_checkin_cria", "check-in noturno 21h")

GEMINI = "gemini_service.extract_notification_config"


def via_gemini(name, config, **kw):
    add(scenario(name, "012f", "configurar notificação", intent="Configurar Notificações", returns={GEMINI: config}, **kw))


via_gemini("gemini_agenda_ativar_grava_e_quebra", {"tipo": "agenda_diaria", "acao": "ativar", "hora": None, "dias_antes": None}, notif={"resumo_matinal_ativo": False})
via_gemini("gemini_agenda_desativar_grava_e_quebra", {"tipo": "agenda_diaria", "acao": "desativar"}, notif="default")
via_gemini("gemini_agenda_configurar_grava_e_quebra", {"tipo": "agenda_diaria", "acao": "configurar", "hora": "08:30"}, notif="default")
via_gemini("gemini_agenda_hora_invalida", {"tipo": "agenda_diaria", "acao": "configurar", "hora": "8h"}, notif="default")
via_gemini("gemini_agenda_acao_desconhecida", {"tipo": "agenda_diaria", "acao": "apagar"}, notif="default")
via_gemini("gemini_agenda_banco_falha", {"tipo": "agenda_diaria", "acao": "ativar"}, notif="default", fail_update=True)
via_gemini("gemini_contas_ativar_quebra_sem_gravar", {"tipo": "contas_vencer", "acao": "ativar"}, notif="default")
via_gemini("gemini_contas_configurar_quebra_sem_gravar", {"tipo": "contas_vencer", "acao": "configurar", "hora": "09:00", "dias_antes": 2}, notif="default")
via_gemini("gemini_contas_acao_desconhecida", {"tipo": "contas_vencer", "acao": "x"}, notif="default")
via_gemini("gemini_tipo_desconhecido", {"tipo": "outra", "acao": "ativar"}, notif="default")
via_gemini("gemini_tipo_nulo", {"tipo": None, "acao": None}, notif="default")

sys.stdout = _REAL_STDOUT
with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
    json.dump({"api_key": H.API_KEY, "signature_key": H.SIGNATURE_KEY, "number": NUMBER, "scenarios": SCENARIOS}, fh, ensure_ascii=False, indent=1)
print("ok", OUT, len(SCENARIOS))
