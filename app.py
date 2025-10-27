import os
from flask import Flask, request, jsonify

# Inicializa o aplicativo Flask
app = Flask(__name__)

@app.route('/')
def home():
    """Uma rota simples para sabermos que o servidor está no ar."""
    return "API do Bot Financeiro está no ar!"

@app.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    """
    Este endpoint vai receber a notificação do seu celular Android (Automate).
    """
    try:
        data = request.json
        texto_notificacao = data.get('texto')

        if not texto_notificacao:
            print("Erro: JSON recebido mas sem a chave 'texto'.")
            return jsonify({"status": "erro", "mensagem": "Chave 'texto' faltando"}), 400

        # Por enquanto, vamos apenas imprimir no log para ver se funcionou
        print(f"[AUTOMATE] Notificação Recebida: {texto_notificacao}")

        # ----- FUTURO -----
        # Aqui é onde você vai:
        # 1. Chamar a API do Gemini para processar o texto
        # 2. Salvar o resultado no banco de dados
        # 3. Chamar o bot do WhatsApp para te avisar
        # ------------------

        return jsonify({"status": "sucesso"}), 200

    except Exception as e:
        print(f"Erro no webhook do Automate: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500

@app.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """
    Este endpoint vai receber as mensagens que VOCÊ envia
    para o seu bot no WhatsApp (Ex: "gastei 50 reais na padaria").
    """
    try:
        data = request.json
        texto_msg = data.get('texto')

        if not texto_msg:
            print("Erro: JSON recebido do WhatsApp mas sem a chave 'texto'.")
            return jsonify({"status": "erro", "mensagem": "Chave 'texto' faltando"}), 400

        print(f"[WHATSAPP] Mensagem Recebida: {texto_msg}")

        # ----- FUTURO -----
        # Aqui é onde você vai:
        # 1. Chamar a API do Gemini para processar o texto
        # 2. Salvar o resultado no banco de dados
        # 3. Chamar o bot do WhatsApp para te responder "OK, salvo!"
        # ------------------

        return jsonify({"status": "sucesso"}), 200

    except Exception as e:
        print(f"Erro no webhook do WhatsApp: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500

if __name__ == "__main__":
    # O Render não vai usar isso, mas é bom para testes locais
    port = int(os.environ.get("PORT", 5000))
    app.run(host='0.0.0.0', port=port)