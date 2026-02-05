# Upload de Arquivos para Google Drive via WhatsApp

## Visao Geral

Esta funcionalidade permite que usuarios enviem imagens e documentos diretamente para o Google Drive atraves do WhatsApp, especificando a pasta de destino na mensagem.

---

## Fluxo de Uso

```
1. Usuario envia imagem/documento com caption:
   "salvar no drive pasta Notas Fiscais"

2. Bot Baileys recebe media + caption

3. Webhook envia para Flask API

4. Sistema processa:
   - Classifica intent como "Upload Drive"
   - Verifica conexao Google Drive
   - Extrai nome da pasta do texto
   - Valida arquivo (tipo, tamanho)
   - Faz upload para pasta no Drive

5. Bot responde com link do arquivo
```

---

## Configuracao do Bot WhatsApp (Baileys)

### Campos Obrigatorios no Webhook

Quando o bot receber uma mensagem com midia, deve enviar os seguintes campos no JSON para o endpoint `/webhook-whatsapp`:

```json
{
  "texto": "salvar no drive pasta Notas Fiscais",
  "numero_remetente": "5511999999999@s.whatsapp.net",
  "media_data": "<conteudo do arquivo em base64>",
  "media_type": "image/jpeg",
  "media_filename": "foto_20240115.jpg"
}
```

### Descricao dos Campos

| Campo | Tipo | Obrigatorio | Descricao |
|-------|------|-------------|-----------|
| `texto` | string | Sim | Texto da mensagem (caption da midia) |
| `numero_remetente` | string | Sim | Numero do WhatsApp no formato `numero@s.whatsapp.net` |
| `media_data` | string | Sim* | Conteudo do arquivo codificado em Base64 |
| `media_type` | string | Sim* | Tipo MIME do arquivo (ex: `image/jpeg`, `application/pdf`) |
| `media_filename` | string | Nao | Nome do arquivo (padrao: "arquivo") |

*Obrigatorio quando a mensagem contem midia

### Exemplo de Implementacao (Baileys/Node.js)

```javascript
// Ao receber mensagem com midia
sock.ev.on('messages.upsert', async ({ messages }) => {
    const msg = messages[0];

    if (msg.message?.imageMessage || msg.message?.documentMessage) {
        // Extrair dados da midia
        const mediaMessage = msg.message.imageMessage || msg.message.documentMessage;

        // Baixar o arquivo
        const buffer = await downloadMediaMessage(msg, 'buffer', {});

        // Converter para base64
        const base64Data = buffer.toString('base64');

        // Preparar payload para o webhook
        const payload = {
            texto: mediaMessage.caption || '',
            numero_remetente: msg.key.remoteJid,
            media_data: base64Data,
            media_type: mediaMessage.mimetype,
            media_filename: mediaMessage.fileName || `arquivo_${Date.now()}`
        };

        // Enviar para o webhook Flask
        await axios.post('https://seu-backend.com/webhook-whatsapp', payload, {
            headers: {
                'Content-Type': 'application/json',
                'x-api-key': 'SUA_API_KEY',
                'X-Webhook-Signature': calcularHMAC(JSON.stringify(payload))
            }
        });
    }
});
```

---

## Tipos de Arquivo Permitidos

### Imagens
- `image/jpeg` (.jpg, .jpeg)
- `image/png` (.png)
- `image/gif` (.gif)
- `image/webp` (.webp)
- `image/bmp` (.bmp)

### Documentos
- `application/pdf` (.pdf)
- `application/msword` (.doc)
- `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (.docx)
- `application/vnd.ms-excel` (.xls)
- `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` (.xlsx)
- `application/vnd.ms-powerpoint` (.ppt)
- `application/vnd.openxmlformats-officedocument.presentationml.presentation` (.pptx)
- `text/plain` (.txt)
- `text/csv` (.csv)

### Audio
- `audio/mpeg` (.mp3)
- `audio/ogg` (.ogg)
- `audio/opus` (.opus)

### Limite de Tamanho
- **Maximo: 10MB**

---

## Frases Reconhecidas

O sistema reconhece variacoes como:

- "salvar no drive pasta X"
- "guardar no drive em X"
- "enviar para o drive na pasta X"
- "subir pro drive pasta X"
- "salva isso no drive"

Se nenhuma pasta for especificada, usa a pasta padrao: **"WhatsApp Uploads"**

---

## Respostas do Sistema

### Sucesso
```
✅ *Arquivo enviado para o Google Drive!*

📁 *Pasta:* Notas Fiscais
📄 *Arquivo:* foto_20240115.jpg

🔗 *Abrir no Drive:*
https://drive.google.com/file/d/xxx/view
```

### Erro: Sem arquivo
```
📎 Nenhum arquivo recebido.

Para salvar no Google Drive, envie uma *imagem* ou *documento* junto com a mensagem.

Exemplo:
[Envie uma foto]
_salvar no drive pasta Notas Fiscais_
```

### Erro: Drive nao conectado
```
🔗 *Google Drive não conectado*

Para usar esta funcionalidade, você precisa conectar sua conta Google.

Acesse as configurações e clique em *Conectar Google*.
```

### Erro: Tipo de arquivo nao permitido
```
❌ Tipo de arquivo não permitido. Aceitos: imagens (JPG, PNG, GIF), documentos (PDF, DOC, DOCX), planilhas (XLS, XLSX), texto (TXT, CSV), áudio (MP3, OGG)
```

### Erro: Arquivo muito grande
```
❌ Arquivo muito grande. Máximo permitido: 10MB
```

---

## OAuth2 - Reconexao Necessaria

Usuarios que ja conectaram o Google Calendar **antes** desta atualizacao precisarao reconectar sua conta Google para autorizar o escopo do Drive.

### Verificacao de Escopo

O sistema verifica automaticamente se o usuario tem o escopo `drive.file` autorizado. Se nao tiver, retorna mensagem solicitando reconexao.

### Escopos Solicitados
- `https://www.googleapis.com/auth/calendar` - Google Calendar
- `https://www.googleapis.com/auth/drive.file` - Google Drive (apenas arquivos criados pelo app)

---

## Arquitetura

### Arquivos Principais

| Arquivo | Descricao |
|---------|-----------|
| `app/services/google_drive_service.py` | Servico de upload e validacao |
| `app/services/google_calendar_oauth_service.py` | OAuth2 (Calendar + Drive) |
| `app/routes/webhooks/intents/drive_intents.py` | Intent handler para WhatsApp |
| `app/routes/webhooks/handlers/whatsapp_handler.py` | Webhook que processa mensagens |

### Fluxo Interno

```
whatsapp_handler.py
    |
    v
gemini_service.py (classifica intent)
    |
    v
drive_intents.py (UploadDriveIntent)
    |
    v
google_calendar_oauth_service.py (get_drive_service)
    |
    v
google_drive_service.py (upload_file)
    |
    v
Google Drive API
```

---

## Seguranca

- **Validacao de tipos MIME**: Apenas tipos permitidos na whitelist
- **Limite de tamanho**: Maximo 10MB por arquivo
- **Tokens criptografados**: OAuth tokens armazenados com Fernet
- **Escopo restrito**: `drive.file` permite apenas arquivos criados pelo app
- **Autenticacao**: Webhook protegido por API Key + HMAC signature
