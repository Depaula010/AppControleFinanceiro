# 🗺️ Manual: Como Gerar Chave do OpenRouteService

## 📋 Índice

- [O que é o OpenRouteService?](#o-que-é-o-openrouteservice)
- [Por que usar sua própria chave?](#por-que-usar-sua-própria-chave)
- [Passo a Passo](#passo-a-passo)
- [Configurando no Sistema](#configurando-no-sistema)
- [Limites e Preços](#limites-e-preços)
- [Solução de Problemas](#solução-de-problemas)

---

## 🚗 O que é o OpenRouteService?

O **OpenRouteService** é o serviço de rotas e mapas usado pelo Meu Secretário para:

- 🕐 Calcular tempo de deslocamento até eventos
- 📍 Converter endereços em coordenadas (geocoding)
- 🚗 Sugerir melhor horário de saída
- 📊 Incluir tempo de viagem no resumo matinal
- 🗺️ Calcular distância entre locais

---

## 💰 Por que usar sua própria chave?

| Chave Própria | Chave do Sistema |
|---------------|------------------|
| ✅ **Gratuito** (até 2.000 req/dia) | ❌ Cobrado conforme seu plano |
| ✅ **Ilimitado** (sem contadores) | ❌ Limite mensal por plano |
| ✅ Controle total | ⚠️ Sujeito a disponibilidade |
| ⏱️ 5 minutos para configurar | ✅ Já funcionando |

**Recomendação:** Use sua própria chave! O plano gratuito é mais do que suficiente.

---

## 📝 Passo a Passo

### **1. Acesse o Site do OpenRouteService**

Abra seu navegador e acesse:

👉 [https://openrouteservice.org/dev/#/signup](https://openrouteservice.org/dev/#/signup)

![OpenRouteService Homepage](https://i.imgur.com/placeholder-ors-home.png)

---

### **2. Crie sua Conta Gratuita**

Preencha o formulário de cadastro:

| Campo | O que preencher |
|-------|-----------------|
| **Email** | Seu melhor e-mail |
| **Username** | Nome de usuário (ex: joao_silva) |
| **Password** | Senha forte (mín. 8 caracteres) |
| **Confirm Password** | Repita a senha |

![Formulário de Cadastro](https://i.imgur.com/placeholder-ors-signup.png)

✅ Marque as caixas:
- **"I have read and agree to the Terms of Service"**
- **"I am not a robot"** (reCAPTCHA)

Clique em **"Sign Up"**

---

### **3. Confirme seu Email**

1. Acesse seu email
2. Procure por email de **"OpenRouteService"**
3. Clique no link **"Verify Account"**
4. Você será redirecionado para a página de login

⚠️ **Importante:** Verifique a pasta de SPAM se não encontrar o email.

---

### **4. Faça Login**

Acesse a página de login:

👉 [https://openrouteservice.org/dev/#/login](https://openrouteservice.org/dev/#/login)

Use seu **username** (não email) e senha.

---

### **5. Crie um Token de API**

Após fazer login, você será direcionado ao dashboard.

**Etapas:**

1. Clique na aba **"TOKENS"** no menu superior
2. Clique no botão **"CREATE TOKEN"** ou **"REQUEST A TOKEN"**

![Dashboard Tokens](https://i.imgur.com/placeholder-ors-tokens.png)

3. Preencha o formulário:

| Campo | O que preencher |
|-------|-----------------|
| **Token name** | Nome descritivo (ex: "Meu Secretário") |
| **Select API** | Selecione **"Standard"** |
| **Restrictions** | Deixe em branco (sem restrições) |

4. Clique em **"CREATE TOKEN"**

---

### **6. Copie sua Chave de API**

Após criar o token, ele aparecerá na lista:

![Token Criado](https://i.imgur.com/placeholder-ors-key.png)

**Sua chave terá formato semelhante a:**

```
5b3ce3597851110001cf6248a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8
```

(58 caracteres alfanuméricos)

⚠️ **IMPORTANTE:**
- **NÃO compartilhe** sua chave com ninguém
- **NÃO publique** em GitHub, redes sociais, etc.
- **Guarde** em local seguro
- Esta é a **única vez** que verá a chave completa

Clique no ícone de **copiar** ou selecione e copie manualmente (Ctrl+C).

**⚠️ ATENÇÃO:** Se perder a chave, terá que criar um novo token.

---

## ⚙️ Configurando no Sistema

### **Método 1: Via Dashboard (Recomendado)**

1. Acesse o dashboard do Meu Secretário
2. Vá em **Configurações** → **Chaves de API**
3. Cole sua chave no campo **"OpenRouteService"**
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
    "provedor": "openroute",
    "chave_api": "5b3ce3597851110001cf6248a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8"
  }'
```

Depois configure a preferência:

```bash
curl -X POST https://seu-dominio.com/api-keys/preferencias/123 \
  -H "X-API-KEY: sua-chave-secreta" \
  -H "Content-Type: application/json" \
  -d '{
    "provedor": "openroute",
    "usar_chave_propria": true
  }'
```

---

### **Método 3: Via WhatsApp**

Envie para o bot:

```
Configurar chave OpenRoute: 5b3ce3597851110001cf6248a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8
```

O sistema responderá confirmando o cadastro.

---

## 💵 Limites e Preços

### **Plano Gratuito (Standard)**

O OpenRouteService oferece um plano gratuito generoso:

| Recurso | Limite Grátis | Observações |
|---------|---------------|-------------|
| **Requisições por dia** | 2.000 | Por chave de API |
| **Requisições por minuto** | 40 | Rate limit |
| **Serviços disponíveis** | Todos | Rotas, geocoding, isócronas |
| **Países** | Mundial | Qualquer lugar |

**Para o uso normal do Meu Secretário:**
- 📊 Média de 3-10 requisições/dia
- ✅ **Completamente grátis** para 99% dos usuários
- 🎉 Muito difícil exceder o limite

### **Se Precisar de Mais**

Você pode:

1. **Criar múltiplos tokens** (1 por aplicação)
   - Cada token tem seu próprio limite de 2.000/dia
2. **Contactar OpenRouteService** para aumento de quota
3. **Usar servidor próprio** (self-hosted, grátis)

---

## 🔍 Verificando o Uso

Para ver quanto você está usando:

1. Acesse [Dashboard do OpenRouteService](https://openrouteservice.org/dev/#/home)
2. Faça login
3. Clique na aba **"TOKENS"**
4. Veja a coluna **"Requests"** ao lado do seu token

![Uso do Token](https://i.imgur.com/placeholder-ors-usage.png)

O contador mostra:
- **Requests today:** Requisições de hoje
- **Requests total:** Total desde criação

---

## 🛡️ Segurança da Chave

### **Boas Práticas:**

✅ **O que fazer:**
- Usar chave apenas em aplicações de backend (servidor)
- Criar tokens separados para cada aplicação
- Nomear tokens de forma descritiva
- Monitorar uso no dashboard
- Deletar tokens não utilizados

❌ **O que NÃO fazer:**
- Compartilhar chave com terceiros
- Publicar chave em código fonte público
- Usar chave em aplicações frontend (navegador, app mobile)
- Usar mesma chave em múltiplas aplicações

### **Se sua chave vazou:**

1. Acesse o [Dashboard](https://openrouteservice.org/dev/#/home)
2. Vá na aba **"TOKENS"**
3. Encontre o token comprometido
4. Clique no ícone de **lixeira** (Delete)
5. Crie um novo token
6. Atualize no sistema

---

## 🗺️ Serviços Disponíveis

O OpenRouteService oferece vários serviços (todos gratuitos):

### **1. Directions (Rotas)**
Calcula rota entre dois pontos:
- 🚗 Carro
- 🚴 Bicicleta
- 🚶 A pé
- 🏍️ Moto

**Informações retornadas:**
- ⏱️ Tempo estimado
- 📏 Distância
- 🛣️ Rota detalhada

### **2. Geocoding**
Converte endereço em coordenadas:
- `"Av Paulista 1000, São Paulo"` → `-23.5617, -46.6558`

### **3. Reverse Geocoding**
Converte coordenadas em endereço:
- `-23.5617, -46.6558` → `"Av Paulista 1000, São Paulo, SP"`

### **4. Isochrones**
Calcula área alcançável em X minutos:
- Exemplo: "Onde posso chegar em 30 minutos de carro?"

---

## ❓ Solução de Problemas

### **Problema 1: Erro "Invalid API key"**

**Causa:** Chave incorreta ou não autorizada

**Solução:**
1. Verifique se copiou a chave completa (58 caracteres)
2. Não pode ter espaços no início/fim
3. Verifique se o token está ativo no dashboard
4. Confirme que verificou seu email de cadastro

---

### **Problema 2: Erro "403 Forbidden"**

**Causa:** Token não tem permissão para o serviço

**Solução:**
1. Acesse o dashboard
2. Verifique se criou token tipo **"Standard"**
3. Se criou token **"Free"** antigo, crie um novo tipo Standard

---

### **Problema 3: Erro "429 Too Many Requests"**

**Causa:** Atingiu limite de 2.000 requisições/dia ou 40/minuto

**Solução:**
- **Se atingiu 40 req/min:** Aguarde 1 minuto
- **Se atingiu 2.000 req/dia:**
  - Aguarde até o próximo dia (reseta à meia-noite UTC)
  - OU crie um novo token
  - OU use chave do sistema temporariamente

---

### **Problema 4: "Address not found" (Geocoding)**

**Causa:** Endereço muito genérico ou incorreto

**Solução:**
- Seja mais específico no endereço
- Adicione cidade e estado
- Use formato: `"Rua/Av, Número, Bairro, Cidade, Estado"`

**Exemplos:**

❌ Ruim:
- `"Paulista"`
- `"Shopping"`

✅ Bom:
- `"Av Paulista 1000, São Paulo, SP"`
- `"Shopping Iguatemi, Campinas, SP"`

---

### **Problema 5: Token sumiu do dashboard**

**Causa:** Conta inativa ou não verificada

**Solução:**
1. Verifique email de confirmação
2. Faça login novamente
3. Se problema persistir, crie novo token

---

## 🧪 Testando sua Chave

Você pode testar sua chave diretamente com curl:

### **Teste 1: Geocoding (endereço → coordenadas)**

```bash
curl "https://api.openrouteservice.org/geocode/search?api_key=SUA_CHAVE&text=Av Paulista, Sao Paulo"
```

**Resposta esperada:**
```json
{
  "features": [
    {
      "geometry": {
        "coordinates": [-46.6558, -23.5617]
      },
      "properties": {
        "label": "Avenida Paulista, São Paulo, Brazil"
      }
    }
  ]
}
```

✅ Se viu isso, sua chave está funcionando!

### **Teste 2: Directions (tempo de viagem)**

```bash
curl -X POST \
  "https://api.openrouteservice.org/v2/directions/driving-car" \
  -H "Authorization: SUA_CHAVE" \
  -H "Content-Type: application/json" \
  -d '{
    "coordinates": [[-46.6558,-23.5617], [-46.6336,-23.5505]]
  }'
```

**Resposta esperada:**
```json
{
  "features": [{
    "properties": {
      "summary": {
        "distance": 2500,
        "duration": 420
      }
    }
  }]
}
```

- `duration`: Tempo em segundos (420s = 7 minutos)
- `distance`: Distância em metros (2.5 km)

---

## 📚 Recursos Adicionais

### **Links Úteis:**

- 📖 [Documentação Oficial](https://openrouteservice.org/dev/#/api-docs)
- 🔑 [Gerenciar Tokens](https://openrouteservice.org/dev/#/home)
- 🗺️ [Playground Interativo](https://maps.openrouteservice.org/)
- ❓ [FAQ do OpenRouteService](https://ask.openrouteservice.org/)
- 💬 [Fórum da Comunidade](https://ask.openrouteservice.org/)

### **Exemplos de Uso:**

- 🚗 [Calcular Rotas](https://openrouteservice.org/dev/#/api-docs/v2/directions)
- 📍 [Geocoding](https://openrouteservice.org/dev/#/api-docs/geocode)
- ⏱️ [Isócronas](https://openrouteservice.org/dev/#/api-docs/v2/isochrones)

---

## 🤝 Precisa de Ajuda?

### **Suporte do Meu Secretário:**

- 💬 **WhatsApp:** (31) 9400-1072
- 📧 **Email:** suporte@meusecretario.com
- 🌐 **Dashboard:** Acesse "Ajuda" no menu

### **Suporte do OpenRouteService:**

- 💬 [Fórum da Comunidade](https://ask.openrouteservice.org/)
- 📧 **Email:** support@openrouteservice.org
- 🐙 [GitHub Issues](https://github.com/GIScience/openrouteservice/issues)

---

## ✅ Checklist Final

Antes de finalizar, confirme:

- [ ] Criei minha conta no OpenRouteService
- [ ] Confirmei meu email de cadastro
- [ ] Criei um token Standard
- [ ] Copiei a chave completa (58 caracteres)
- [ ] Guardei a chave em local seguro
- [ ] Cadastrei a chave no sistema
- [ ] Configurei para usar minha própria chave
- [ ] Testei com curl ou no browser
- [ ] Recebi tempo de viagem no resumo matinal

---

## 🗺️ Exemplo de Uso no Resumo Matinal

Quando configurado corretamente, você verá:

```
🌤️ Bom dia!

📅 Hoje, 04 de Dezembro

📆 Sua Agenda:
• 09:00 - Reunião com cliente
  📍 Av Paulista, 1000 - São Paulo
  🚗 Saia às 08:30 (30 min de viagem)

• 14:00 - Academia
  📍 Shopping Iguatemi
  🚗 Saia às 13:45 (15 min de viagem)

Tenha um ótimo dia! ☀️
```

---

**🎉 Parabéns!** Você agora tem acesso a cálculo de rotas gratuito!

Com 2.000 requisições/dia, você não precisará se preocupar com limites.

---

## 🌍 Curiosidade: OpenStreetMap

O OpenRouteService usa dados do **OpenStreetMap** (OSM), um mapa colaborativo e gratuito mantido por milhões de voluntários ao redor do mundo.

Por ser open source e gratuito, você pode:
- ✅ Usar sem restrições comerciais
- ✅ Acessar dados de qualquer lugar do mundo
- ✅ Contribuir adicionando/corrigindo mapas

Mais info: [openstreetmap.org](https://www.openstreetmap.org/)

---

**Última Atualização:** 04/12/2025
**Versão:** 1.0
