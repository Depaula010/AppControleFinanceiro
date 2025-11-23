# 🛡️ Guia de Deploy com Nginx - Contabo VPS

Este guia explica como fazer deploy da aplicação com Nginx como reverse proxy no seu servidor Contabo.

---

## 📋 O Que Mudou?

### **Antes:**
```
Internet → Servidor Contabo (porta 8000) → Container Python
```
**Problema:** Bots atacam direto no Python (lento, logs sujos)

### **Agora:**
```
Internet → Nginx (porta 80) → Container Python (rede interna)
```
**Benefícios:**
- ✅ Nginx bloqueia 90% dos bots antes de chegar no Python
- ✅ Rate limiting nativo (muito mais rápido)
- ✅ Logs separados (Nginx captura o lixo)
- ✅ Performance máxima
- ✅ Preparado para HTTPS (SSL/TLS)

---

## 🔧 Mudanças Necessárias

### **1. Nenhuma mudança no `.env` da API** ✅
- Todas as variáveis de ambiente permanecem iguais
- A aplicação continua rodando na porta 8000 (internamente)

### **2. Chatbot - Verificar URL**

#### **Se o chatbot usa URL externa:**
```bash
# Antes (no .env do chatbot)
API_URL=http://SEU_IP:8000

# Agora (mude para porta 80)
API_URL=http://SEU_IP
# ou
API_URL=http://SEU_IP:80
```

#### **Se o chatbot usa rede Docker interna:**
```bash
# NÃO PRECISA MUDAR (continua igual)
API_URL=http://meu-secretario-api:8000
```

---

## 🚀 Como Fazer o Deploy

### **Passo 1: Criar estrutura de diretórios**

```bash
# No servidor Contabo, na pasta do projeto
mkdir -p nginx/logs
```

### **Passo 2: Verificar arquivos criados**

Você deve ter:
```
AppControleFinanceiro/
├── nginx/
│   ├── nginx.conf        ← Arquivo de configuração do Nginx
│   └── logs/             ← Pasta para logs (será criada automaticamente)
├── docker-compose.yml    ← Atualizado com container Nginx
├── .env
└── ...
```

### **Passo 3: Parar containers atuais**

```bash
docker-compose down
```

### **Passo 4: Rebuild e subir com Nginx**

```bash
docker-compose up -d --build
```

### **Passo 5: Verificar se subiu**

```bash
# Ver status dos containers
docker-compose ps

# Deve mostrar 3 containers rodando:
# - meu-secretario-nginx  (porta 80)
# - meu-secretario-api    (sem porta exposta)
# - meu-secretario-redis  (porta 6379)
```

### **Passo 6: Testar**

```bash
# Teste básico (deve funcionar)
curl http://localhost/

# Teste de URL suspeita (deve retornar 404)
curl http://localhost/.env

# Teste de bot malicioso (deve retornar 403)
curl -H "User-Agent: sqlmap" http://localhost/
```

---

## 🔍 Verificação de Logs

### **Logs do Nginx (tráfego externo):**
```bash
# Ver tentativas de ataque
tail -f nginx/logs/access.log | grep -E "(404|403)"

# Ver logs de erro
tail -f nginx/logs/error.log
```

### **Logs da aplicação Python:**
```bash
# Agora devem estar MUITO mais limpos
docker logs -f meu-secretario-api
```

### **O que você verá:**

**Logs do Nginx (vai capturar o lixo):**
```
172.104.241.92 - - [23/Nov/2025:08:53:54 +0000] "GET /.env HTTP/1.0" 404
172.104.241.92 - - [23/Nov/2025:08:53:59 +0000] "GET /wp-admin HTTP/1.0" 404
```

**Logs da API (só tráfego válido):**
```
[SECURITY] ✅ Rate Limiting ativado
[SECURITY] ✅ Bot Protection ativado
127.0.0.1 - - [23/Nov/2025:09:00:00 +0000] "POST /api/transacao HTTP/1.1" 200
```

---

## ⚙️ Configurações Importantes do Nginx

### **1. Rate Limiting Configurado:**

| Tipo | Limite | Burst |
|------|--------|-------|
| API geral | 20 req/s | +10 |
| Admin | 5 req/s | +5 |
| Webhooks | 50 req/s | +50 |

### **2. Bloqueios Automáticos:**

- ❌ User-Agents de bots (sqlmap, nmap, nikto, etc.)
- ❌ Requisições sem User-Agent
- ❌ Métodos HTTP inválidos
- ❌ URLs suspeitas (`.env`, `wp-admin`, etc.)
- ❌ Scripts suspeitos (`.asp`, `.jsp`, etc.)

### **3. Timeouts:**

- Conexões idle: 30s
- Body do request: 10s
- Headers: 10s
- Envio de resposta: 10s

---

## 🔒 Configurando HTTPS (SSL/TLS)

### **Opção 1: Let's Encrypt (Recomendado - Grátis)**

```bash
# 1. Instalar Certbot no servidor
apt-get update
apt-get install certbot python3-certbot-nginx

# 2. Obter certificado
certbot --nginx -d seu-dominio.com

# 3. Certbot vai editar nginx.conf automaticamente

# 4. Rebuild
docker-compose restart nginx
```

### **Opção 2: Manual (se já tiver certificado)**

1. Edite `nginx/nginx.conf`
2. Descomente a seção `# CONFIGURAÇÃO PARA HTTPS`
3. Substitua `seu-dominio.com` pelo seu domínio
4. Coloque os certificados em `/etc/letsencrypt/`
5. No `docker-compose.yml`, descomente a porta `443:443`
6. Rebuild: `docker-compose up -d`

---

## 🐛 Troubleshooting

### **Problema: "502 Bad Gateway"**

**Causa:** Nginx não consegue conectar ao backend

**Solução:**
```bash
# Verificar se o backend está rodando
docker ps | grep meu-secretario-api

# Verificar logs do backend
docker logs meu-secretario-api

# Verificar rede Docker
docker network inspect rede-global
```

### **Problema: "nginx: [emerg] host not found in upstream"**

**Causa:** Container `meu-secretario-api` ainda não subiu

**Solução:**
```bash
# Subir backend primeiro
docker-compose up -d web redis

# Depois subir Nginx
docker-compose up -d nginx
```

### **Problema: Chatbot não consegue mais se conectar à API**

**Causa:** Chatbot usando `http://SEU_IP:8000` (porta 8000 não exposta mais)

**Solução:**
```bash
# No .env do chatbot, mudar para:
API_URL=http://SEU_IP      # Porta 80 (padrão HTTP)
# ou
API_URL=http://SEU_IP:80
```

---

## 📊 Testando a Proteção

### **Teste 1: URL Suspeita**
```bash
curl -i http://SEU_IP/.env
# Esperado: HTTP/1.1 404 Not Found
```

### **Teste 2: Bot Malicioso**
```bash
curl -H "User-Agent: sqlmap/1.0" http://SEU_IP/
# Esperado: HTTP/1.1 403 Forbidden
```

### **Teste 3: Rate Limiting**
```bash
# Enviar 30 requisições rapidamente
for i in {1..30}; do curl -s -o /dev/null -w "%{http_code}\n" http://SEU_IP/; done
# Esperado: Primeiras ~20 retornam 200, depois começam a retornar 503
```

### **Teste 4: Endpoint Válido**
```bash
curl http://SEU_IP/
# Esperado: HTTP/1.1 200 OK + resposta da aplicação
```

---

## 🎯 Métricas de Sucesso

Após 24h rodando com Nginx, você deve ver:

### **Logs do Nginx:**
- 📊 100-500 requisições bloqueadas/dia
- 🚫 80-90% de redução de tráfego chegando no Python

### **Logs da API:**
- ✅ Apenas requisições válidas
- ✅ Zero tentativas de scanner nos logs do Python
- ✅ Performance melhor (menos processamento)

### **Uso de CPU/RAM:**
- ⬇️ Redução de 20-40% no uso de CPU do container Python
- ✅ Nginx usa <50MB de RAM

---

## 🔄 Rollback (se der problema)

Se precisar voltar para a configuração anterior:

```bash
# 1. Parar tudo
docker-compose down

# 2. Editar docker-compose.yml
# Comentar seção do nginx
# Descomentar porta 8000 do container 'web'

# 3. Subir novamente
docker-compose up -d
```

---

## ⚡ Próximos Passos Opcionais

### **1. Configurar HTTPS**
- Seguir passos na seção "Configurando HTTPS"
- Certificado grátis com Let's Encrypt

### **2. Whitelist de IPs para /admin**
- Editar `nginx/nginx.conf`
- Descomentar seção `# allow SEU_IP_PUBLICO;`
- Adicionar seu IP público

### **3. Log Rotation**
- Evitar logs gigantes do Nginx
```bash
# Criar logrotate config
cat > /etc/logrotate.d/nginx-docker <<EOF
/caminho/nginx/logs/*.log {
    daily
    rotate 7
    compress
    delaycompress
    missingok
    notifempty
}
EOF
```

---

## 📞 Checklist Final

Antes de fazer deploy em produção:

- [ ] Pasta `nginx/` criada
- [ ] Arquivo `nginx/nginx.conf` presente
- [ ] `docker-compose.yml` atualizado
- [ ] Porta 80 liberada no firewall do Contabo
- [ ] Chatbot atualizado para usar porta 80 (se necessário)
- [ ] Teste local funcionando
- [ ] Backup do `docker-compose.yml` antigo feito

---

**🎉 Pronto! Sua aplicação agora está protegida por duas camadas de segurança:**

1. **Nginx** → Bloqueia 90% do lixo (rápido, em C)
2. **Middleware Python** → Bloqueia os 10% que passaram (inteligente, contextual)

**Resultado:** Logs limpos + Performance máxima + Segurança em profundidade! 🛡️
