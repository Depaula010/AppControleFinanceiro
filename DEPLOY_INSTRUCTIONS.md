# 🚀 Instruções de Deploy - Meu Secretário

## 📋 Índice

1. [Primeira Configuração](#primeira-configuração)
2. [Deploy Automático (GitHub Actions)](#deploy-automático-github-actions)
3. [Deploy Manual](#deploy-manual)
4. [Pós-Deploy](#pós-deploy)
5. [Troubleshooting](#troubleshooting)

---

## 🆕 Primeira Configuração

### 1. Configurar Secrets no GitHub

Acesse: `Settings` → `Secrets and variables` → `Actions` → `New repository secret`

**Secrets obrigatórios:**

| Secret | Descrição | Exemplo |
|--------|-----------|---------|
| `CONTABO_SSH_KEY` | Chave SSH privada | `-----BEGIN RSA PRIVATE KEY-----...` |
| `CONTABO_HOST` | IP do servidor | `212.47.65.37` |
| `CONTABO_USER` | Usuário SSH | `root` ou `usuario` |
| `API_SECRET_KEY` | Chave de autenticação admin | `sua-chave-super-secreta-32-caracteres-min` |
| `GEMINI_API_KEY` | API key do Google Gemini | `AIza...` |
| `DATABASE_URL` | URL do PostgreSQL | `postgresql://user:pass@host:5432/db` |
| `BOT_WHATSAPP_URL` | URL do bot WhatsApp | `https://seu-bot-whatsapp.com` |
| `GOOGLE_CLIENT_ID` | OAuth Google Calendar | `123456789-abcdef.apps.googleusercontent.com` |
| `GOOGLE_CLIENT_SECRET` | OAuth Google Calendar | `GOCSPX-abc123...` |
| `GOOGLE_REDIRECT_URI` | OAuth redirect | `http://212.47.65.37/oauth2callback` |
| `DB_PASSWORD` | Senha do PostgreSQL | `senha-segura-do-banco` |

### 2. Preparar Servidor

**SSH no servidor:**
```bash
ssh usuario@212.47.65.37
```

**Instalar Docker:**
```bash
# Atualizar sistema
sudo apt-get update
sudo apt-get upgrade -y

# Instalar Docker
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh

# Habilitar Docker
sudo systemctl enable docker
sudo systemctl start docker
```

**Instalar Docker Compose:**
```bash
sudo apt-get install docker-compose-plugin -y
```

**Criar rede Docker:**
```bash
docker network create rede-global
```

**Clonar repositório:**
```bash
cd /opt
sudo git clone https://github.com/seu-usuario/AppControleFinanceiro.git meu-secretario
cd meu-secretario
```

### 3. Gerar Certificados SSL

```bash
cd /opt/meu-secretario/nginx
bash generate-ssl-cert.sh
```

**Saída esperada:**
```
🔐 Gerando certificado SSL auto-assinado...
✅ Certificados SSL gerados com sucesso!
📁 Arquivos criados:
   - ssl/selfsigned.key (chave privada)
   - ssl/selfsigned.crt (certificado público)
```

### 4. Criar arquivo .env

```bash
cd /opt/meu-secretario
nano .env
```

**Conteúdo (substitua os valores):**
```env
GEMINI_API_KEY=sua-chave-gemini
DATABASE_URL=postgresql://user:pass@host:5432/db
REDIS_URL=redis://redis:6379
API_SECRET_KEY=sua-chave-super-secreta
BOT_WHATSAPP_URL=https://seu-bot-whatsapp.com
GOOGLE_CLIENT_ID=seu-client-id
GOOGLE_CLIENT_SECRET=seu-client-secret
GOOGLE_REDIRECT_URI=http://212.47.65.37/oauth2callback
DB_PASSWORD=senha-do-banco
PYTHON_VERSION=3.11
```

### 5. Primeiro Deploy

```bash
# Build e start
docker compose build --no-cache
docker compose up -d

# Verificar logs
docker compose logs -f
```

**Aguardar até ver:**
```
meu-secretario-nginx  | Nginx started successfully
meu-secretario-api    | [DB] ✅ Engine configurado
meu-secretario-api    | Gunicorn listening on 0.0.0.0:8000
```

### 6. Testar Aplicação

**HTTP:**
```bash
curl http://212.47.65.37/
```

**HTTPS:**
```bash
curl -k https://212.47.65.37/
```

**Esperado:**
```json
{
  "status": "online",
  "service": "Meu Secretário API",
  "database": "✅ Conectado",
  "timestamp": "2025-11-23T10:30:00"
}
```

---

## 🤖 Deploy Automático (GitHub Actions)

### Como Funciona

Ao fazer **push para `main`**, o GitHub Actions:
1. ✅ SSH no servidor
2. ✅ `git pull` (atualiza código)
3. ✅ Atualiza `.env` com secrets
4. ✅ `docker compose down`
5. ✅ `docker compose build --no-cache`
6. ✅ `docker compose up -d`
7. ✅ Health check na porta 80

### Fazer Deploy

```bash
# 1. Fazer alterações no código
# 2. Commit
git add .
git commit -m "feat: Nova funcionalidade"

# 3. Push (deploy automático)
git push origin main
```

### Acompanhar Deploy

**No GitHub:**
- Vá para: `Actions` → `Deploy to Contabo` → Ver workflow em execução

**Via SSH:**
```bash
# Logs em tempo real
docker compose logs -f

# Status dos containers
docker compose ps
```

---

## 🛠️ Deploy Manual

### Quando Usar

- ⚠️ GitHub Actions está fora do ar
- ⚠️ Deploy emergencial
- ⚠️ Teste local

### Procedimento

```bash
# 1. SSH no servidor
ssh usuario@212.47.65.37

# 2. Navegar para projeto
cd /opt/meu-secretario

# 3. Backup do .env (segurança)
cp .env .env.backup

# 4. Atualizar código
git fetch origin
git reset --hard origin/main

# 5. Parar containers
docker compose down

# 6. Limpar cache
docker system prune -f

# 7. Rebuild
docker compose build --no-cache

# 8. Iniciar
docker compose up -d

# 9. Verificar logs
docker compose logs --tail=50 web
docker compose logs --tail=50 nginx

# 10. Health check
curl http://localhost/
```

---

## ✅ Pós-Deploy

### 1. Verificar Serviços

```bash
# Status dos containers
docker compose ps

# Esperado:
# NAME                    STATUS        PORTS
# meu-secretario-nginx    Up 2 minutes  80->80, 443->443
# meu-secretario-api      Up 2 minutes  (internal)
# meu-secretario-redis    Up 2 minutes  (internal)
```

### 2. Testar Endpoints

**Home:**
```bash
curl http://212.47.65.37/
```

**Admin (com autenticação):**
```bash
curl -H "x-api-key: sua-chave" http://212.47.65.37/admin/security-stats
```

### 3. Configurar Fail2Ban (Opcional)

```bash
# Instalar
sudo apt-get install fail2ban -y

# Copiar configurações
sudo cp /opt/meu-secretario/nginx/fail2ban-nginx.conf /etc/fail2ban/jail.d/meu-secretario.conf

# Criar filtros (ver SECURITY_GUIDE.md)
# ...

# Reiniciar
sudo systemctl restart fail2ban

# Verificar
sudo fail2ban-client status
```

### 4. Configurar Log Rotation

```bash
# Copiar configuração
sudo cp /opt/meu-secretario/nginx/logrotate-nginx.conf /etc/logrotate.d/nginx-meu-secretario

# Definir permissões
sudo chmod 644 /etc/logrotate.d/nginx-meu-secretario

# Testar
sudo logrotate -d /etc/logrotate.d/nginx-meu-secretario

# Forçar rotação (teste)
sudo logrotate -f /etc/logrotate.d/nginx-meu-secretario
```

### 5. Configurar Monitoramento

```bash
# Dar permissão ao script
chmod +x /opt/meu-secretario/scripts/monitor-security.sh

# Testar manualmente
/opt/meu-secretario/scripts/monitor-security.sh

# Adicionar ao cron (executa a cada hora)
crontab -e
# Adicionar:
0 * * * * /opt/meu-secretario/scripts/monitor-security.sh >> /opt/meu-secretario/logs/security-monitor.log 2>&1
```

---

## 🔥 Troubleshooting

### ❌ Container não inicia

**Sintoma:** `docker compose ps` mostra container com status `Exited`

**Solução:**
```bash
# Ver logs de erro
docker compose logs web

# Verificar .env
cat .env

# Reconstruir
docker compose down
docker compose build --no-cache
docker compose up -d
```

### ❌ Erro 502 Bad Gateway

**Sintoma:** Nginx retorna 502 ao acessar aplicação

**Causas comuns:**
1. Backend não iniciou (verificar logs do container `web`)
2. PostgreSQL inacessível

**Solução:**
```bash
# Verificar se backend está rodando
docker compose ps

# Logs do backend
docker compose logs web

# Testar conexão com PostgreSQL manualmente
docker exec meu-secretario-api python -c "from app import db_engine; db_engine.connect()"
```

### ❌ Erro "Permission denied" (GitHub Actions)

**Sintoma:** Deploy falha com erro de SSH

**Solução:**
1. Verificar `CONTABO_SSH_KEY` está correto (incluir `-----BEGIN RSA PRIVATE KEY-----`)
2. Verificar permissões da chave no servidor:
   ```bash
   chmod 700 ~/.ssh
   chmod 600 ~/.ssh/authorized_keys
   ```

### ❌ Redis não conecta

**Sintoma:** Logs mostram `Redis não conectado`

**Solução:**
```bash
# Verificar se Redis está rodando
docker compose ps redis

# Logs do Redis
docker compose logs redis

# Testar conexão
docker exec meu-secretario-api python -c "import redis; r = redis.Redis(host='redis', port=6379); print(r.ping())"
```

### ❌ HTTPS não funciona

**Sintoma:** `curl https://212.47.65.37` retorna erro

**Causas:**
1. Certificados não gerados
2. Porta 443 não exposta

**Solução:**
```bash
# Verificar certificados
ls -la /opt/meu-secretario/nginx/ssl/

# Se não existirem:
cd /opt/meu-secretario/nginx
bash generate-ssl-cert.sh

# Verificar porta 443 no docker-compose.yml
grep "443:443" docker-compose.yml

# Reiniciar
docker restart meu-secretario-nginx
```

### ❌ IP mudou e não consigo acessar /admin

**Sintoma:** `403 Forbidden` ao acessar `/admin`

**Solução:**
```bash
# 1. SSH no servidor
ssh usuario@212.47.65.37

# 2. Descobrir seu novo IP
curl ifconfig.me

# 3. Editar nginx.conf
nano /opt/meu-secretario/nginx/nginx.conf

# Procurar linha:
#   allow 212.47.65.37;
# Substituir pelo novo IP:
#   allow SEU.NOVO.IP.AQUI;

# 4. Reiniciar Nginx
docker restart meu-secretario-nginx
```

---

## 📞 Comandos Úteis

### Logs
```bash
# Todos os logs (tempo real)
docker compose logs -f

# Apenas backend
docker compose logs -f web

# Apenas nginx
docker compose logs -f nginx

# Últimas 100 linhas
docker compose logs --tail=100
```

### Restart
```bash
# Restart suave (recarrega código)
docker compose restart

# Restart completo (rebuild)
docker compose down
docker compose up -d

# Restart apenas um serviço
docker restart meu-secretario-api
```

### Limpar
```bash
# Limpar containers parados
docker container prune -f

# Limpar imagens não usadas
docker image prune -a -f

# Limpar tudo (cuidado!)
docker system prune -a -f
```

### Banco de Dados
```bash
# Executar script SQL
docker exec -it meu-secretario-api python -c "from app import db_engine; ..."

# Ver logs do PostgreSQL (se fosse local)
# (No seu caso é externo)
```

---

## 🎯 Próximos Passos

Após primeiro deploy bem-sucedido:

1. ✅ Testar todos os endpoints
2. ✅ Configurar Fail2Ban
3. ✅ Configurar log rotation
4. ✅ Configurar monitoramento (cron)
5. ✅ Documentar procedimentos da equipe
6. ✅ Fazer backup do servidor
7. ✅ Treinar equipe em resposta a incidentes

---

**Dúvidas?** Consulte [SECURITY_GUIDE.md](SECURITY_GUIDE.md) para procedimentos de segurança.

**Última atualização:** 2025-11-23
