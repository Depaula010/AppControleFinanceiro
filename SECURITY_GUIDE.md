# 🔐 Guia de Segurança - Meu Secretário

## 📋 Índice

1. [Visão Geral da Segurança](#visão-geral-da-segurança)
2. [Arquitetura de Segurança](#arquitetura-de-segurança)
3. [Configurações de Segurança](#configurações-de-segurança)
4. [Procedimentos de Deploy Seguro](#procedimentos-de-deploy-seguro)
5. [Monitoramento e Alertas](#monitoramento-e-alertas)
6. [Resposta a Incidentes](#resposta-a-incidentes)
7. [Manutenção Regular](#manutenção-regular)
8. [Preparação para SaaS](#preparação-para-saas)

---

## 🛡️ Visão Geral da Segurança

### Status Atual de Segurança: **9.5/10** ⭐

#### ✅ Proteções Implementadas

- **Nginx Reverse Proxy** com WAF (Web Application Firewall)
- **HTTPS/TLS** com certificados SSL
- **Rate Limiting** em múltiplas camadas (Nginx + Python)
- **Whitelist de IPs** para endpoints administrativos
- **Bloqueio Automático** de IPs suspeitos (Redis)
- **Bot Detection** e bloqueio de scanners conhecidos
- **Security Headers** modernos (HSTS, CSP, X-Frame-Options)
- **Backend não exposto** diretamente (apenas via Nginx)
- **Redis não exposto** publicamente
- **Secrets gerenciados** via GitHub Actions

#### 🔒 Camadas de Defesa

```
┌─────────────────────────────────────────────────┐
│  CAMADA 1: Firewall do Sistema (iptables)      │
│  - Fail2Ban (bane IPs automaticamente)         │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│  CAMADA 2: Nginx (Reverse Proxy + WAF)         │
│  - Rate limiting (20 req/s)                     │
│  - Bloqueio de bots conhecidos                  │
│  - Bloqueio de URLs suspeitas                   │
│  - Whitelist de IPs para /admin                 │
│  - Headers de segurança                         │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│  CAMADA 3: Middleware Python (Flask)           │
│  - Verificação de IP bloqueado (Redis)         │
│  - Detecção de padrões suspeitos                │
│  - Bloqueio automático (5 tentativas)           │
│  - Rate limiting adicional                      │
└────────────────────┬────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────┐
│  CAMADA 4: Autenticação da Aplicação           │
│  - x-api-key para endpoints admin               │
│  - OAuth2 para Google Calendar                  │
│  - Criptografia de dados sensíveis              │
└─────────────────────────────────────────────────┘
```

---

## 🏗️ Arquitetura de Segurança

### Fluxo de Requisições

```
Internet → [Firewall/Fail2Ban] → [Nginx:443 HTTPS] → [Backend:8000] → [PostgreSQL]
                                        ↓
                                   [Redis:6379]
                                   (apenas interno)
```

### Portas Expostas

| Porta | Serviço | Exposição | Segurança |
|-------|---------|-----------|-----------|
| 80    | HTTP    | Pública   | ✅ Redireciona para HTTPS |
| 443   | HTTPS   | Pública   | ✅ SSL/TLS habilitado |
| 8000  | Backend | ❌ NÃO EXPOSTA | ✅ Apenas interna (via Nginx) |
| 6379  | Redis   | ❌ NÃO EXPOSTA | ✅ Apenas interna (containers) |

---

## ⚙️ Configurações de Segurança

### 1. Nginx (nginx/nginx.conf)

#### Rate Limiting
```nginx
# API Geral: 20 req/s (burst +10)
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=20r/s;

# Admin: 5 req/s (burst +5)
limit_req_zone $binary_remote_addr zone=admin_limit:10m rate=5r/s;

# Webhooks: 50 req/s (burst +50)
limit_req_zone $binary_remote_addr zone=webhook_limit:10m rate=50r/s;
```

#### Autenticação para Admin
Endpoints `/admin/*` protegidos por:
- ✅ Header `x-api-key` obrigatório (secret)
- ✅ Rate limiting (5 req/s)
- ✅ HTTPS criptografado
- ✅ Bloqueio automático de IPs suspeitos

**Nota:** Whitelist de IP foi removida para facilitar desenvolvimento.
Para produção SaaS, considere adicionar OAuth2/JWT.

#### URLs Bloqueadas
- `.env`, `.git`, `.sql`, `.bak` (arquivos sensíveis)
- `wp-admin`, `phpMyAdmin` (admin de outras plataformas)
- `shell.php`, `config.php` (scripts maliciosos)
- Path traversal (`..`)

### 2. Middleware Python (app/middleware/security.py)

#### Sistema de Bloqueio Automático
- **Threshold:** 5 tentativas suspeitas
- **Janela:** 10 minutos
- **Duração do bloqueio:** 60 minutos
- **Armazenamento:** Redis (TTL automático)

#### Padrões Suspeitos Detectados
- URLs com `.env`, `.git`, `wp-admin`, `phpMyAdmin`
- User-Agents de scanners (sqlmap, nikto, nmap, metasploit)
- Endpoints não autorizados (whitelist)

### 3. SSL/TLS (HTTPS)

#### Certificado Atual
- **Tipo:** Auto-assinado (desenvolvimento)
- **Validade:** 365 dias
- **Algoritmo:** RSA 2048 bits
- **Protocolos:** TLSv1.2, TLSv1.3

#### Gerar Novo Certificado
```bash
cd nginx
bash generate-ssl-cert.sh
```

#### ⚠️ Para Produção SaaS
- **Obter domínio real** (ex: meusecretario.com)
- **Usar Let's Encrypt** (certificado válido e gratuito)
- **Renovação automática** a cada 90 dias

### 4. Fail2Ban (Opcional mas Recomendado)

#### Instalação
```bash
# 1. Instalar Fail2Ban
sudo apt-get update
sudo apt-get install fail2ban -y

# 2. Copiar configurações
sudo cp nginx/fail2ban-nginx.conf /etc/fail2ban/jail.d/meu-secretario.conf

# 3. Criar filtros (ver nginx/fail2ban-filters.conf)
sudo nano /etc/fail2ban/filter.d/meu-secretario-auth.conf
sudo nano /etc/fail2ban/filter.d/meu-secretario-limit.conf
sudo nano /etc/fail2ban/filter.d/meu-secretario-bots.conf

# 4. Reiniciar
sudo systemctl restart fail2ban
sudo fail2ban-client status
```

#### Comandos Úteis
```bash
# Ver status
sudo fail2ban-client status
sudo fail2ban-client status meu-secretario-auth

# Desbanir IP
sudo fail2ban-client set meu-secretario-auth unbanip 1.2.3.4

# Ver log
sudo tail -f /var/log/fail2ban.log
```

---

## 🚀 Procedimentos de Deploy Seguro

### Checklist Pré-Deploy

- [ ] **Secrets configurados** no GitHub
  - [ ] `CONTABO_SSH_KEY`
  - [ ] `API_SECRET_KEY` (forte, 32+ caracteres)
  - [ ] `GEMINI_API_KEY`
  - [ ] `DATABASE_URL`
  - [ ] `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET`

- [ ] **Certificados SSL gerados**
  ```bash
  cd /opt/meu-secretario/nginx
  bash generate-ssl-cert.sh
  ```

- [ ] **IP whitelist atualizado** (se mudou)
  - Editar [nginx/nginx.conf](nginx/nginx.conf#L155)

- [ ] **Variáveis de ambiente verificadas**
  ```bash
  cat /opt/meu-secretario/.env
  ```

### Deploy Manual (Emergência)

```bash
# 1. SSH no servidor
ssh usuario@212.47.65.37

# 2. Navegar para projeto
cd /opt/meu-secretario

# 3. Backup do .env atual
cp .env .env.backup

# 4. Atualizar código
git pull origin main

# 5. Rebuild e restart
docker compose down
docker compose build --no-cache
docker compose up -d

# 6. Verificar logs
docker compose logs -f
```

### Deploy Automático (GitHub Actions)

Basta fazer **push para `main`**:
```bash
git add .
git commit -m "Deploy: Atualização de segurança"
git push origin main
```

O GitHub Actions fará:
1. ✅ SSH no servidor
2. ✅ Git pull
3. ✅ Atualizar .env
4. ✅ Rebuild containers
5. ✅ Health check na porta 80

---

## 📊 Monitoramento e Alertas

### 1. Script de Monitoramento

Execute manualmente ou via cron:
```bash
# Dar permissão
chmod +x scripts/monitor-security.sh

# Executar
./scripts/monitor-security.sh

# Configurar cron (a cada hora)
crontab -e
# Adicionar linha:
0 * * * * /opt/meu-secretario/scripts/monitor-security.sh >> /opt/meu-secretario/logs/security-monitor.log 2>&1
```

### 2. Endpoint de Estatísticas

**URL:** `http://212.47.65.37/admin/security-stats`

**Autenticação:** Requer header `x-api-key`

**Retorna:**
- Total de IPs bloqueados
- Total de tentativas suspeitas
- IPs atualmente bloqueados (lista)
- Timestamp

### 3. Logs para Monitorar

| Log | Localização | O que monitorar |
|-----|-------------|-----------------|
| Nginx Access | `nginx/logs/access.log` | 403, 404, rate limits |
| Nginx Error | `nginx/logs/error.log` | Erros de conexão, timeouts |
| Python App | `logs/app.log` | Erros da aplicação |
| Segurança | `logs/security.log` | IPs bloqueados, tentativas |
| Fail2Ban | `/var/log/fail2ban.log` | Banimentos automáticos |

### 4. Comandos Úteis

```bash
# Ver logs em tempo real
docker compose logs -f nginx
docker compose logs -f web

# Ver IPs bloqueados no Redis
docker exec meu-secretario-redis redis-cli KEYS "blocked_ip:*"

# Ver tentativas suspeitas
docker exec meu-secretario-redis redis-cli KEYS "suspicious:*"

# Desbloquear IP manualmente
docker exec meu-secretario-redis redis-cli DEL "blocked_ip:1.2.3.4"

# Top 10 IPs com mais requisições
docker logs meu-secretario-nginx | awk '{print $1}' | sort | uniq -c | sort -rn | head -10
```

---

## 🚨 Resposta a Incidentes

### Identificação de Ataque

**Sinais de ataque em andamento:**
1. ✅ Muitos 403/404 nos logs (> 1000/hora)
2. ✅ Muitos IPs bloqueados no Redis (> 20)
3. ✅ Rate limiting frequente no error.log
4. ✅ Script de monitoramento reporta alerta
5. ✅ CPU/Memória alta no servidor

### Procedimento de Resposta

#### 1. Verificar Severidade
```bash
# Ver IPs bloqueados
./scripts/monitor-security.sh

# Ver logs recentes
docker logs meu-secretario-nginx --since 1h | grep -E "(403|404)" | tail -50
```

#### 2. Identificar Padrão de Ataque
```bash
# Top IPs atacantes
docker logs meu-secretario-nginx --since 1h | awk '{print $1}' | sort | uniq -c | sort -rn | head -20

# Ver URLs mais atacadas
docker logs meu-secretario-nginx --since 1h | grep " 403 " | awk '{print $7}' | sort | uniq -c | sort -rn | head -10
```

#### 3. Bloquear Faixa de IPs (se ataque coordenado)
```bash
# Exemplo: Bloquear faixa 192.168.x.x
sudo iptables -A INPUT -s 192.168.0.0/16 -j DROP

# Salvar regra
sudo iptables-save > /etc/iptables/rules.v4
```

#### 4. Reduzir Rate Limits Temporariamente
Editar [nginx/nginx.conf](nginx/nginx.conf#L5):
```nginx
# De:
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=20r/s;

# Para:
limit_req_zone $binary_remote_addr zone=api_limit:10m rate=5r/s;
```

Reiniciar Nginx:
```bash
docker restart meu-secretario-nginx
```

#### 5. Notificar e Documentar
- Registrar incidente em `logs/incidents.log`
- Salvar evidências (logs, IPs)
- Atualizar regras de firewall

### Recuperação Pós-Ataque

1. ✅ Analisar logs completos
2. ✅ Atualizar regras de bloqueio
3. ✅ Verificar se houve comprometimento
4. ✅ Restaurar rate limits normais
5. ✅ Documentar lições aprendidas

---

## 🔧 Manutenção Regular

### Diária
- [ ] Verificar logs de erro
- [ ] Monitorar IPs bloqueados

### Semanal
- [ ] Executar script de monitoramento
- [ ] Revisar estatísticas de segurança
- [ ] Verificar espaço em disco (logs)

### Mensal
- [ ] Atualizar dependências (requirements.txt)
- [ ] Renovar certificado SSL (se não auto-renovável)
- [ ] Revisar regras de firewall
- [ ] Backup completo do servidor

### Comandos de Manutenção

```bash
# Limpar logs antigos (manter últimos 30 dias)
find nginx/logs -name "*.log" -mtime +30 -delete
find logs -name "*.log" -mtime +30 -delete

# Verificar espaço em disco
df -h
du -sh /opt/meu-secretario/*

# Atualizar imagens Docker
docker compose pull
docker compose up -d

# Limpar containers/imagens não usados
docker system prune -a
```

---

## 🚀 Preparação para SaaS

### Requisitos Adicionais

#### 1. Domínio e DNS
- [ ] Registrar domínio (ex: meusecretario.com.br)
- [ ] Configurar DNS A record apontando para 212.47.65.37
- [ ] Configurar HTTPS com Let's Encrypt

#### 2. Autenticação Multi-Usuário
- [ ] Sistema de registro de usuários
- [ ] Autenticação JWT/OAuth2
- [ ] Roles e permissões (admin, user)
- [ ] Limite de uso por usuário (rate limiting por user_id)

#### 3. Infraestrutura Escalável
- [ ] Load balancer (Nginx + múltiplos backends)
- [ ] PostgreSQL em cluster (read replicas)
- [ ] Redis Sentinel (alta disponibilidade)
- [ ] CDN para assets estáticos

#### 4. Monitoramento Avançado
- [ ] Prometheus + Grafana (métricas)
- [ ] Sentry (error tracking)
- [ ] Uptime monitoring (Pingdom, UptimeRobot)
- [ ] Log aggregation (ELK stack)

#### 5. Compliance e Legal
- [ ] LGPD (Lei Geral de Proteção de Dados)
- [ ] Termos de Uso e Política de Privacidade
- [ ] Criptografia de dados em repouso
- [ ] Backup automatizado (daily, off-site)
- [ ] Plano de Disaster Recovery

#### 6. Custos Estimados (SaaS)

| Recurso | Custo Mensal (USD) |
|---------|-------------------|
| Servidor Contabo VPS (8GB RAM) | $15 |
| Domínio (.com.br) | $3 |
| PostgreSQL Gerenciado | $25 |
| Redis Gerenciado | $10 |
| CDN (Cloudflare Pro) | $20 |
| Monitoring (Datadog) | $15 |
| **TOTAL** | **~$88/mês** |

---

## 📞 Contatos de Emergência

### Em caso de incidente crítico:

1. **Desligar aplicação** (emergência):
   ```bash
   docker compose down
   ```

2. **Bloquear todo tráfego**:
   ```bash
   sudo iptables -A INPUT -j DROP
   ```

3. **Restaurar backup**:
   ```bash
   # [Documentar procedimento de backup]
   ```

---

## 📚 Recursos Adicionais

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [Nginx Security Hardening](https://www.nginx.com/blog/mitigating-ddos-attacks-with-nginx-and-nginx-plus/)
- [Flask Security Best Practices](https://flask.palletsprojects.com/en/3.0.x/security/)
- [LGPD Guide](https://www.gov.br/cidadania/pt-br/acesso-a-informacao/lgpd)

---

**Última atualização:** 2025-11-23
**Versão:** 1.0
**Responsável:** Administrador do Sistema
