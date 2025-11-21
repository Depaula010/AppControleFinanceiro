"""
Proxy simples para OAuth do Google Calendar
Redireciona requisições do Render (HTTPS) para servidor real (HTTP)
"""

from flask import Flask, request, redirect
import os

app = Flask(__name__)

# IP do servidor real (configurar via variável de ambiente)
REAL_SERVER = os.environ.get('REAL_SERVER_URL', 'http://212.47.65.37:8000')

@app.route('/connect-calendar/<int:usuario_id>')
def connect_calendar(usuario_id):
    """Redireciona para o servidor real para iniciar OAuth"""
    target_url = f"{REAL_SERVER}/connect-calendar/{usuario_id}"
    print(f"[PROXY] Redirecionando connect-calendar para: {target_url}")
    return redirect(target_url, code=302)

@app.route('/oauth2callback')
def oauth2callback():
    """Redireciona callback do Google para o servidor real"""
    # Pegar todos os query parameters (code, state, etc)
    query_string = request.query_string.decode('utf-8')
    target_url = f"{REAL_SERVER}/oauth2callback?{query_string}"

    print(f"[PROXY] Redirecionando oauth2callback para: {target_url}")
    return redirect(target_url, code=302)

@app.route('/health')
def health():
    """Health check para Render"""
    return {"status": "ok", "proxy_target": REAL_SERVER}, 200

@app.route('/')
def index():
    """Página inicial"""
    return f"""
    <h1>OAuth Proxy - Assistente Financeiro</h1>
    <p>Este serviço redireciona requisições OAuth do Google para o servidor real.</p>
    <p><strong>Servidor de destino:</strong> {REAL_SERVER}</p>
    <p><strong>Endpoints disponíveis:</strong></p>
    <ul>
        <li><code>/connect-calendar/&lt;usuario_id&gt;</code> - Inicia OAuth</li>
        <li><code>/oauth2callback</code> - Callback do Google</li>
        <li><code>/health</code> - Health check</li>
    </ul>
    """, 200

if __name__ == '__main__':
    port = int(os.environ.get('PORT', 10000))
    app.run(host='0.0.0.0', port=port)
