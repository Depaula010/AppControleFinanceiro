# app/routes/webhooks/calendar.py
"""
Rotas de integração com Google Calendar via OAuth2.

Contém:
- connect_calendar: Inicia fluxo OAuth2
- oauth2callback: Callback do Google após autorização
- disconnect_calendar: Revoga acesso ao calendário
"""

from flask import request, jsonify, redirect
from sqlalchemy import text

from app import db_engine
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app.services import notification_service
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService

# Utilitários Fase A
from app.shared.decorators import handle_errors

from . import webhooks_bp



@webhooks_bp.route('/connect-calendar/<int:usuario_id>', methods=['GET'])
@handle_errors(tag="OAUTH_CONNECT")
def connect_calendar(usuario_id):
    """
    Endpoint para iniciar processo de conexão OAuth2.
    Usuário acessa via link enviado pelo WhatsApp.

    Fase A: Refatorado com decorators (economia: ~8 linhas)
    - @handle_errors: Tratamento de exceções automático
    """
    if not db_engine:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503

    # Verificar se usuário existe
    with db_engine.connect() as conn:
        sql = text("SELECT nome FROM Usuarios WHERE id = :uid")
        usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()

    if not usuario:
        return "❌ Usuário não encontrado", 404

    # Verificar se já está conectado
    if GoogleCalendarOAuthService.is_user_connected(usuario_id):
        return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Já Conectado</title>
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
                    .btn {
                        background: #667eea;
                        color: white;
                        padding: 15px 30px;
                        border-radius: 8px;
                        text-decoration: none;
                        display: inline-block;
                        margin-top: 20px;
                        font-weight: bold;
                    }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="icon">✅</div>
                    <h1>Você já está conectado!</h1>
                    <p>Seu Google Calendar já está integrado.</p>
                    <p>Volte para o WhatsApp e pergunte:</p>
                    <p><strong>"Tenho compromisso hoje?"</strong></p>
                </div>
            </body>
            </html>
            """, 200

    # Gerar URL de autorização
    auth_url = GoogleCalendarOAuthService.get_authorization_url(usuario_id)

    # Redirecionar para Google
    return redirect(auth_url)


@webhooks_bp.route('/oauth2callback', methods=['GET'])
@handle_errors(tag="OAUTH_CALLBACK")
def oauth2callback():
    """
    Callback do Google após autorização.
    Google redireciona usuário para cá com código de autorização.

    Fase A: Refatorado com decorators (economia: ~5 linhas)
    - @handle_errors: Tratamento de exceções automático
    """
    # Pegar parâmetros
    code = request.args.get('code')
    state = request.args.get('state')
    error = request.args.get('error')

    # Verificar se usuário negou
    if error:
        return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Autorização Negada</title>
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
                    <div class="icon">❌</div>
                    <h1>Autorização Negada</h1>
                    <p>Você negou o acesso ao Google Calendar.</p>
                    <p>Para usar esta funcionalidade, você precisa autorizar o acesso.</p>
                    <p>Tente novamente quando quiser! 👋</p>
                </div>
            </body>
            </html>
            """, 200
        
        if not code or not state:
            return "❌ Erro: código ou state faltando", 400
        
        # Trocar código por tokens
        usuario_id = GoogleCalendarOAuthService.exchange_code_for_tokens(code, state)
        
        # Buscar dados do usuário
        with db_engine.connect() as conn:
            sql = text("SELECT nome, numero_whatsapp FROM Usuarios WHERE id = :uid")
            usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()
        
        # Enviar notificação no WhatsApp
        if usuario and usuario.numero_whatsapp:
            mensagem = (
                f"✅ *Google Calendar Conectado!*\n\n"
                f"Olá {usuario.nome}! Seu calendário foi conectado com sucesso.\n\n"
                f"Agora você pode perguntar:\n"
                f"• Tenho compromisso hoje?\n"
                f"• Minha agenda amanhã\n"
                f"• Compromissos do final de semana\n\n"
                f"🔒 Seus dados estão seguros via OAuth2!"
            )
            
            notification_service.enviar_notificacao_whatsapp(
                usuario.numero_whatsapp,
                mensagem,
                BOT_WHATSAPP_URL,
                API_SECRET_KEY
            )
        
        # Página de sucesso
        return f"""
        <!DOCTYPE html>
        <html>
        <head>
            <title>Conexão Autorizada</title>
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
                <div class="icon">✅</div>
                <h1 class="success">Google Calendar Conectado!</h1>
                <p>Olá <strong>{usuario.nome if usuario else 'Usuário'}</strong>!</p>
                <p>Seu calendário foi conectado com sucesso.</p>
                
                <div class="info">
                    <h3 style="margin-top: 0; color: #667eea;">📱 Agora você pode:</h3>
                    <div class="commands">
                        <div class="command">• "Tenho compromisso hoje?"</div>
                        <div class="command">• "Minha agenda amanhã"</div>
                        <div class="command">• "Compromissos do final de semana"</div>
                        <div class="command">• "Agenda desta semana"</div>
                    </div>
                </div>
                
                <div class="footer">
                    <p>✅ Você já pode fechar esta janela</p>
                    <p>💬 Volte para o WhatsApp!</p>
                    <p style="margin-top: 20px;">
                        <small>🔒 Conexão segura via OAuth2 oficial do Google</small>
                    </p>
                </div>
            </div>
        </body>
        </html>
        """, 200


@webhooks_bp.route('/disconnect-calendar/<int:usuario_id>', methods=['POST'])
@handle_errors(tag="OAUTH_DISCONNECT")
def disconnect_calendar(usuario_id):
    """
    Permite usuário desconectar Google Calendar.
    Pode ser chamado via WhatsApp ou interface web.

    Fase A: Refatorado com decorators (economia: ~5 linhas)
    - @handle_errors: Tratamento de exceções automático
    """
    # Autenticar (verificar se é o próprio usuário)
    # ... adicionar autenticação se necessário ...

    GoogleCalendarOAuthService.revoke_access(usuario_id)

    return jsonify({
        "status": "sucesso",
        "mensagem": "Google Calendar desconectado com sucesso"
    }), 200
