# 🚨 Guia: Remover IP 172.19.0.6 da Blacklist

## Resumo

Você bloqueou acidentalmente o IP **172.19.0.6** (seu próprio Postman dentro do Docker). Este guia mostra como removê-lo.

---

## ✅ Opção 1: Via API (Postman) - MAIS RÁPIDO

### Passo a Passo

1. **Abra o Postman**

2. **Importe a Collection Completa**
   - File → Import
   - Selecione: `postman/Complete_API.postman_collection.json`

3. **Configure as Variáveis**
   - Clique no nome da collection "Meu Secretário - API Completa"
   - Vá em "Variables"
   - Configure:
     ```
     BASE_URL: http://seu-servidor-contabo.com:8000
     API_SECRET_KEY: sua-chave-secreta-aqui
     USUARIO_ID: 1
     ```

4. **Remover o IP da Blacklist**
   - Vá em: `🔐 Segurança` → `Remover IP da Blacklist`
   - No body, o IP já está preenchido: `172.19.0.6`
   - Clique em **Send**

5. **Verificar se foi removido**
   - Execute: `🔐 Segurança` → `Ver Estatísticas de Segurança`
   - Verifique que `total_blacklisted` está `0`
   - Ou que `172.19.0.6` não aparece mais na lista

### Resposta Esperada (Sucesso)

```json
{
  "status": "sucesso",
  "mensagem": "IP 172.19.0.6 removido da blacklist",
  "ip": "172.19.0.6"
}
```

---

## ✅ Opção 2: Via Script Python no Servidor

### Passo 1: Acessar o Servidor

```bash
ssh seu-usuario@seu-servidor-contabo.com
```

### Passo 2: Navegar até o diretório do projeto

```bash
cd /caminho/do/projeto/AppControleFinanceiro
```

### Passo 3: Executar o Script dentro do Docker

```bash
docker exec -it meu-secretario-api python scripts/remove_ip_from_blacklist.py 172.19.0.6
```

### Saída Esperada

```
============================================================
SCRIPT: Remover IP da Blacklist
============================================================
[INFO] Conectando ao Redis: redis://redis:6379/0
[OK] Conectado ao Redis com sucesso!

[INFO] Removendo IP: 172.19.0.6

ANTES:
============================================================
IPs NA BLACKLIST
============================================================

IP: 172.19.0.6
  Razão: Tentativas repetidas de invasão
  Bloqueado em: 2025-11-27T20:50:00

============================================================
Total: 1 IP(s) na blacklist
============================================================

[SUCESSO] IP 172.19.0.6 removido da blacklist!
  Razão original: Tentativas repetidas de invasão
  Bloqueado em: 2025-11-27T20:50:00
  Removido em: 2025-11-27 21:00:00

DEPOIS:
============================================================
IPs NA BLACKLIST
============================================================
Nenhum IP na blacklist.

============================================================
Total: 0 IP(s) na blacklist
============================================================

[OK] Operação concluída com sucesso!
```

### Passo 4: Listar IPs (Opcional)

Para apenas ver quais IPs estão na blacklist:

```bash
docker exec -it meu-secretario-api python scripts/remove_ip_from_blacklist.py
```

---

## ✅ Opção 3: Via cURL (Linha de Comando)

### No seu computador local:

```bash
curl -X POST http://seu-servidor-contabo.com:8000/admin/security-blacklist-remove \
  -H "x-api-key: SUA_API_KEY_AQUI" \
  -H "Content-Type: application/json" \
  -d '{"ip": "172.19.0.6"}'
```

### Verificar se foi removido:

```bash
curl -X GET http://seu-servidor-contabo.com:8000/admin/security-stats \
  -H "x-api-key: SUA_API_KEY_AQUI"
```

---

## 🔍 Verificar se o Problema foi Resolvido

Após remover o IP, teste se o Postman volta a funcionar:

1. **No Postman, execute qualquer endpoint**
   - Por exemplo: `🏠 Home` → `Health Check`
   - Ou: `🔧 Setup & Configuração` → `Setup Database`

2. **Resultado esperado:**
   - ✅ Status 200 OK (ou o status esperado do endpoint)
   - ❌ NÃO deve retornar 403 Forbidden

3. **Se ainda retornar 403:**
   - Verifique se o IP foi realmente removido (veja estatísticas)
   - Verifique se há bloqueio temporário (aparecerá em `blocked_ips`)
   - Aguarde alguns minutos (cache do Redis)

---

## 🛡️ Prevenir que Isso Aconteça Novamente

### Opção A: Whitelist de IPs Internos (Recomendado)

Edite [app/middleware/security.py](app/middleware/security.py) e adicione no início do `security_filter()`:

```python
def security_filter():
    ip = request.remote_addr
    path = request.path
    user_agent = request.headers.get('User-Agent', '')

    # WHITELIST: IPs internos do Docker (nunca bloquear)
    INTERNAL_IPS = ['127.0.0.1', 'localhost']
    INTERNAL_NETWORKS = ['172.16.0.0/12', '10.0.0.0/8', '192.168.0.0/16']

    # Verificar se IP é interno
    from ipaddress import ip_address, ip_network
    try:
        ip_obj = ip_address(ip)
        for network in INTERNAL_NETWORKS:
            if ip_obj in ip_network(network):
                return None  # IP interno, não bloquear
    except ValueError:
        pass

    # Continua com as verificações normais...
```

### Opção B: Usar Postman Fora do Docker

Configure o Postman para acessar diretamente o servidor, não via rede Docker.

---

## 📋 Checklist Final

- [ ] IP 172.19.0.6 removido da blacklist
- [ ] Estatísticas de segurança mostram `total_blacklisted: 0`
- [ ] Postman consegue acessar endpoints novamente
- [ ] (Opcional) Whitelist de IPs internos implementada

---

## 🆘 Troubleshooting

### Erro: "Redis indisponível"

**Problema:** O Redis não está rodando.

**Solução:**
```bash
docker ps | grep redis
docker start redis  # se estiver parado
```

### Erro: "IP não encontrado na blacklist"

**Problema:** O IP já foi removido ou nunca foi bloqueado.

**Solução:**
```bash
# Verificar estatísticas
docker exec -it meu-secretario-api python scripts/remove_ip_from_blacklist.py
```

### Erro: "Chave de API inválida"

**Problema:** A `x-api-key` está incorreta.

**Solução:**
- Verifique o arquivo `.env` no servidor
- A chave está em `API_SECRET_KEY`

### Ainda retorna 403 após remover

**Possíveis causas:**
1. IP está em **bloqueio temporário** (não blacklist)
2. Cache do Redis
3. Endpoint realmente não existe

**Verificar:**
```bash
curl http://seu-servidor.com:8000/admin/security-stats \
  -H "x-api-key: SUA_KEY"
```

Procure por:
- `blacklisted_ips`: Se 172.19.0.6 ainda aparece aqui, rode o script novamente
- `blocked_ips`: Se aparece aqui, aguarde 1 hora ou remova manualmente do Redis
- `suspicious_activity`: Se aparece aqui, é apenas monitoramento, não bloqueio

---

## 📞 Suporte

Se nada funcionar:

1. **Limpar TUDO do Redis (última opção):**
   ```bash
   docker exec -it redis redis-cli FLUSHDB
   ```
   ⚠️ **ATENÇÃO:** Isso remove TODOS os dados de segurança!

2. **Verificar logs:**
   ```bash
   docker logs meu-secretario-api | grep BLACKLIST
   ```

3. **Reiniciar o container:**
   ```bash
   docker restart meu-secretario-api
   ```

---

## ✅ Tudo Pronto!

Agora você tem:
- ✅ Script Python para gerenciar blacklist
- ✅ Endpoints da API para gerenciar via Postman
- ✅ Collection completa do Postman com TODOS os endpoints
- ✅ IP 172.19.0.6 removido da blacklist
- ✅ Guia completo para prevenir problemas futuros
