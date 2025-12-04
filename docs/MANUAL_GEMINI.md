# 🤖 Manual: Como Gerar Chave do Google Gemini AI

## 📋 Índice

- [O que é o Google Gemini?](#o-que-é-o-google-gemini)
- [Por que usar sua própria chave?](#por-que-usar-sua-própria-chave)
- [Passo a Passo](#passo-a-passo)
- [Configurando no Sistema](#configurando-no-sistema)
- [Limites e Preços](#limites-e-preços)
- [Solução de Problemas](#solução-de-problemas)

---

## 🎯 O que é o Google Gemini?

O **Google Gemini** é a inteligência artificial do Google usada pelo Meu Secretário para:

- 📝 Categorizar automaticamente suas transações
- 🗣️ Entender comandos em linguagem natural
- 📊 Gerar relatórios e análises inteligentes
- 📅 Processar informações da sua agenda
- 💬 Criar resumos matinais personalizados

---

## 💰 Por que usar sua própria chave?

| Chave Própria | Chave do Sistema |
|---------------|------------------|
| ✅ **Gratuito** (até 1.500 req/dia) | ❌ Cobrado conforme seu plano |
| ✅ **Ilimitado** (sem contadores) | ❌ Limite mensal por plano |
| ✅ Controle total | ⚠️ Sujeito a disponibilidade |
| ⏱️ 5 minutos para configurar | ✅ Já funcionando |

**Recomendação:** Use sua própria chave! É grátis e você tem controle total.

---

## 📝 Passo a Passo

### **1. Acesse o Google AI Studio**

Abra seu navegador e acesse:

👉 [https://aistudio.google.com/app/apikey](https://aistudio.google.com/app/apikey)

![Google AI Studio](https://i.imgur.com/placeholder-aistudio.png)

---

### **2. Faça Login com sua Conta Google**

Use qualquer conta Google (Gmail, Workspace, etc.).

Se não tiver conta, crie uma gratuita em [accounts.google.com](https://accounts.google.com).

---

### **3. Aceite os Termos de Uso**

Na primeira vez, você precisará aceitar os Termos de Serviço do Google AI.

✅ Leia e aceite os termos

---

### **4. Crie uma Nova Chave de API**

Clique no botão **"Get API key"** ou **"Create API key"**.

![Criar Chave](https://i.imgur.com/placeholder-create-key.png)

Você terá duas opções:

#### **Opção A: Criar em novo projeto (Recomendado)**

1. Clique em **"Create API key in new project"**
2. A chave será criada automaticamente
3. Pronto! Sua chave está disponível

#### **Opção B: Criar em projeto existente**

1. Clique em **"Create API key in existing project"**
2. Selecione o projeto Google Cloud
3. A chave será criada

---

### **5. Copie sua Chave de API**

Sua chave terá formato semelhante a:

```
AIzaSyABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890
```

⚠️ **IMPORTANTE:**
- **NÃO compartilhe** sua chave com ninguém
- **NÃO publique** em GitHub, redes sociais, etc.
- **Guarde** em local seguro

![Copiar Chave](https://i.imgur.com/placeholder-copy-key.png)

Clique no ícone de **copiar** ou selecione e copie manualmente (Ctrl+C).

---

## ⚙️ Configurando no Sistema

### **Método 1: Via Dashboard (Recomendado)**

1. Acesse o dashboard do Meu Secretário
2. Vá em **Configurações** → **Chaves de API**
3. Cole sua chave no campo **"Google Gemini"**
4. Escolha **"Usar minha própria chave"**
5. Clique em **Salvar**

---

### **Método 2: Via API REST**

Se você é desenvolvedor, pode cadastrar via API:

```bash
curl -X POST https://seu-dominio.com/api-keys/usuario/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "provedor": "gemini",
    "chave_api": "AIzaSyABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890"
  }'
```

Depois configure a preferência:

```bash
curl -X POST https://seu-dominio.com/api-keys/preferencias/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "provedor": "gemini",
    "usar_chave_propria": true
  }'
```

---

### **Método 3: Via WhatsApp**

Envie para o bot:

```
Configurar chave Gemini: AIzaSyABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890
```

O sistema responderá confirmando o cadastro.

---

## 💵 Limites e Preços

### **Plano Gratuito (Free Tier)**

O Google Gemini oferece um plano gratuito generoso:

| Recurso | Limite Grátis | Preço se Exceder |
|---------|---------------|------------------|
| **Requisições por dia** | 1.500 | - |
| **Requisições por minuto** | 15 | - |
| **Tokens por dia** | ~32.000 | - |

**Para o uso normal do Meu Secretário:**
- 📊 Média de 50-100 requisições/dia
- ✅ **Completamente grátis** para 99% dos usuários

### **Se Exceder os Limites**

Você pode:

1. **Esperar até o próximo dia** (limites resetam às 00h UTC)
2. **Ativar o billing** no Google Cloud (paga apenas o que usar)
   - Preço: ~$0.00025 por requisição (muito barato)

---

## 🔍 Verificando o Uso

Para ver quanto você está usando:

1. Acesse [Google AI Studio](https://aistudio.google.com/app/apikey)
2. Clique na sua chave
3. Veja o gráfico de uso

![Uso da API](https://i.imgur.com/placeholder-usage.png)

---

## 🛡️ Segurança da Chave

### **Boas Práticas:**

✅ **O que fazer:**
- Usar chave apenas em aplicações de backend (servidor)
- Manter chave em variáveis de ambiente
- Rotacionar chave periodicamente (a cada 3-6 meses)

❌ **O que NÃO fazer:**
- Compartilhar chave com terceiros
- Publicar chave em código fonte público
- Usar chave em aplicações frontend (navegador, app mobile)

### **Se sua chave vazou:**

1. Acesse o [Google AI Studio](https://aistudio.google.com/app/apikey)
2. Clique nos 3 pontinhos ao lado da chave
3. Clique em **"Delete key"**
4. Crie uma nova chave
5. Atualize no sistema

---

## ❓ Solução de Problemas

### **Problema 1: Erro "API key not valid"**

**Causa:** Chave incorreta ou inválida

**Solução:**
1. Verifique se copiou a chave completa
2. Não pode ter espaços no início/fim
3. Formato correto: `AIzaSy...` (começa com "AIzaSy")

---

### **Problema 2: Erro "Quota exceeded"**

**Causa:** Atingiu o limite de 1.500 requisições/dia

**Solução:**
- **Opção 1:** Aguarde até o próximo dia (reseta às 00h UTC = 21h horário de Brasília)
- **Opção 2:** Troque para usar chave do sistema temporariamente
- **Opção 3:** Ative billing no Google Cloud

---

### **Problema 3: Erro "403 Forbidden"**

**Causa:** API não habilitada no projeto

**Solução:**
1. Acesse [Google Cloud Console](https://console.cloud.google.com)
2. Selecione seu projeto
3. Vá em **APIs & Services** → **Library**
4. Procure por **"Generative Language API"**
5. Clique em **Enable**

---

### **Problema 4: "Resource has been exhausted"**

**Causa:** Muitas requisições em curto período (rate limit)

**Solução:**
- Aguarde 1 minuto (limite é 15 req/min)
- O sistema automaticamente faz retry

---

## 📚 Recursos Adicionais

### **Links Úteis:**

- 📖 [Documentação Oficial do Gemini](https://ai.google.dev/docs)
- 🔑 [Gerenciar suas Chaves](https://aistudio.google.com/app/apikey)
- 💰 [Preços do Gemini](https://ai.google.dev/pricing)
- ❓ [FAQ do Google AI](https://ai.google.dev/docs/faq)

### **Vídeos Tutoriais:**

- 🎥 [Como criar chave do Gemini](https://youtube.com/watch?v=exemplo) *(em breve)*
- 🎥 [Configurando no Meu Secretário](https://youtube.com/watch?v=exemplo) *(em breve)*

---

## 🤝 Precisa de Ajuda?

### **Suporte do Meu Secretário:**

- 💬 **WhatsApp:** (31) 9400-1072
- 📧 **Email:** suporte@meusecretario.com
- 🌐 **Dashboard:** Acesse "Ajuda" no menu

### **Suporte do Google:**

- 📖 [Google AI Support](https://developers.google.com/support)
- 💬 [Comunidade Google Developers](https://developers.google.com/community)

---

## ✅ Checklist Final

Antes de finalizar, confirme:

- [ ] Criei minha conta no Google AI Studio
- [ ] Gerei minha chave de API
- [ ] Copiei a chave completa (formato: AIzaSy...)
- [ ] Cadastrei a chave no sistema
- [ ] Configurei para usar minha própria chave
- [ ] Testei enviando uma mensagem no WhatsApp
- [ ] Guardei a chave em local seguro

---

**🎉 Parabéns!** Você agora está usando o Google Gemini gratuitamente!

Aproveite o uso ilimitado e economize no seu plano do Meu Secretário.

---

**Última Atualização:** 04/12/2025
**Versão:** 1.0
