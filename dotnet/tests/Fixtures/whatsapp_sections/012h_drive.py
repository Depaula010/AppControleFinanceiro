# -*- coding: utf-8 -*-
"""
Secao 012h (Drive) do gerador de fixtures: roda UploadDriveIntent REAL (extracao da pasta, validacoes, base64, mensagens) com o
GoogleDriveService real (validate_file) e o upload roteirizado. Valores FICTICIOS.
"""
import base64
import sys
import types

sys.path.insert(0, HERE)

DRIVE = {"scope": True, "service_ok": True, "upload": None, "uploaded": []}
CONFIG = types.SimpleNamespace(GOOGLE_REDIRECT_URI="https://proxy.example/oauth/callback")


class _OAuthDrive:
    @staticmethod
    def has_drive_scope(usuario_id):
        return DRIVE["scope"]

    @staticmethod
    def get_drive_service(usuario_id):
        if not DRIVE["service_ok"]:
            raise Exception("credenciais expiradas")
        return object()


stub_module("app.services.google_calendar_oauth_service", GoogleCalendarOAuthService=_OAuthDrive)
_drive_ns = load("google_drive_service.py", source("services", "google_drive_service.py"))
_Real = _drive_ns["GoogleDriveService"]


class _Drive(_Real):
    @staticmethod
    def upload_file(service, file_data, filename, folder_path, mime_type):
        DRIVE["uploaded"].append({"filename": filename, "folder_path": list(folder_path), "mime": mime_type, "size": len(file_data)})
        return DRIVE["upload"]


stub_module("app.services.google_drive_service", GoogleDriveService=_Drive)

_base_ns = load("base_intent.py", source("routes", "webhooks", "intents", "base_intent.py"))
stub_module("app.routes.webhooks.intents.base_intent", BaseIntent=_base_ns["BaseIntent"], ConfirmationRequiredIntent=_base_ns["ConfirmationRequiredIntent"])
UploadDrive = load("drive_intents.py", source("routes", "webhooks", "intents", "drive_intents.py").replace(
    "from .base_intent import", "from app.routes.webhooks.intents.base_intent import"))["UploadDriveIntent"]

# ------------------------------------------------------------------ extracao da pasta
folder_texts = [
    "salvar no drive pasta Notas Fiscais",
    "Salvar no Drive na pasta IR > IR 2026",
    "guardar no google drive pasta IR/Comprovantes",
    "enviar para o drive pasta \"Contratos 2026\"",
    "subir pro drive na pasta Fotos\\Viagem",
    "upload em drive pasta CNPJ",
    "pasta Comprovantes",
    "PASTA recibos de luz",
    "salvar em Documentos",
    "guardar na pasta",
    "manda pra Casa",
    "salvar no drive",
    "oi",
    "",
    "   ",
    "pasta IR > > IR 2026",
    "pasta  ",
    "pasta !!!",
    "pasta Água & Luz",
    "pasta nota_fiscal.v2",
    "pasta João da Silva/ação",
    "pasta " + "a" * 150,
    "pasta ÁREA 51 > secreta",
    "salvar no drive pasta 'Minhas Coisas'",
    "pasta iR 2026",
    "  salvar no drive pasta   Notas   Fiscais  ",
    "pasta 1ª parte",
    "pasta contas de 2026, fevereiro",
]
folder_cases = []
for text in folder_texts:
    intent = UploadDrive(usuario_id=7, mensagem=text, conn=None, numero_whatsapp="5531999990001")
    folder_cases.append({"text": text, "path": intent._extract_folder_path(text)})
out["drive_folder_cases"] = folder_cases

# ------------------------------------------------------------------ fluxo completo
OK_B64 = base64.b64encode(b"conteudo do arquivo").decode()
SUCCESS = {"success": True, "file_id": "f1", "filename": "nota.png", "folder_name": "Notas Fiscais", "folder_path": ["Notas Fiscais"],
           "folder_path_display": "Notas Fiscais", "folder_id": "d1", "web_view_link": "https://drive.example/f1", "web_content_link": None}


def run(name, text, data, mime, filename="nota.png", scope=True, service_ok=True, upload=SUCCESS, redirect="https://proxy.example/oauth/callback", has_data=True):
    DRIVE.update(scope=scope, service_ok=service_ok, upload=upload, uploaded=[])
    CONFIG.GOOGLE_REDIRECT_URI = redirect
    stub_module("app.config", GOOGLE_REDIRECT_URI=redirect)
    intent = UploadDrive(usuario_id=7, mensagem=text, conn=None, numero_whatsapp="5531999990001", media_data=data, media_type=mime, media_filename=filename)
    result = intent.handle()
    return {"name": name, "text": text, "data": data, "mime": mime, "filename": filename, "scope": scope, "service_ok": service_ok,
            "upload": upload, "redirect": redirect, "success": result["success"], "message": result["message"], "uploaded": list(DRIVE["uploaded"])}


drive_cases = [
    run("sucesso", "salvar no drive pasta Notas Fiscais", OK_B64, "image/png"),
    run("sucesso_com_prefixo", "pasta IR > IR 2026", "data:image/png;base64," + OK_B64, "image/png",
        upload=dict(SUCCESS, folder_path_display="IR > IR 2026", folder_name="IR 2026")),
    run("sucesso_sem_link", "pasta Docs", OK_B64, "application/pdf", upload=dict(SUCCESS, web_view_link=None, filename="doc.pdf")),
    run("sucesso_so_folder_name", "pasta Docs", OK_B64, "application/pdf", upload={"success": True, "filename": "a.pdf", "folder_name": "Docs", "web_view_link": "https://x"}),
    run("sem_nome_do_arquivo", "pasta Docs", OK_B64, "image/png", filename=None),
    run("sem_arquivo", "salvar no drive", None, "image/png"),
    run("arquivo_vazio_texto", "salvar no drive", "", "image/png"),
    run("sem_tipo", "salvar no drive", OK_B64, None),
    run("tipo_vazio", "salvar no drive", OK_B64, ""),
    run("sem_escopo", "salvar no drive", OK_B64, "image/png", scope=False),
    run("sem_escopo_sem_uri", "salvar no drive", OK_B64, "image/png", scope=False, redirect=""),
    run("erro_de_autenticacao", "salvar no drive", OK_B64, "image/png", service_ok=False),
    run("erro_de_autenticacao_sem_uri", "salvar no drive", OK_B64, "image/png", service_ok=False, redirect=""),
    run("base64_invalido", "salvar no drive", "isto nao e base64!", "image/png"),
    run("base64_sem_preenchimento", "salvar no drive", "aGVsbG8", "image/png"),
    run("base64_com_quebras", "salvar no drive", OK_B64[:8] + "\n" + OK_B64[8:], "image/png"),
    run("base64_nao_ascii", "salvar no drive", "aGVsbG8=é", "image/png"),
    run("base64_so_igual", "salvar no drive", "====", "image/png"),
    run("base64_vazio_depois_do_prefixo", "salvar no drive", "data:image/png;base64,", "image/png"),
    run("tipo_nao_permitido", "salvar no drive", OK_B64, "application/zip"),
    run("tipo_com_charset", "salvar no drive", OK_B64, "image/png; charset=x"),
    run("tipo_maiusculo", "salvar no drive", OK_B64, "IMAGE/PNG"),
    run("falha_no_upload", "salvar no drive", OK_B64, "image/png", upload={"success": False, "error": "Limite de armazenamento do Google Drive atingido."}),
]
out["drive_cases"] = drive_cases

# casos de base64 isolados (b64decode em modo nao estrito)
import binascii

b64_cases = []
for text in ["aGVsbG8=", "aGVsbG8", "aGVsbG8==", "aGVs bG8=", "aGVs\nbG8=", "====", "", "a", "ab", "abc", "abcd", "YQ==", "YQ=", "YQ", "YWJj", "YWJjZA==", "YWJjZA=", "YWJjZA",
             "YQ==YQ==", "Y Q = =", "YW$Jj", "@@@@", "é", "YWJj=", "YWJj==", "=YWJj", "YQ===", "YWI=", "YWI", "YW=="]:
    try:
        decoded = base64.b64decode(text)
        b64_cases.append({"text": text, "bytes": list(decoded)})
    except (binascii.Error, ValueError):
        b64_cases.append({"text": text, "bytes": None})
out["base64_cases"] = b64_cases
