# 🛡️ Guia de Segurança - Meu Secretário API

## Visão Geral

Este documento descreve as camadas de segurança implementadas na aplicação para proteger contra ataques comuns como:
- Scanning/reconnaissance de bots
- Brute force
- DDoS
- Exploits de vulnerabilidades conhecidas
- Requisições malformadas

## Camadas de Segurança Implementadas

### 1. Rate Limiting ⏱️

Limites de requisições por IP para prevenir abuso:

- **Padrão:** 1000 requisições/hora (~16/min - uso confortável)
- **API:** 500 requisições/hora (endpoints de transação)
- **Webhooks:** 100/minuto + 3000/hora (suporta bursts do WhatsApp)
- **Admin:** 20/minuto + 200/hora (proteção brute force)

**Configuração via variáveis de ambiente:**
```bash
RATELIMIT_ENABLED=true
REDIS_URL=redis://localhost:6379  # Para armazenamento distribuído
```

### 2. Proteção contra Bots e Scanners 🤖

Sistema automático de detecção e bloqueio de bots maliciosos:

#### Padrões Detectados:
- URLs suspeitas: `.env`, `.git`, `phpMyAdmin`, `wp-admin`, etc.
- User-Agents maliciosos: `sqlmap`, `nikto`, `nmap`, `metasploit`, etc.
- Endpoints inexistentes (não na whitelist)

#### Comportamento:
1. **1ª tentativa suspeita:** Log de aviso + retorna 404
2. **5 tentativas em 10 minutos:** Bloqueio automático do IP por 60 minutos
3. **IP bloqueado:** Retorna 403 com mensagem clara

#### Logs de Segurança:
```
[SECURITY-ATTEMPT] IP: 172.104.241.92 | Tentativa: 3/5 | Razão: Suspicious URL pattern
[SECURITY-AUTO-BLOCK] IP 172.104.241.92 bloqueado automaticamente | Razão: 5 tentativas suspeitas
[SECURITY-BLOCKED] IP bloqueado tentou acessar | IP: 172.104.241.92 | Path: /admin
```

**Configuração via variáveis de ambiente:**
```bash
BOT_PROTECTION_ENABLED=true
AUTO_BLOCK_ENABLED=true
BLOCK_DURATION_MINUTES=60
MAX_SUSPICIOUS_ATTEMPTS=5
```

### 3. Security Headers 🔒

Headers HTTP de segurança configurados via Flask-Talisman:

- **Strict-Transport-Security:** Força HTTPS por 1 ano
- **X-Frame-Options:** DENY (previne clickjacking)
- **X-Content-Type-Options:** nosniff
- **Referrer-Policy:** strict-origin-when-cross-origin
- **Content-Security-Policy:** Política restrita de conteúdo

**Configuração via variáveis de ambiente:**
```bash
SECURITY_HEADERS_ENABLED=true
```

### 4. CORS (Cross-Origin Resource Sharing) 🌐

Controle de origens permitidas (desabilitado por padrão):

```bash
CORS_ENABLED=false  # Habilite apenas se necessário
CORS_ORIGINS=https://seudominio.com,https://app.seudominio.com
```

### 5. Logging de Segurança 📝

Todos os eventos de segurança são registrados em:
- **Arquivo:** `logs/security.log`
- **Console:** stdout (visível no Docker/Render.com)

**Configuração via variáveis de ambiente:**
```bash
SECURITY_LOG_FILE=logs/security.log
SECURITY_LOG_LEVEL=WARNING  # WARNING, INFO, DEBUG
```

## Endpoints Administrativos

### Visualizar Estatísticas de Segurança

```http
GET /admin/security-stats
Header: x-api-key: SUA_CHAVE_SECRETA
```

**Resposta:**
```json
{
  "blocked_ips": [
    {
      "ip": "172.104.241.92",
      "attempts": 7,
      "blocked_until": "2025-11-23T09:54:49",
      "time_remaining": "0:45:23"
    }
  ],
  "suspicious_activity": [
    {
      "ip": "192.168.1.100",
      "recent_attempts": 3,
      "last_attempt": "2025-11-23T09:10:15",
      "reasons": [
        "Suspicious URL pattern: /wp-admin",
        "Unknown endpoint: /phpMyAdmin",
        "Suspicious User-Agent: sqlmap"
      ]
    }
  ],
  "total_blocked": 1,
  "total_suspicious": 1
}
```

## Análise do Log de Ataque

Exemplo real do log fornecido:

```
172.104.241.92 - - [23/Nov/2025:08:53:54 +0000] "GET /nice%20ports%2C/Tri%6Eity.txt%2ebak HTTP/1.0" 404 207
```

### O que estava acontecendo:

1. **IP:** `172.104.241.92` (provavelmente bot/scanner)
2. **Ataques detectados:**
   - `/nice%20ports%2C/Tri%6Eity.txt%2ebak` - Scanner Nmap
   - `/devicedesc.xml` - Busca por dispositivos UPnP
   - `/+CSCOE+/logon.html` - Cisco VPN exploit
   - `/dana-na/` - Pulse Secure VPN exploit
   - `/CFIDE/componentutils/` - Adobe ColdFusion exploit
   - `/geoserver/` - GeoServer exploit
   - Protocolos não-HTTP: RTSP, SIP, SOCKS

3. **Como a aplicação respondeu:**
   - ✅ Retornou 404 para todos os endpoints inexistentes
   - ✅ Bloqueou requisições com protocolos inválidos (Gunicorn)
   - ✅ Não revelou informações sobre a stack tecnológica

### Com a proteção implementada:

Agora esse IP seria **automaticamente bloqueado** após 5 tentativas, recebendo:

```json
{
  "error": "Access denied",
  "message": "Too many suspicious requests. IP blocked."
}
```

## Boas Práticas de Deploy

### 1. Variáveis de Ambiente Recomendadas

Adicione no seu `.env` (e configure no Render.com):

```bash
# Segurança - Rate Limiting
RATELIMIT_ENABLED=true

# Segurança - Headers HTTP
SECURITY_HEADERS_ENABLED=true

# Segurança - Proteção contra Bots
BOT_PROTECTION_ENABLED=true
AUTO_BLOCK_ENABLED=true
BLOCK_DURATION_MINUTES=60
MAX_SUSPICIOUS_ATTEMPTS=5

# Segurança - Criptografia
# IMPORTANTE: Gere uma chave única para produção!
# ENCRYPTION_KEY=<gere com: python -c "from cryptography.fernet import Fernet; print(Fernet.generate_key().decode())">

# Logging
SECURITY_LOG_LEVEL=WARNING

# CORS (apenas se necessário)
CORS_ENABLED=false
# CORS_ORIGINS=https://seudominio.com
```

### 2. Instalar Dependências

```bash
pip install -r requirements.txt
```

### 3. Rebuild do Docker

```bash
docker-compose down
docker-compose build --no-cache
docker-compose up -d
```

### 4. Monitoramento

Verifique logs de segurança regularmente:

```bash
# No servidor
tail -f logs/security.log

# No Docker
docker logs -f meu-secretario-api | grep SECURITY
```

Consulte estatísticas via API:
```bash
curl -H "x-api-key: SUA_CHAVE" https://seu-backend.onrender.com/admin/security-stats
```

## Melhorias Implementadas ✅

### Implementado Recentemente:

1. ✅ **Persistência de bloqueios no Redis** - Bloqueios sobrevivem a restarts
2. ✅ **Rate limits ajustados** - Valores realistas para uso pessoal e preparados para SaaS
3. ✅ **Criptografia de API keys** - Keys armazenadas criptografadas no banco (Fernet/AES-128)
4. ✅ **Múltiplas janelas de tempo** - Rate limiting com minuto + hora

## Limitações Atuais

### O que NÃO está implementado:

1. **IP Whitelisting/Blacklisting permanente** - Os bloqueios são temporários (60 min no Redis)
2. **WAF (Web Application Firewall)** - Para proteção mais avançada
3. **CAPTCHA** - Para endpoints públicos se necessário
4. **MFA (Multi-Factor Authentication)** - Para acessos administrativos
5. **Rate limiting por usuário** - Atualmente é apenas por IP

## Próximos Passos Recomendados

### ✅ Concluído:
1. ✅ Implementar proteção contra bots
2. ✅ Rate limiting
3. ✅ Security headers
4. ✅ Persistência de bloqueios no Redis
5. ✅ Criptografia de API keys
6. ✅ Ajuste de limites para uso real

### Médio Prazo (quando virar SaaS):
1. Rate limiting por usuário (não apenas por IP)
2. Implementar IP blacklist permanente via banco de dados
3. Adicionar auditoria detalhada de acessos administrativos
4. Planos com limites diferentes (Free/Pro/Enterprise)

### Longo Prazo:
1. Implementar WAF rules no proxy reverso (Cloudflare/AWS WAF)
2. Migrar autenticação para JWT com refresh tokens
3. Adicionar MFA opcional para endpoints críticos
4. Implementar honeypots para detectar bots
5. Dashboard de métricas de segurança em tempo real

## Suporte e Dúvidas

Para reportar problemas de segurança ou sugerir melhorias, abra uma issue ou entre em contato diretamente.

---

**Última atualização:** 2025-11-23
**Versão:** 1.0
