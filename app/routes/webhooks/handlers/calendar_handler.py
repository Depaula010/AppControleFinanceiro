# app/routes/webhooks/handlers/calendar_handler.py
"""
CalendarHandler - Processa webhooks de calendario.

Rotas:
- /connect-calendar/<usuario_id>: Inicia OAuth
- /oauth2callback: Callback do Google
- /disconnect-calendar/<usuario_id>: Desconecta
"""

from typing import Tuple, Any
from flask import jsonify, request, redirect
from sqlalchemy import text

from app import db_engine
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app.utils import ensure_db_connection
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services import notification_service


class CalendarHandler:
    """Handler para webhooks de calendario."""

    def handle_connect(self, usuario_id: int) -> Tuple[Any, int]:
        """
        Endpoint para iniciar processo de conexao OAuth2.
        Usuario acessa via link enviado pelo WhatsApp.
        """
        try:
            ensure_db_connection()
        except Exception as e:
            return jsonify({
                "status": "erro",
                "resposta": "Banco de dados temporariamente indisponivel"
            }), 503

        try:
            # Verificar se usuario existe
            with db_engine.connect() as conn:
                sql = text("SELECT nome FROM Usuarios WHERE id = :uid")
                usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()

            if not usuario:
                return "Erro: Usuario nao encontrado", 404

            # Verificar se ja esta conectado COM TODOS os escopos necessarios
            is_connected = GoogleCalendarOAuthService.is_user_connected(usuario_id)
            has_drive = GoogleCalendarOAuthService.has_drive_scope(usuario_id)
            force = request.args.get('force') == 'true'

            if is_connected and has_drive and not force:
                return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Ja Conectado</title>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                    body {
                        font-family: Arial, sans-serif;
                        text-align: center;
                        padding: 50px;
                        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                        color: white;
                    }
                    .container {
                        background: white;
                        color: #333;
                        padding: 40px;
                        border-radius: 15px;
                        max-width: 500px;
                        margin: 0 auto;
                        box-shadow: 0 10px 30px rgba(0,0,0,0.3);
                    }
                    .icon { font-size: 64px; margin-bottom: 20px; }
                    h1 { color: #667eea; margin-bottom: 20px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="icon">OK</div>
                    <h1>Voce ja esta conectado!</h1>
                    <p>Seu Google Calendar e Google Drive ja estao integrados.</p>
                    <p>Volte para o WhatsApp e use:</p>
                    <p><strong>"Tenho compromisso hoje?"</strong></p>
                    <p><strong>"Salvar no drive pasta X"</strong> (envie com uma imagem)</p>
                </div>
            </body>
            </html>
            """, 200

            # Se conectado mas sem escopo Drive, redireciona para re-autorizacao
            if is_connected and not has_drive:
                print(f"[OAUTH] Usuario {usuario_id} conectado mas sem escopo Drive. Redirecionando para re-autorizacao.")

            # Gerar URL de autorizacao
            auth_url = GoogleCalendarOAuthService.get_authorization_url(usuario_id)

            # Redirecionar para Google
            return redirect(auth_url)

        except Exception as e:
            print(f"[OAUTH] Erro ao conectar: {e}")
            return f"Erro ao conectar: {str(e)}", 500

    def handle_oauth2callback(self) -> Tuple[Any, int]:
        """
        Callback do Google apos autorizacao.
        Google redireciona usuario para ca com codigo de autorizacao.
        """
        try:
            # Pegar parametros
            code = request.args.get('code')
            state = request.args.get('state')
            error = request.args.get('error')

            # Verificar se usuario negou
            if error:
                return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Autorizacao Negada</title>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                    body {
                        font-family: Arial, sans-serif;
                        text-align: center;
                        padding: 50px;
                        background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
                        color: white;
                    }
                    .container {
                        background: white;
                        color: #333;
                        padding: 40px;
                        border-radius: 15px;
                        max-width: 500px;
                        margin: 0 auto;
                        box-shadow: 0 10px 30px rgba(0,0,0,0.3);
                    }
                    .icon { font-size: 64px; margin-bottom: 20px; }
                    h1 { color: #f5576c; margin-bottom: 20px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="icon">X</div>
                    <h1>Autorizacao Negada</h1>
                    <p>Voce negou o acesso ao Google Calendar.</p>
                    <p>Para usar esta funcionalidade, voce precisa autorizar o acesso.</p>
                    <p>Tente novamente quando quiser!</p>
                </div>
            </body>
            </html>
            """, 200

            if not code or not state:
                return "Erro: codigo ou state faltando", 400

            # Trocar codigo por tokens
            usuario_id = GoogleCalendarOAuthService.exchange_code_for_tokens(code, state)

            # Buscar dados do usuario
            with db_engine.connect() as conn:
                sql = text("SELECT nome, numero_whatsapp FROM Usuarios WHERE id = :uid")
                usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()

            # Enviar notificacao no WhatsApp
            if usuario and usuario.numero_whatsapp:
                mensagem = (
                    f"*Google Calendar + Drive Conectados!*\n\n"
                    f"Ola {usuario.nome}! Sua conta Google foi conectada com sucesso.\n\n"
                    f"*Calendario:*\n"
                    f"- Tenho compromisso hoje?\n"
                    f"- Minha agenda amanha\n\n"
                    f"*Google Drive:*\n"
                    f"- Envie uma imagem/documento com a legenda:\n"
                    f"  _salvar no drive pasta Notas Fiscais_\n\n"
                    f"Seus dados estao seguros via OAuth2!"
                )

                notification_service.enviar_notificacao_whatsapp(
                    usuario.numero_whatsapp,
                    mensagem,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

            # Pagina de sucesso
            nome_usuario = usuario.nome if usuario else 'Usuario'
            return f"""
        <!DOCTYPE html>
        <html>
        <head>
            <title>Conexao Autorizada</title>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
                body {{
                    font-family: Arial, sans-serif;
                    text-align: center;
                    padding: 50px;
                    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                    color: white;
                }}
                .container {{
                    background: white;
                    color: #333;
                    padding: 40px;
                    border-radius: 15px;
                    max-width: 500px;
                    margin: 0 auto;
                    box-shadow: 0 10px 30px rgba(0,0,0,0.3);
                    animation: slideIn 0.5s ease-out;
                }}
                @keyframes slideIn {{
                    from {{ transform: translateY(-50px); opacity: 0; }}
                    to {{ transform: translateY(0); opacity: 1; }}
                }}
                .icon {{
                    font-size: 80px;
                    margin-bottom: 20px;
                    animation: bounce 1s infinite;
                }}
                @keyframes bounce {{
                    0%, 100% {{ transform: translateY(0); }}
                    50% {{ transform: translateY(-10px); }}
                }}
                h1 {{
                    color: #667eea;
                    margin-bottom: 20px;
                    font-size: 28px;
                }}
                .success {{ color: #10b981; font-weight: bold; }}
                .info {{
                    background: #f0f9ff;
                    padding: 20px;
                    border-radius: 10px;
                    margin: 20px 0;
                    border-left: 4px solid #667eea;
                }}
                .commands {{
                    text-align: left;
                    margin: 20px 0;
                }}
                .command {{
                    background: #f9fafb;
                    padding: 10px;
                    margin: 8px 0;
                    border-radius: 5px;
                    border-left: 3px solid #10b981;
                }}
                .footer {{
                    margin-top: 30px;
                    font-size: 14px;
                    color: #6b7280;
                }}
            </style>
        </head>
        <body>
            <div class="container">
                <div class="icon">OK</div>
                <h1 class="success">Google Calendar + Drive Conectados!</h1>
                <p>Ola <strong>{nome_usuario}</strong>!</p>
                <p>Sua conta Google foi conectada com sucesso.</p>

                <div class="info">
                    <h3 style="margin-top: 0; color: #667eea;">Calendario:</h3>
                    <div class="commands">
                        <div class="command">- "Tenho compromisso hoje?"</div>
                        <div class="command">- "Minha agenda amanha"</div>
                        <div class="command">- "Compromissos do final de semana"</div>
                    </div>
                    <h3 style="color: #667eea;">Google Drive:</h3>
                    <div class="commands">
                        <div class="command">- Envie uma imagem com: "salvar no drive pasta X"</div>
                        <div class="command">- Envie um PDF com: "guardar no drive"</div>
                    </div>
                </div>

                <div class="footer">
                    <p>Voce ja pode fechar esta janela</p>
                    <p>Volte para o WhatsApp!</p>
                    <p style="margin-top: 20px;">
                        <small>Conexao segura via OAuth2 oficial do Google</small>
                    </p>
                </div>
            </div>
        </body>
        </html>
        """, 200

        except Exception as e:
            print(f"[OAUTH] Erro no callback: {e}")
            return f"""
        <!DOCTYPE html>
        <html>
        <head>
            <title>Erro</title>
            <meta charset="utf-8">
            <style>
                body {{ text-align: center; padding: 50px; font-family: Arial; }}
                .error {{ color: red; }}
            </style>
        </head>
        <body>
            <h1 class="error">Erro</h1>
            <p>Ocorreu um erro ao processar a autorizacao:</p>
            <p><code>{str(e)}</code></p>
            <p>Tente novamente ou contate o suporte.</p>
        </body>
        </html>
        """, 500

    def handle_disconnect(self, usuario_id: int) -> Tuple[Any, int]:
        """
        Permite usuario desconectar Google Calendar.
        Pode ser chamado via WhatsApp ou interface web.
        """
        try:
            # Autenticar (verificar se e o proprio usuario)
            # ... adicionar autenticacao se necessario ...

            GoogleCalendarOAuthService.revoke_access(usuario_id)

            return jsonify({
                "status": "sucesso",
                "mensagem": "Google Calendar desconectado com sucesso"
            }), 200

        except Exception as e:
            print(f"[OAUTH] Erro ao desconectar: {e}")
            return jsonify({
                "status": "erro",
                "mensagem": str(e)
            }), 500


# Instancia singleton
_handler = CalendarHandler()


def connect_calendar(usuario_id: int) -> Tuple[Any, int]:
    """Inicia processo de conexao OAuth2."""
    return _handler.handle_connect(usuario_id)


def oauth2callback() -> Tuple[Any, int]:
    """Callback do Google apos autorizacao."""
    return _handler.handle_oauth2callback()


def disconnect_calendar(usuario_id: int) -> Tuple[Any, int]:
    """Desconecta Google Calendar."""
    return _handler.handle_disconnect(usuario_id)
