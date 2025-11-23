# 🧪 Guia Completo de Testes - Segurança

Este guia ensina como testar todas as funcionalidades de segurança implementadas.

## 📋 Pré-requisitos

1. Aplicação rodando localmente:
   ```bash
   python run.py
   ```
   Ou via Docker:
   ```bash
   docker-compose up
   ```

2. Redis rodando (para testes de persistência)

3. Ferramentas necessárias:
   - `curl` (linha de comando)
   - `python` (para scripts de teste)
   - Postman/Insomnia (opcional, para testes manuais)

---

## 1️⃣ Testando Rate Limiting

### Teste Manual com curl:

```bash
# Enviar múltiplas requisições rapidamente
for i in {1..20}; do
  echo "Requisição $i:"
  curl -s -o /dev/null -w "%{http_code}\n" http://localhost:8000/
  sleep 0.1
done
```

**Resultado esperado:**
- Primeiras ~16 requisições: `200 OK`
- Após limite: `429 Too Many Requests`

### Teste com Script Python:

```python
import requests
import time

BASE_URL = "http://localhost:8000"

print("Testando rate limiting...")
for i in range(1, 21):
    response = requests.get(BASE_URL)
    print(f"Req {i}: Status {response.status_code}")

    if response.status_code == 429:
        print(f"✅ Rate limiting ativou após {i} requisições!")
        break

    time.sleep(0.1)
```

---

## 2️⃣ Testando Proteção contra Bots

### Teste 1: URL Suspeita (deve retornar 404)

```bash
curl -i http://localhost:8000/.env
curl -i http://localhost:8000/wp-admin
curl -i http://localhost:8000/phpMyAdmin
```

**Resultado esperado:**
- Status: `404 Not Found`
- Log no console: `[SECURITY-ATTEMPT] IP: 127.0.0.1 | Tentativa: 1/5`

### Teste 2: Bloqueio Automático

Envie 6 requisições suspeitas:

```bash
#!/bin/bash
echo "Enviando requisições suspeitas..."

urls=(
  "/.env"
  "/wp-admin"
  "/phpMyAdmin"
  "/.git/config"
  "/admin/login.php"
  "/shell.php"
)

for url in "${urls[@]}"; do
  echo "Tentando: $url"
  curl -s -o /dev/null -w "Status: %{http_code}\n" "http://localhost:8000$url"
  sleep 0.5
done

echo ""
echo "Tentando acessar endpoint válido após bloqueio:"
curl -i http://localhost:8000/
```

**Resultado esperado:**
- Primeiras 4 tentativas: `404`
- 5ª tentativa: `403 Forbidden` com mensagem de bloqueio
- Endpoint válido também retorna `403` (IP bloqueado)

### Teste 3: User-Agent Suspeito

```bash
curl -H "User-Agent: sqlmap/1.0" http://localhost:8000/
```

**Resultado esperado:**
- Status: `404`
- Log: `[SECURITY-ATTEMPT] ... Razão: Suspicious User-Agent`

---

## 3️⃣ Testando Persistência de Bloqueios (Redis)

### Passo 1: Bloquear um IP

```bash
# Enviar 5 requisições suspeitas
for i in {1..5}; do
  curl http://localhost:8000/.env
done
```

### Passo 2: Reiniciar a Aplicação

```bash
# Via Docker
docker-compose restart

# Ou se rodando localmente
# Ctrl+C para parar
# python run.py para reiniciar
```

### Passo 3: Tentar acessar novamente

```bash
curl -i http://localhost:8000/
```

**Resultado esperado:**
- ✅ Ainda retorna `403 Forbidden`
- ✅ IP permanece bloqueado após restart (graças ao Redis!)

### Verificar dados no Redis:

```bash
# Conectar ao Redis
docker exec -it <redis_container> redis-cli

# Listar chaves de segurança
KEYS security:*

# Ver detalhes de um IP bloqueado
GET security:blocked:127.0.0.1

# Ver tentativas de um IP
GET security:attempts:127.0.0.1
```

---

## 4️⃣ Testando Endpoint de Estatísticas

```bash
curl -H "x-api-key: SUA_CHAVE_SECRETA" \
  http://localhost:8000/admin/security-stats | jq
```

**Resultado esperado:**
```json
{
  "blocked_ips": [
    {
      "ip": "127.0.0.1",
      "attempts": 5,
      "blocked_until": "2025-11-23T11:30:00",
      "time_remaining": "0:58:23"
    }
  ],
  "suspicious_activity": [],
  "total_blocked": 1,
  "total_suspicious": 0,
  "redis_connected": true
}
```

---

## 5️⃣ Testando Criptografia de API Keys

### Teste 1: Criar Novo Usuário

```bash
# Simular cadastro de usuário (ajuste conforme seu endpoint)
curl -X POST http://localhost:8000/webhook-whatsapp \
  -H "x-api-key: SUA_CHAVE_SECRETA" \
  -H "Content-Type: application/json" \
  -d '{
    "texto": "começar",
    "numero_remetente": "5511999999999"
  }'
```

### Teste 2: Verificar no Banco

```sql
-- Conectar ao PostgreSQL
psql -U usuario -d nome_do_banco

-- Ver API key criptografada
SELECT id, nome, LEFT(api_key_automate, 50) as api_key_preview
FROM Usuarios
WHERE numero_whatsapp = '5511999999999';
```

**Resultado esperado:**
- API key começa com `gAAAAA` (token criptografado Fernet)
- **Não** está em plain text

### Teste 3: Autenticação com API Key

```bash
# Usar a API key para fazer uma transação (ajuste conforme seu endpoint)
curl -X POST http://localhost:8000/api/transacao \
  -H "Content-Type: application/json" \
  -d '{
    "user_api_key": "SUA_API_KEY_AQUI",
    "valor": 50.00,
    "local": "Teste",
    "conta": "Carteira",
    "tipo_pagamento": "dinheiro"
  }'
```

**Resultado esperado:**
- ✅ Autenticação funciona normalmente
- ✅ API key é descriptografada internamente
- ✅ Transação criada com sucesso

---

## 6️⃣ Testando Security Headers

```bash
curl -I http://localhost:8000/
```

**Headers esperados:**
```
HTTP/1.1 200 OK
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Content-Security-Policy: default-src 'self'; ...
```

---

## 7️⃣ Script de Teste Automatizado Completo

Use o script fornecido:

```bash
python test_security.py
```

**O que ele testa:**
1. ✅ Endpoints válidos funcionam
2. ✅ URLs suspeitas retornam 404
3. ✅ Bloqueio automático após 5 tentativas
4. ✅ IP bloqueado não acessa endpoints válidos
5. ✅ Endpoint de estatísticas funciona
6. ✅ Rate limiting ativa após muitas requisições

---

## 8️⃣ Testes de Integração (Produção)

### Após Deploy no Render.com:

#### 1. Testar Proteção contra Scanners Reais

Aguarde alguns minutos e verifique os logs:

```bash
# Logs no Render.com (Dashboard > Logs)
# Ou via CLI
render logs -t your-service
```

Procure por:
```
[SECURITY-ATTEMPT] IP: x.x.x.x | Tentativa: ...
[SECURITY-AUTO-BLOCK] IP x.x.x.x bloqueado automaticamente
```

#### 2. Verificar Estatísticas de Segurança

```bash
curl -H "x-api-key: SUA_CHAVE" \
  https://seu-backend.onrender.com/admin/security-stats | jq
```

#### 3. Monitorar Redis

```bash
# Conectar ao Redis de produção (ajuste conforme seu provider)
redis-cli -h seu-redis-host -p 6379 -a senha

# Ver IPs bloqueados
KEYS security:blocked:*

# Contar IPs bloqueados
DBSIZE
```

---

## 9️⃣ Cenários de Teste Específicos

### Cenário 1: Ataque de Scanner (Simulado)

```python
import requests
import random

BASE_URL = "http://localhost:8000"

# Lista de URLs típicas de scanners
scanner_urls = [
    "/.env", "/wp-admin", "/phpmyadmin", "/.git/config",
    "/admin/login.php", "/xmlrpc.php", "/shell.php",
    "/config.php", "/backup.sql", "/database.sql"
]

print("Simulando scanner de vulnerabilidades...")
for i, url in enumerate(scanner_urls, 1):
    try:
        response = requests.get(f"{BASE_URL}{url}", timeout=5)
        print(f"{i}. {url}: {response.status_code}")

        if response.status_code == 403:
            print(f"   ✅ Scanner bloqueado na tentativa {i}!")
            break
    except Exception as e:
        print(f"   Erro: {e}")

    if i % 3 == 0:
        import time
        time.sleep(1)
```

### Cenário 2: Usuário Legítimo não é Afetado

```bash
# Simular uso normal
echo "Simulando usuário legítimo..."

# 10 requisições válidas em sequência
for i in {1..10}; do
  echo "Requisição $i:"
  curl -s -o /dev/null -w "Status: %{http_code}\n" http://localhost:8000/
  sleep 2  # Intervalo normal entre requisições
done
```

**Resultado esperado:**
- ✅ Todas retornam `200 OK`
- ✅ Nenhum bloqueio

### Cenário 3: Burst Legítimo (WhatsApp)

```bash
# Simular múltiplas mensagens chegando rapidamente
echo "Simulando burst de mensagens do WhatsApp..."

for i in {1..15}; do
  curl -X POST http://localhost:8000/webhook-whatsapp \
    -H "x-api-key: SUA_CHAVE" \
    -H "Content-Type: application/json" \
    -d "{\"texto\": \"msg $i\", \"numero_remetente\": \"5511999999999\"}" \
    -s -o /dev/null -w "Msg $i: %{http_code}\n"

  sleep 0.3
done
```

**Resultado esperado:**
- ✅ Primeiras ~10-15 mensagens: `200 OK`
- ⚠️  Após isso: Pode atingir rate limit (100/min)

---

## 🔟 Troubleshooting

### Problema: Rate limiting não funciona

**Possíveis causas:**
1. Redis não está rodando
2. Variável `RATELIMIT_ENABLED=false`

**Solução:**
```bash
# Verificar Redis
docker ps | grep redis

# Verificar variável de ambiente
echo $RATELIMIT_ENABLED
```

### Problema: Bloqueios não persistem

**Possíveis causas:**
1. Redis não conectado
2. Dados não estão sendo salvos

**Solução:**
```bash
# Verificar logs da aplicação
docker logs meu-secretario-api | grep REDIS

# Deve aparecer:
# [REDIS] Conectado ao Redis
```

### Problema: Criptografia falha

**Possíveis causas:**
1. Biblioteca `cryptography` não instalada
2. `ENCRYPTION_KEY` inválida

**Solução:**
```bash
# Reinstalar dependências
pip install -r requirements.txt

# Gerar nova chave
python -c "from cryptography.fernet import Fernet; print(Fernet.generate_key().decode())"
```

---

## ✅ Checklist de Testes

Antes do deploy em produção, verifique:

- [ ] Rate limiting funciona (teste com 20+ requisições)
- [ ] Proteção contra bots bloqueia URLs suspeitas
- [ ] Bloqueio automático ativa após 5 tentativas
- [ ] IPs bloqueados persistem após restart (Redis)
- [ ] Endpoint `/admin/security-stats` retorna dados corretos
- [ ] API keys são criptografadas no banco
- [ ] Autenticação funciona com keys criptografadas
- [ ] Security headers presentes nas respostas
- [ ] Logs de segurança são gerados em `logs/security.log`
- [ ] Redis está conectado e funcionando

---

## 📊 Métricas de Sucesso

Após 24h em produção, você deve ver:

1. **Bloqueios automáticos:** Alguns IPs de scanners bloqueados
2. **Tentativas suspeitas:** Log de ~10-50 tentativas/dia
3. **Rate limiting:** Poucos casos (usuários legítimos não atingem)
4. **Criptografia:** Todas as novas API keys criptografadas

---

## 🆘 Suporte

Se encontrar problemas durante os testes:

1. Verifique os logs: `docker logs -f meu-secretario-api`
2. Verifique o arquivo de log: `tail -f logs/security.log`
3. Teste conexão Redis: `redis-cli ping`
4. Verifique variáveis de ambiente: `docker exec meu-secretario-api env | grep SECURITY`

---

**Última atualização:** 2025-11-23
