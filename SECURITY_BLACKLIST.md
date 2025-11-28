# Sistema de Blacklist Permanente de IPs

## Visão Geral

Sistema de bloqueio permanente de IPs para proteger a aplicação contra invasores e bots maliciosos.

## Funcionalidades

### 1. Bloqueio Permanente (Blacklist)
- IPs são bloqueados por **1 ano** (365 dias)
- Armazenados no Redis com chave `security:blacklist:{ip}`
- Primeira verificação no middleware (máxima performance)
- Logs detalhados de todas as tentativas

### 2. Bloqueio Temporário (Mantido)
- Sistema existente: 5 tentativas suspeitas = bloqueio de 1 hora
- Complementa a blacklist permanente

### 3. Endpoints Administrativos

#### 📊 Ver Estatísticas de Segurança
```bash
GET /admin/security-stats
Header: x-api-key: SUA_API_KEY
```

**Resposta:**
```json
{
  "blacklisted_ips": [
    {
      "ip": "172.19.0.6",
      "reason": "Tentativas repetidas de invasão",
      "blacklisted_at": "2025-11-27T20:50:00",
      "permanent": true
    }
  ],
  "blocked_ips": [...],
  "suspicious_activity": [...],
  "total_blacklisted": 1,
  "total_blocked": 0,
  "total_suspicious": 0,
  "redis_connected": true
}
```

#### 🚫 Adicionar IP à Blacklist
```bash
POST /admin/security-blacklist-add
Header: x-api-key: SUA_API_KEY
Content-Type: application/json

Body:
{
  "ip": "172.19.0.6",
  "reason": "Tentativas repetidas de invasão"
}
```

**Resposta de Sucesso:**
```json
{
  "status": "sucesso",
  "mensagem": "IP 172.19.0.6 adicionado à blacklist permanente",
  "ip": "172.19.0.6",
  "reason": "Tentativas repetidas de invasão"
}
```

#### ✅ Remover IP da Blacklist
```bash
POST /admin/security-blacklist-remove
Header: x-api-key: SUA_API_KEY
Content-Type: application/json

Body:
{
  "ip": "172.19.0.6"
}
```

**Resposta de Sucesso:**
```json
{
  "status": "sucesso",
  "mensagem": "IP 172.19.0.6 removido da blacklist",
  "ip": "172.19.0.6"
}
```

## Como Usar

### Bloquear o IP 172.19.0.6

1. **Via Postman:**
   - Importe a collection: `postman/Security_Blacklist.postman_collection.json`
   - Configure as variáveis:
     - `BASE_URL`: URL do seu servidor
     - `API_SECRET_KEY`: Sua chave de API
   - Execute a requisição "2. Adicionar IP à Blacklist"

2. **Via cURL:**
```bash
curl -X POST http://seu-servidor.com/admin/security-blacklist-add \
  -H "x-api-key: SUA_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "ip": "172.19.0.6",
    "reason": "Tentativas repetidas de invasão ao endpoint /admin/setup-alertas-financeiros"
  }'
```

3. **Via Python:**
```python
import requests

url = "http://seu-servidor.com/admin/security-blacklist-add"
headers = {
    "x-api-key": "SUA_API_KEY",
    "Content-Type": "application/json"
}
data = {
    "ip": "172.19.0.6",
    "reason": "Tentativas repetidas de invasão"
}

response = requests.post(url, headers=headers, json=data)
print(response.json())
```

### Verificar se o Bloqueio Funcionou

```bash
curl -X GET http://seu-servidor.com/admin/security-stats \
  -H "x-api-key: SUA_API_KEY"
```

Procure por `"total_blacklisted": 1` na resposta.

## Comportamento do Sistema

### Ordem de Verificação (Middleware)
1. ✋ **Blacklist permanente** → 403 imediato
2. ⏱️ **Bloqueio temporário** → 403 imediato
3. 🔍 **Análise de suspeita** → Track ou 404
4. ✅ **Requisição válida** → Prossegue

### Respostas HTTP

**IP na Blacklist:**
```json
HTTP 403 Forbidden
{
  "error": "Access permanently denied",
  "message": "Your IP has been permanently blocked"
}
```

**IP Bloqueado Temporariamente:**
```json
HTTP 403 Forbidden
{
  "error": "Access denied",
  "message": "Your IP has been temporarily blocked due to suspicious activity"
}
```

## Logs de Segurança

### Quando IP é Adicionado à Blacklist
```
[SECURITY-BLACKLIST] IP adicionado à blacklist permanente: 172.19.0.6 | Razão: Tentativas repetidas de invasão
```

### Quando IP Blacklistado Tenta Acessar
```
[SECURITY-BLACKLISTED] IP na blacklist tentou acessar | IP: 172.19.0.6 | Path: /admin/setup-alertas-financeiros
```

### Quando IP é Removido da Blacklist
```
[SECURITY-BLACKLIST] IP removido da blacklist: 172.19.0.6
```

## Arquitetura

### Armazenamento Redis
```
Chave: security:blacklist:{ip}
TTL: 31536000 segundos (1 ano)
Valor: JSON com:
  - ip
  - reason
  - blacklisted_at
  - permanent: true
```

### Funções Principais (security.py)

| Função | Descrição |
|--------|-----------|
| `is_ip_blacklisted(ip)` | Verifica se IP está na blacklist |
| `blacklist_ip(ip, reason)` | Adiciona IP à blacklist |
| `remove_from_blacklist(ip)` | Remove IP da blacklist |
| `security_filter()` | Middleware que filtra requisições |
| `get_security_stats()` | Retorna estatísticas completas |

## Observações Importantes

### Sobre o IP 172.19.0.6

⚠️ **ATENÇÃO:** Este IP é provavelmente do **Postman dentro do Docker** (rede interna).

Se você está vendo este IP nos logs:
- Ele está acessando endpoints **válidos** (`/admin/setup-alertas-financeiros`)
- É provável que seja **você mesmo testando** via Postman
- Bloqueá-lo pode **impedir seus próprios testes**

**Verifique antes de bloquear:**
```bash
docker network inspect <nome-da-rede> | grep 172.19.0.6
```

### Recomendação

Se o IP **172.19.0.6** for do Postman:
1. **NÃO bloqueie** este IP
2. Configure o Postman para usar outra rede
3. Ou ajuste as regras de segurança para ignorar IPs da rede Docker interna

Se for um invasor real:
1. ✅ Bloqueie imediatamente
2. Investigue os logs completos
3. Verifique se há outras tentativas de outros IPs

## Dependências

- **Redis** (obrigatório): Armazenamento de blacklist
- **Flask**: Framework web
- **app.services.redis_service**: Serviço de conexão com Redis

## Troubleshooting

### "Redis indisponível"
```json
{
  "status": "erro",
  "mensagem": "Falha ao adicionar IP à blacklist (Redis indisponível)"
}
```

**Solução:**
1. Verifique se o Redis está rodando: `docker ps | grep redis`
2. Teste conexão: `redis-cli ping` (deve retornar `PONG`)
3. Verifique variáveis de ambiente `REDIS_URL`

### IP não está sendo bloqueado

1. Verifique se está na blacklist:
```bash
curl http://seu-servidor.com/admin/security-stats -H "x-api-key: SUA_KEY"
```

2. Verifique logs do servidor:
```bash
docker logs meu-secretario-api | grep SECURITY-BLACKLIST
```

3. Teste diretamente do IP bloqueado:
```bash
curl http://seu-servidor.com/
```

Deve retornar:
```json
{
  "error": "Access permanently denied",
  "message": "Your IP has been permanently blocked"
}
```

## Segurança

- ✅ Todos os endpoints requerem `x-api-key`
- ✅ IPs são validados antes de adicionar
- ✅ Logs detalhados de todas as operações
- ✅ Blacklist tem TTL de 1 ano (não é eterno)
- ✅ Possibilidade de remover IPs da blacklist
- ✅ Sistema resiliente (fallback se Redis cair)

## Exemplos de Uso Real

### Cenário 1: Bot Scanner Detectado
```bash
# 1. Ver estatísticas
curl http://api.com/admin/security-stats -H "x-api-key: KEY"

# 2. Identificar IP com muitas tentativas
# Output: "ip": "45.123.45.67", "recent_attempts": 8

# 3. Bloquear permanentemente
curl -X POST http://api.com/admin/security-blacklist-add \
  -H "x-api-key: KEY" \
  -H "Content-Type: application/json" \
  -d '{"ip": "45.123.45.67", "reason": "Scanner bot - 8 tentativas suspeitas"}'
```

### Cenário 2: Falso Positivo
```bash
# Usuário legítimo foi bloqueado por engano

# 1. Verificar se está na blacklist
curl http://api.com/admin/security-stats -H "x-api-key: KEY"

# 2. Remover da blacklist
curl -X POST http://api.com/admin/security-blacklist-remove \
  -H "x-api-key: KEY" \
  -H "Content-Type: application/json" \
  -d '{"ip": "192.168.1.50"}'

# 3. Confirmar remoção
curl http://api.com/admin/security-stats -H "x-api-key: KEY"
```

## Integração com Monitoramento

### Script de Monitoramento Automático
```bash
#!/bin/bash
# monitor-security.sh

API_URL="http://seu-servidor.com"
API_KEY="sua-chave"

# Verificar estatísticas
stats=$(curl -s "$API_URL/admin/security-stats" -H "x-api-key: $API_KEY")

# Alertar se houver IPs suspeitos
suspicious_count=$(echo $stats | jq '.total_suspicious')

if [ "$suspicious_count" -gt 3 ]; then
    echo "ALERTA: $suspicious_count IPs com atividade suspeita!"
    # Enviar notificação (e-mail, Slack, etc.)
fi
```

Execute periodicamente via cron:
```cron
*/15 * * * * /path/to/monitor-security.sh
```

## Changelog

### v1.0 (2025-11-27)
- ✅ Sistema de blacklist permanente implementado
- ✅ 3 novos endpoints administrativos
- ✅ Collection do Postman criada
- ✅ Integração com middleware de segurança existente
- ✅ Logs detalhados
- ✅ Documentação completa
