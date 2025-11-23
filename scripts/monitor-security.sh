#!/bin/bash
# ============================================
# SCRIPT DE MONITORAMENTO DE SEGURANÇA
# Meu Secretário - Monitor de IPs Bloqueados
# ============================================
#
# Este script monitora:
# - IPs bloqueados no Redis (pelo middleware Python)
# - IPs banidos pelo Fail2Ban (se instalado)
# - Estatísticas de segurança
# - Logs de ataques recentes
#
# USO:
# ----
# chmod +x scripts/monitor-security.sh
# ./scripts/monitor-security.sh
#
# CRON (Executar a cada hora e enviar alertas):
# 0 * * * * /opt/meu-secretario/scripts/monitor-security.sh >> /opt/meu-secretario/logs/security-monitor.log 2>&1

echo "============================================"
echo "🔐 MONITORAMENTO DE SEGURANÇA"
echo "⏰ Data: $(date '+%Y-%m-%d %H:%M:%S')"
echo "============================================"
echo ""

# Cores para output
RED='\033[0;31m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# ============================================
# 1. VERIFICAR IPS BLOQUEADOS NO REDIS
# ============================================
echo -e "${BLUE}📊 IPs Bloqueados no Redis (Middleware Python)${NC}"
echo "-------------------------------------------"

# Acessar Redis dentro do container
BLOCKED_IPS=$(docker exec meu-secretario-redis redis-cli KEYS "blocked_ip:*" 2>/dev/null)

if [ -z "$BLOCKED_IPS" ]; then
    echo -e "${GREEN}✅ Nenhum IP bloqueado no momento${NC}"
else
    COUNT=$(echo "$BLOCKED_IPS" | wc -l)
    echo -e "${RED}⚠️  $COUNT IPs bloqueados:${NC}"
    echo ""

    for key in $BLOCKED_IPS; do
        IP=$(echo $key | sed 's/blocked_ip://')
        TTL=$(docker exec meu-secretario-redis redis-cli TTL "$key" 2>/dev/null)

        if [ "$TTL" -gt 0 ]; then
            MINUTES=$((TTL / 60))
            echo -e "  ${RED}🔒${NC} $IP (expira em ${MINUTES}min)"
        else
            echo -e "  ${RED}🔒${NC} $IP (permanente)"
        fi
    done
fi
echo ""

# ============================================
# 2. VERIFICAR TENTATIVAS SUSPEITAS
# ============================================
echo -e "${BLUE}🕵️  Tentativas Suspeitas Recentes${NC}"
echo "-------------------------------------------"

SUSPICIOUS=$(docker exec meu-secretario-redis redis-cli KEYS "suspicious:*" 2>/dev/null)

if [ -z "$SUSPICIOUS" ]; then
    echo -e "${GREEN}✅ Nenhuma tentativa suspeita recente${NC}"
else
    COUNT=$(echo "$SUSPICIOUS" | wc -l)
    echo -e "${YELLOW}⚠️  $COUNT IPs com tentativas suspeitas:${NC}"
    echo ""

    for key in $SUSPICIOUS; do
        IP=$(echo $key | sed 's/suspicious://')
        ATTEMPTS=$(docker exec meu-secretario-redis redis-cli GET "$key" 2>/dev/null)
        TTL=$(docker exec meu-secretario-redis redis-cli TTL "$key" 2>/dev/null)

        if [ "$ATTEMPTS" -ge 3 ]; then
            echo -e "  ${RED}⚠️${NC}  $IP: $ATTEMPTS tentativas (TTL: ${TTL}s)"
        else
            echo -e "  ${YELLOW}⚠️${NC}  $IP: $ATTEMPTS tentativas (TTL: ${TTL}s)"
        fi
    done
fi
echo ""

# ============================================
# 3. VERIFICAR FAIL2BAN (se instalado)
# ============================================
if command -v fail2ban-client &> /dev/null; then
    echo -e "${BLUE}🛡️  Status do Fail2Ban${NC}"
    echo "-------------------------------------------"

    # Verificar jails ativos
    JAILS=$(fail2ban-client status 2>/dev/null | grep "Jail list" | sed 's/.*://;s/,//g')

    if [ -z "$JAILS" ]; then
        echo -e "${YELLOW}⚠️  Fail2Ban instalado mas sem jails ativos${NC}"
    else
        for jail in $JAILS; do
            STATUS=$(fail2ban-client status $jail 2>/dev/null)
            BANNED=$(echo "$STATUS" | grep "Currently banned" | awk '{print $NF}')
            TOTAL=$(echo "$STATUS" | grep "Total banned" | awk '{print $NF}')

            if [ "$BANNED" -gt 0 ]; then
                echo -e "  ${RED}🚫${NC} $jail: $BANNED IPs banidos (total histórico: $TOTAL)"

                # Listar IPs banidos
                BANNED_IPS=$(echo "$STATUS" | grep "Banned IP list" | sed 's/.*://;s/\s//g')
                if [ ! -z "$BANNED_IPS" ]; then
                    echo "     IPs: $BANNED_IPS"
                fi
            else
                echo -e "  ${GREEN}✅${NC} $jail: Nenhum IP banido"
            fi
        done
    fi
    echo ""
else
    echo -e "${YELLOW}⚠️  Fail2Ban não instalado${NC}"
    echo "   Para instalar: sudo apt-get install fail2ban"
    echo ""
fi

# ============================================
# 4. LOGS DE ATAQUES RECENTES (últimas 24h)
# ============================================
echo -e "${BLUE}📋 Logs de Ataques Recentes (últimas 24h)${NC}"
echo "-------------------------------------------"

# Contar 403s (bloqueados)
BLOCKED_403=$(docker logs meu-secretario-nginx --since 24h 2>/dev/null | grep -c ' 403 ')
echo -e "  ${RED}🔒${NC} Requisições bloqueadas (403): $BLOCKED_403"

# Contar 404s (URLs não encontradas)
NOT_FOUND=$(docker logs meu-secretario-nginx --since 24h 2>/dev/null | grep -c ' 404 ')
echo -e "  ${YELLOW}❓${NC} URLs não encontradas (404): $NOT_FOUND"

# Contar rate limits
RATE_LIMITS=$(docker logs meu-secretario-nginx --since 24h 2>/dev/null | grep -c 'limiting requests')
echo -e "  ${YELLOW}⏱️${NC}  Rate limits aplicados: $RATE_LIMITS"

# Top 5 IPs com mais requisições bloqueadas
echo ""
echo -e "${BLUE}🔝 Top 5 IPs Atacantes (últimas 24h)${NC}"
docker logs meu-secretario-nginx --since 24h 2>/dev/null | \
    grep -E ' (403|404) ' | \
    awk '{print $1}' | \
    sort | uniq -c | sort -rn | head -5 | \
    while read count ip; do
        echo -e "  ${RED}📍${NC} $ip: $count requisições bloqueadas"
    done
echo ""

# ============================================
# 5. ESTATÍSTICAS DO ENDPOINT DE SEGURANÇA
# ============================================
echo -e "${BLUE}📊 Estatísticas da API de Segurança${NC}"
echo "-------------------------------------------"

# Chamar endpoint /admin/security-stats (requer autenticação)
# Descomentar se quiser automatizar (configure API_SECRET_KEY)
# STATS=$(curl -s -H "x-api-key: $API_SECRET_KEY" http://localhost:80/admin/security-stats)
# echo "$STATS" | jq '.'

echo "Para ver estatísticas completas, acesse:"
echo "  http://212.47.65.37/admin/security-stats"
echo ""

# ============================================
# 6. ALERTAS (se houver IPs críticos)
# ============================================
TOTAL_BLOCKED=$(echo "$BLOCKED_IPS" | wc -l)
TOTAL_SUSPICIOUS=$(echo "$SUSPICIOUS" | wc -l)

if [ "$TOTAL_BLOCKED" -gt 10 ] || [ "$BLOCKED_403" -gt 1000 ]; then
    echo -e "${RED}🚨 ALERTA: Possível ataque em andamento!${NC}"
    echo "   - $TOTAL_BLOCKED IPs bloqueados"
    echo "   - $BLOCKED_403 requisições bloqueadas (24h)"
    echo ""
    echo "Ações recomendadas:"
    echo "   1. Verificar logs detalhados: docker logs meu-secretario-nginx"
    echo "   2. Verificar país de origem dos IPs"
    echo "   3. Considerar bloquear faixa de IPs se ataque coordenado"
    echo ""
fi

echo "============================================"
echo -e "${GREEN}✅ Monitoramento concluído${NC}"
echo "============================================"
