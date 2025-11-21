# 📱 Exemplo: Endpoint `/enviar-imagem` para Bot WhatsApp

## 🎯 Objetivo

Este documento fornece um exemplo de implementação do endpoint `/enviar-imagem` que deve ser adicionado ao seu bot WhatsApp para suportar o envio de gráficos.

---

## 📋 Especificação da API

### Endpoint

**POST** `/enviar-imagem`

### Headers

```
Content-Type: application/json
x-api-key: sua_api_key_secreta
```

### Request Body

```json
{
  "numero": "5531999999999",
  "imagem": "iVBORw0KGgoAAAANSUhEUgAA...",
  "legenda": "📊 Gastos por Categoria - Últimos 30 dias"
}
```

**Campos:**
- `numero` (string, obrigatório): Número do WhatsApp no formato internacional (apenas dígitos)
- `imagem` (string, obrigatório): Imagem em formato base64
- `legenda` (string, opcional): Texto que acompanha a imagem

### Response

**Sucesso (200 OK):**
```json
{
  "status": "sucesso",
  "mensagem": "Imagem enviada com sucesso"
}
```

**Erro (400 Bad Request):**
```json
{
  "status": "erro",
  "mensagem": "Campo 'numero' é obrigatório"
}
```

**Erro (401 Unauthorized):**
```json
{
  "status": "erro",
  "mensagem": "API key inválida"
}
```

**Erro (500 Internal Server Error):**
```json
{
  "status": "erro",
  "mensagem": "Erro ao enviar imagem: [detalhes]"
}
```

---

## 💻 Implementação (Node.js + Baileys)

### Exemplo usando Express + Baileys

```javascript
// routes/enviar-imagem.js
const express = require('express');
const router = express.Router();
const fs = require('fs');
const path = require('path');

// Middleware de autenticação
const authenticateApiKey = (req, res, next) => {
  const apiKey = req.headers['x-api-key'];
  const validApiKey = process.env.API_SECRET_KEY;

  if (!apiKey || apiKey !== validApiKey) {
    return res.status(401).json({
      status: 'erro',
      mensagem: 'API key inválida'
    });
  }

  next();
};

router.post('/enviar-imagem', authenticateApiKey, async (req, res) => {
  try {
    const { numero, imagem, legenda } = req.body;

    // Validações
    if (!numero) {
      return res.status(400).json({
        status: 'erro',
        mensagem: 'Campo "numero" é obrigatório'
      });
    }

    if (!imagem) {
      return res.status(400).json({
        status: 'erro',
        mensagem: 'Campo "imagem" é obrigatório'
      });
    }

    // Formatar número no formato do WhatsApp
    const numeroFormatado = `${numero}@s.whatsapp.net`;

    // Converter base64 para buffer
    const imageBuffer = Buffer.from(imagem, 'base64');

    // Salvar temporariamente (opcional)
    const tempPath = path.join(__dirname, '../temp', `img_${Date.now()}.png`);
    fs.writeFileSync(tempPath, imageBuffer);

    // Enviar via Baileys
    const sock = req.app.get('sock'); // Socket do Baileys

    await sock.sendMessage(numeroFormatado, {
      image: imageBuffer,
      caption: legenda || ''
    });

    // Remover arquivo temporário
    if (fs.existsSync(tempPath)) {
      fs.unlinkSync(tempPath);
    }

    console.log(`[BOT] ✅ Imagem enviada para ${numero}`);

    return res.status(200).json({
      status: 'sucesso',
      mensagem: 'Imagem enviada com sucesso'
    });

  } catch (error) {
    console.error(`[BOT] ❌ Erro ao enviar imagem: ${error}`);

    return res.status(500).json({
      status: 'erro',
      mensagem: `Erro ao enviar imagem: ${error.message}`
    });
  }
});

module.exports = router;
```

### Configurar no app principal

```javascript
// app.js ou index.js
const express = require('express');
const enviarImagemRoutes = require('./routes/enviar-imagem');

const app = express();

// Middlewares
app.use(express.json({ limit: '10mb' })); // Importante: aumentar limite para imagens

// Rotas
app.use('/', enviarImagemRoutes);

// ... resto do código
```

---

## 💻 Implementação (Python + Flask + Twilio)

### Exemplo usando Flask + Twilio API

```python
# routes/bot_routes.py
from flask import Blueprint, request, jsonify
import os
import base64
from twilio.rest import Client

bot_bp = Blueprint('bot', __name__)

# Configuração Twilio
TWILIO_ACCOUNT_SID = os.getenv('TWILIO_ACCOUNT_SID')
TWILIO_AUTH_TOKEN = os.getenv('TWILIO_AUTH_TOKEN')
TWILIO_WHATSAPP_NUMBER = os.getenv('TWILIO_WHATSAPP_NUMBER')
API_SECRET_KEY = os.getenv('API_SECRET_KEY')

twilio_client = Client(TWILIO_ACCOUNT_SID, TWILIO_AUTH_TOKEN)

def authenticate_api_key():
    """Valida API key do header."""
    api_key = request.headers.get('x-api-key')

    if not api_key or api_key != API_SECRET_KEY:
        return False

    return True


@bot_bp.route('/enviar-imagem', methods=['POST'])
def enviar_imagem():
    """
    Envia imagem via WhatsApp usando Twilio.
    """
    # Autenticação
    if not authenticate_api_key():
        return jsonify({
            'status': 'erro',
            'mensagem': 'API key inválida'
        }), 401

    # Validar payload
    data = request.get_json()

    numero = data.get('numero')
    imagem_base64 = data.get('imagem')
    legenda = data.get('legenda', '')

    if not numero:
        return jsonify({
            'status': 'erro',
            'mensagem': 'Campo "numero" é obrigatório'
        }), 400

    if not imagem_base64:
        return jsonify({
            'status': 'erro',
            'mensagem': 'Campo "imagem" é obrigatório'
        }), 400

    try:
        # Decodificar base64
        image_data = base64.b64decode(imagem_base64)

        # Salvar temporariamente
        temp_path = f'/tmp/whatsapp_img_{numero}.png'
        with open(temp_path, 'wb') as f:
            f.write(image_data)

        # Upload para servidor/CDN (Twilio precisa de URL pública)
        # Aqui você pode usar S3, Cloudinary, etc.
        image_url = upload_to_cdn(temp_path)  # Implementar conforme seu CDN

        # Formatar número WhatsApp
        numero_formatado = f'whatsapp:+{numero}'

        # Enviar via Twilio
        message = twilio_client.messages.create(
            from_=f'whatsapp:{TWILIO_WHATSAPP_NUMBER}',
            body=legenda,
            media_url=[image_url],
            to=numero_formatado
        )

        # Remover arquivo temporário
        if os.path.exists(temp_path):
            os.remove(temp_path)

        print(f'[BOT] ✅ Imagem enviada para {numero} (SID: {message.sid})')

        return jsonify({
            'status': 'sucesso',
            'mensagem': 'Imagem enviada com sucesso',
            'message_sid': message.sid
        }), 200

    except Exception as e:
        print(f'[BOT] ❌ Erro ao enviar imagem: {e}')

        return jsonify({
            'status': 'erro',
            'mensagem': f'Erro ao enviar imagem: {str(e)}'
        }), 500


def upload_to_cdn(file_path):
    """
    Faz upload da imagem para CDN e retorna URL pública.
    Implementar conforme seu provedor (S3, Cloudinary, etc.)
    """
    # EXEMPLO COM CLOUDINARY
    # import cloudinary.uploader
    # result = cloudinary.uploader.upload(file_path)
    # return result['secure_url']

    # EXEMPLO COM S3
    # import boto3
    # s3 = boto3.client('s3')
    # bucket_name = 'meu-bucket'
    # object_name = f'whatsapp/{os.path.basename(file_path)}'
    # s3.upload_file(file_path, bucket_name, object_name)
    # return f'https://{bucket_name}.s3.amazonaws.com/{object_name}'

    raise NotImplementedError('Implementar upload para CDN')
```

---

## 💻 Implementação (Python + Flask + WhatsApp Business API)

### Exemplo usando WhatsApp Business API (oficial)

```python
# routes/bot_routes.py
from flask import Blueprint, request, jsonify
import os
import base64
import requests
from io import BytesIO

bot_bp = Blueprint('bot', __name__)

WHATSAPP_API_URL = os.getenv('WHATSAPP_API_URL')  # Ex: https://graph.facebook.com/v18.0
WHATSAPP_PHONE_ID = os.getenv('WHATSAPP_PHONE_ID')
WHATSAPP_TOKEN = os.getenv('WHATSAPP_TOKEN')
API_SECRET_KEY = os.getenv('API_SECRET_KEY')


def authenticate_api_key():
    """Valida API key do header."""
    api_key = request.headers.get('x-api-key')
    return api_key == API_SECRET_KEY


@bot_bp.route('/enviar-imagem', methods=['POST'])
def enviar_imagem():
    """
    Envia imagem via WhatsApp Business API.
    """
    # Autenticação
    if not authenticate_api_key():
        return jsonify({
            'status': 'erro',
            'mensagem': 'API key inválida'
        }), 401

    # Validar payload
    data = request.get_json()
    numero = data.get('numero')
    imagem_base64 = data.get('imagem')
    legenda = data.get('legenda', '')

    if not numero or not imagem_base64:
        return jsonify({
            'status': 'erro',
            'mensagem': 'Campos "numero" e "imagem" são obrigatórios'
        }), 400

    try:
        # Decodificar base64
        image_data = base64.b64decode(imagem_base64)

        # 1. Upload da mídia para WhatsApp
        upload_url = f'{WHATSAPP_API_URL}/{WHATSAPP_PHONE_ID}/media'
        headers = {
            'Authorization': f'Bearer {WHATSAPP_TOKEN}'
        }
        files = {
            'file': ('chart.png', BytesIO(image_data), 'image/png'),
            'type': (None, 'image/png'),
            'messaging_product': (None, 'whatsapp')
        }

        upload_response = requests.post(upload_url, headers=headers, files=files)
        upload_response.raise_for_status()

        media_id = upload_response.json()['id']

        # 2. Enviar mensagem com imagem
        send_url = f'{WHATSAPP_API_URL}/{WHATSAPP_PHONE_ID}/messages'
        payload = {
            'messaging_product': 'whatsapp',
            'to': numero,
            'type': 'image',
            'image': {
                'id': media_id,
                'caption': legenda
            }
        }

        send_response = requests.post(send_url, headers=headers, json=payload)
        send_response.raise_for_status()

        print(f'[BOT] ✅ Imagem enviada para {numero}')

        return jsonify({
            'status': 'sucesso',
            'mensagem': 'Imagem enviada com sucesso',
            'media_id': media_id
        }), 200

    except Exception as e:
        print(f'[BOT] ❌ Erro ao enviar imagem: {e}')

        return jsonify({
            'status': 'erro',
            'mensagem': f'Erro ao enviar imagem: {str(e)}'
        }), 500
```

---

## 🧪 Teste do Endpoint

### Usando cURL

```bash
# 1. Preparar imagem em base64
BASE64_IMAGE=$(base64 -w 0 test_image.png)

# 2. Enviar requisição
curl -X POST http://localhost:5000/enviar-imagem \
  -H "Content-Type: application/json" \
  -H "x-api-key: sua_api_key" \
  -d '{
    "numero": "5531999999999",
    "imagem": "'"$BASE64_IMAGE"'",
    "legenda": "Teste de envio de imagem"
  }'
```

### Usando Python (requests)

```python
import requests
import base64

# Ler imagem
with open('test_image.png', 'rb') as f:
    image_data = f.read()
    image_base64 = base64.b64encode(image_data).decode('utf-8')

# Enviar
url = 'http://localhost:5000/enviar-imagem'
headers = {
    'Content-Type': 'application/json',
    'x-api-key': 'sua_api_key'
}
payload = {
    'numero': '5531999999999',
    'imagem': image_base64,
    'legenda': 'Teste de envio de imagem'
}

response = requests.post(url, headers=headers, json=payload)
print(response.json())
```

---

## ⚙️ Considerações de Produção

### Limites de Tamanho

- **WhatsApp:** Limite de ~16MB por imagem
- **Base64:** Aumenta tamanho em ~33%
- **Recomendação:** Configurar limite do Express/Flask para 10-15MB

```javascript
// Express
app.use(express.json({ limit: '10mb' }));
```

```python
# Flask
app.config['MAX_CONTENT_LENGTH'] = 10 * 1024 * 1024  # 10MB
```

### Performance

- Cache de imagens se possível
- Comprimir imagens antes de enviar
- Usar CDN para armazenamento temporário

### Segurança

- ✅ Sempre validar API key
- ✅ Validar formato base64
- ✅ Limitar taxa de requisições (rate limiting)
- ✅ Sanitizar número de telefone
- ✅ Remover arquivos temporários

---

## 📚 Referências

- [Baileys Documentation](https://github.com/WhiskeySockets/Baileys)
- [Twilio WhatsApp API](https://www.twilio.com/docs/whatsapp)
- [WhatsApp Business API](https://developers.facebook.com/docs/whatsapp)

---

## 🎯 Checklist de Implementação

- [ ] Endpoint `/enviar-imagem` implementado
- [ ] Autenticação via API key funcionando
- [ ] Validação de campos
- [ ] Conversão base64 → buffer
- [ ] Envio via biblioteca WhatsApp
- [ ] Tratamento de erros
- [ ] Logs de sucesso/erro
- [ ] Remoção de arquivos temporários
- [ ] Teste com cURL
- [ ] Teste com aplicação principal

---

Após implementar, teste usando:

```bash
python test_chart_generation.py --user-id 1
```

Ou pelo WhatsApp:

```
"gráfico de gastos"
```

Boa sorte! 🚀
