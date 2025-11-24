# Checklist de Deploy Seguro

## 🔐 Antes de Fazer Deploy em Produção

### 1. Variáveis de Ambiente

#### Backend (AppControleFinanceiro)
API_SECRET_KEY=P_j22bUUTQRRCn09NY2Ptf1jemweHZBNHaCckYapsAA
WEBHOOK_SIGNATURE_KEY=_LYl0o9iG-ir77uHrYfWV0iSnDFPjeKqQrOwz9I5OPU

- [ ] API_SECRET_KEY configurada (32+ caracteres)
- [ ] WEBHOOK_SIGNATURE_KEY configurada  
- [ ] ENCRYPTION_KEY configurada (Fernet)
- [ ] DATABASE_URL com senha forte
- [ ] GEMINI_API_KEY configurada
- [ ] REDIS_URL com senha
- [ ] CORS_ENABLED=false (ou origins explícitas)

#### Bot WhatsApp
- [ ] API_SECRET_KEY (mesma do backend)
- [ ] WEBHOOK_SIGNATURE_KEY (mesma do backend)
- [ ] DATABASE_URL com senha forte
- [ ] POSTGRES_PASSWORD forte
- [ ] ADMIN_WHATSAPP_NUMBER configurado
- [ ] ALLOWED_RECIPIENTS configurado
- [ ] PYTHON_API_URL correto

### 2. Segurança

- [ ] Arquivo .env criado (nunca commitar!)
- [ ] .gitignore contém .env
- [ ] Secrets removidos do código
- [ ] Rate limiting ativado
- [ ] HTTPS configurado (certificado válido)
- [ ] Redis com --requirepass
- [ ] PostgreSQL com sslmode=require
- [ ] Logs sanitizados (testar com transação)

### 3. Docker & Deploy

- [ ] docker-compose.yml sem secrets hardcoded
- [ ] Containers com usuário não-root (recomendado)
- [ ] Portas sensíveis não expostas (5432, 6379)
- [ ] Volumes para persistência configurados
- [ ] Health checks funcionando

### 4. Testes de Segurança



### 5. Monitoramento

- [ ] Sentry/Datadog configurado
- [ ] Alertas para falhas de autenticação
- [ ] Monitoring de uso de APIs (Gemini)
- [ ] Logs centralizados

### 6. Backup

- [ ] Backup automatizado do PostgreSQL
- [ ] Backup das credenciais OAuth (criptografadas)
- [ ] Procedimento de restore testado

---

## ⚠️ Avisos Importantes

1. **NUNCA** commite arquivo .env
2. **SEMPRE** use chaves diferentes para cada ambiente (dev/staging/prod)
3. **ROTACIONE** API_SECRET_KEY periodicamente (a cada 3-6 meses)
4. **MANTENHA** logs de acesso por no mínimo 90 dias
5. **TESTE** restore de backup mensalmente

---

## 🚨 Em Caso de Incidente

1. Rotacione imediatamente:
   - API_SECRET_KEY
   - WEBHOOK_SIGNATURE_KEY  
   - ENCRYPTION_KEY (requer re-criptografar dados)
   - Senha do banco
   
2. Analise logs para identificar comprometimento

3. Notifique usuários afetados (LGPD/GDPR)

---

**Criado:** 2025-01-24
