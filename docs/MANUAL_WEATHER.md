# ⛅ Manual: Como Gerar Chave do WeatherAPI

## 📋 Índice

- [O que é o WeatherAPI?](#o-que-é-o-weatherapi)
- [Por que usar sua própria chave?](#por-que-usar-sua-própria-chave)
- [Passo a Passo](#passo-a-passo)
- [Configurando no Sistema](#configurando-no-sistema)
- [Limites e Preços](#limites-e-preços)
- [Solução de Problemas](#solução-de-problemas)

---

## 🌤️ O que é o WeatherAPI?

O **WeatherAPI** é o serviço de clima usado pelo Meu Secretário para:

- 🌡️ Mostrar clima no resumo matinal
- ☔ Alertar sobre chuva antes de eventos
- 🌤️ Prever condições climáticas para sua agenda
- 📊 Incluir temperatura em relatórios

---

## 💰 Por que usar sua própria chave?

| Chave Própria | Chave do Sistema |
|---------------|------------------|
| ✅ **Gratuito** (até 1 milhão req/mês) | ❌ Cobrado conforme seu plano |
| ✅ **Ilimitado** (sem contadores) | ❌ Limite mensal por plano |
| ✅ Controle total | ⚠️ Sujeito a disponibilidade |
| ⏱️ 3 minutos para configurar | ✅ Já funcionando |

**Recomendação:** Use sua própria chave! O plano gratuito é mais do que suficiente.

---

## 📝 Passo a Passo

### **1. Acesse o Site do WeatherAPI**

Abra seu navegador e acesse:

👉 [https://www.weatherapi.com/signup.aspx](https://www.weatherapi.com/signup.aspx)

![WeatherAPI Homepage](https://i.imgur.com/placeholder-weather-home.png)

---

### **2. Crie sua Conta Gratuita**

Preencha o formulário de cadastro:

| Campo | O que preencher |
|-------|-----------------|
| **Name** | Seu nome completo |
| **Email** | Seu melhor e-mail |
| **Password** | Senha forte (mín. 8 caracteres) |

![Formulário de Cadastro](https://i.imgur.com/placeholder-weather-signup.png)

✅ Marque a caixa **"I agree to the Terms and Conditions"**

Clique em **"Sign Up"**

---

### **3. Confirme seu Email**

1. Acesse seu email
2. Procure por email de **"WeatherAPI.com"**
3. Clique no link de confirmação
4. Você será redirecionado para o dashboard

⚠️ **Importante:** Verifique a pasta de SPAM se não encontrar o email.

---

### **4. Acesse o Dashboard**

Após confirmar o email, faça login:

👉 [https://www.weatherapi.com/login.aspx](https://www.weatherapi.com/login.aspx)

Use seu email e senha cadastrados.

---

### **5. Copie sua Chave de API**

No dashboard, você verá sua chave automaticamente gerada:

![Dashboard com Chave](https://i.imgur.com/placeholder-weather-key.png)

**Sua chave terá formato semelhante a:**

```
a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6
```

(32 caracteres alfanuméricos)

⚠️ **IMPORTANTE:**
- **NÃO compartilhe** sua chave com ninguém
- **NÃO publique** em GitHub, redes sociais, etc.
- **Guarde** em local seguro

Clique no ícone de **copiar** ou selecione e copie manualmente (Ctrl+C).

---

## ⚙️ Configurando no Sistema

### **Método 1: Via Dashboard (Recomendado)**

1. Acesse o dashboard do Meu Secretário
2. Vá em **Configurações** → **Chaves de API**
3. Cole sua chave no campo **"WeatherAPI"**
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
    "provedor": "weather",
    "chave_api": "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6"
  }'
```

Depois configure a preferência:

```bash
curl -X POST https://seu-dominio.com/api-keys/preferencias/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "provedor": "weather",
    "usar_chave_propria": true
  }'
```

---

### **Método 3: Via WhatsApp**

Envie para o bot:

```
Configurar chave Weather: a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6
```

O sistema responderá confirmando o cadastro.

---

## 💵 Limites e Preços

### **Plano Gratuito (Free Tier)**

O WeatherAPI oferece um plano gratuito muito generoso:

| Recurso | Limite Grátis | Observações |
|---------|---------------|-------------|
| **Requisições por mês** | 1.000.000 | 1 milhão! |
| **Requisições por dia** | ~33.000 | Mais que suficiente |
| **Dados disponíveis** | Clima atual + Previsão 3 dias | Completo |
| **Cidades** | Ilimitadas | Qualquer lugar do mundo |

**Para o uso normal do Meu Secretário:**
- 📊 Média de 2-5 requisições/dia
- ✅ **Completamente grátis** para 100% dos usuários
- 🎉 Impossível exceder o limite com uso normal

### **Planos Pagos (Opcional)**

Apenas se você precisar de recursos avançados:

| Plano | Preço | Recursos Extras |
|-------|-------|-----------------|
| **Pro** | $4/mês | Previsão 14 dias, dados históricos |
| **Business** | $35/mês | API de alta performance |

💡 **Dica:** O plano gratuito é mais do que suficiente para o Meu Secretário.

---

## 🔍 Verificando o Uso

Para ver quanto você está usando:

1. Acesse [WeatherAPI Dashboard](https://www.weatherapi.com/my/)
2. Veja o painel **"API Usage"**
3. Acompanhe requisições diárias/mensais

![Dashboard de Uso](https://i.imgur.com/placeholder-weather-usage.png)

---

## 🛡️ Segurança da Chave

### **Boas Práticas:**

✅ **O que fazer:**
- Usar chave apenas em aplicações de backend (servidor)
- Manter chave em variáveis de ambiente
- Monitorar uso no dashboard
- Rotacionar chave a cada 6 meses (opcional)

❌ **O que NÃO fazer:**
- Compartilhar chave com terceiros
- Publicar chave em código fonte público
- Usar chave em aplicações frontend (navegador, app mobile)

### **Se sua chave vazou:**

1. Acesse o [Dashboard](https://www.weatherapi.com/my/)
2. Vá em **"API Key"** → **"Manage"**
3. Clique em **"Regenerate Key"**
4. Copie a nova chave
5. Atualize no sistema

---

## 🌍 Cidades Suportadas

O WeatherAPI suporta **qualquer cidade do mundo**:

### **Formato de Busca:**

1. **Por nome da cidade:**
   - `São Paulo`
   - `Rio de Janeiro`
   - `Campinas`

2. **Por cidade + estado:**
   - `São Paulo, SP`
   - `Campinas, São Paulo`

3. **Por coordenadas (lat, lon):**
   - `-23.5505,-46.6333` (São Paulo)

4. **Por CEP:**
   - `01310-100`

5. **Por código aeroporto:**
   - `GRU` (Guarulhos)

---

## ❓ Solução de Problemas

### **Problema 1: Erro "API key not valid"**

**Causa:** Chave incorreta ou inválida

**Solução:**
1. Verifique se copiou a chave completa (32 caracteres)
2. Não pode ter espaços no início/fim
3. Verifique se confirmou o email de cadastro
4. Tente fazer login no dashboard para confirmar que a conta está ativa

---

### **Problema 2: Erro "API key disabled"**

**Causa:** Conta não foi confirmada por email

**Solução:**
1. Verifique seu email (inclusive SPAM)
2. Procure por email de confirmação da WeatherAPI
3. Clique no link de ativação
4. Aguarde 5 minutos e tente novamente

---

### **Problema 3: Erro "Location not found"**

**Causa:** Cidade não encontrada ou nome incorreto

**Solução:**
- Use formato: `"São Paulo, Brazil"`
- Adicione nome do país para cidades pequenas
- Verifique ortografia (acentos importam)

**Exemplos corretos:**
- ✅ `Belo Horizonte, Brazil`
- ✅ `Campinas, SP`
- ❌ `BH` (muito genérico)

---

### **Problema 4: Dados de clima não aparecem**

**Causa:** Preferência não configurada

**Solução:**
1. Cadastre a chave no sistema
2. **Configure a preferência** para usar sua chave
3. Envie `Bom dia` no WhatsApp para testar

---

### **Problema 5: Erro "Request limit exceeded"**

**Causa:** Atingiu limite de 1 milhão/mês (improvável)

**Solução:**
- Aguarde até o próximo mês
- OU faça upgrade para plano pago
- OU use chave do sistema temporariamente

---

## 🧪 Testando sua Chave

Você pode testar sua chave diretamente no navegador:

**Formato da URL:**
```
https://api.weatherapi.com/v1/current.json?key=SUA_CHAVE&q=Sao Paulo&lang=pt
```

**Substitua:**
- `SUA_CHAVE` pela sua chave real
- `Sao Paulo` pela cidade desejada

Cole no navegador e pressione Enter. Você deve ver um JSON com dados do clima:

```json
{
  "location": {
    "name": "Sao Paulo",
    "region": "Sao Paulo",
    "country": "Brazil",
    "localtime": "2025-12-04 11:30"
  },
  "current": {
    "temp_c": 24,
    "condition": {
      "text": "Parcialmente nublado"
    }
  }
}
```

✅ Se viu isso, sua chave está funcionando!

---

## 📚 Recursos Adicionais

### **Links Úteis:**

- 📖 [Documentação Oficial](https://www.weatherapi.com/docs/)
- 🔑 [Gerenciar sua Conta](https://www.weatherapi.com/my/)
- 💰 [Planos e Preços](https://www.weatherapi.com/pricing.aspx)
- ❓ [FAQ do WeatherAPI](https://www.weatherapi.com/faq.aspx)

### **Recursos da API:**

- 🌡️ Temperatura atual
- 💨 Vento (velocidade e direção)
- 💧 Umidade
- ☔ Chance de chuva
- 🌅 Nascer e pôr do sol
- 🌙 Fases da lua
- 📈 Previsão 3 dias (plano grátis)

---

## 🤝 Precisa de Ajuda?

### **Suporte do Meu Secretário:**

- 💬 **WhatsApp:** (31) 9400-1072
- 📧 **Email:** suporte@meusecretario.com
- 🌐 **Dashboard:** Acesse "Ajuda" no menu

### **Suporte do WeatherAPI:**

- 📧 **Email:** support@weatherapi.com
- 💬 [Contato no Site](https://www.weatherapi.com/contact.aspx)

---

## ✅ Checklist Final

Antes de finalizar, confirme:

- [ ] Criei minha conta no WeatherAPI
- [ ] Confirmei meu email de cadastro
- [ ] Copiei minha chave de API (32 caracteres)
- [ ] Cadastrei a chave no sistema
- [ ] Configurei para usar minha própria chave
- [ ] Testei a chave no navegador
- [ ] Recebi resumo matinal com clima

---

## 🌡️ Exemplo de Uso no Resumo Matinal

Quando configurado corretamente, você verá:

```
🌤️ Bom dia!

📅 Hoje, 04 de Dezembro

🌡️ Clima: 24°C, Parcialmente nublado ⛅

📆 Sua Agenda:
• 09:00 - Reunião com cliente
• 14:00 - Academia

Tenha um ótimo dia! ☀️
```

---

**🎉 Parabéns!** Você agora tem acesso a dados de clima gratuitos!

Com 1 milhão de requisições/mês, você não precisará se preocupar com limites.

---

**Última Atualização:** 04/12/2025
**Versão:** 1.0
