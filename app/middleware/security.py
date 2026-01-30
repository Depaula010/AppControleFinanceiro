# app/middleware/security.py
"""
Middleware de segurança para proteção contra bots, scanners e ataques
"""
import logging
import json
import ipaddress
from flask import request, jsonify
from functools import wraps
from datetime import datetime, timedelta
from collections import defaultdict
import re
from app.services.redis_service import redis_service

# Configurar logger
security_logger = logging.getLogger('security')
security_logger.setLevel(logging.WARNING)

# Prefixos para chaves Redis
REDIS_PREFIX_BLOCKED = 'security:blocked:'
REDIS_PREFIX_ATTEMPTS = 'security:attempts:'
REDIS_PREFIX_BLACKLIST = 'security:blacklist:'  # Bloqueio permanente

# TTL padrão para dados de segurança
BLOCKED_IP_TTL = 3600  # 1 hora em segundos
ATTEMPTS_TTL = 600  # 10 minutos em segundos
BLACKLIST_TTL = 31536000  # 1 ano (bloqueio "permanente")

# Padrões de URLs suspeitas (comum em scanners/bots)
SUSPICIOUS_PATTERNS = [
    r'/\.env',
    r'/\.git',
    # r'/admin',  # Removido - endpoints /admin são legítimos e protegidos por x-api-key
    r'/phpMyAdmin',
    r'/phpmyadmin',
    r'/wp-admin',
    r'/wp-login',
    r'/xmlrpc\.php',
    r'/\.php',
    r'/cgi-bin',
    r'/shell',
    r'/config',
    r'/backup',
    r'/sql',
    r'/db',
    r'/database',
    r'/\_',
    r'/\.\.',
    r'/%2e%2e',
    r'/nice%20ports',
    r'/Tri%6Eity\.txt',
    r'/devicedesc\.xml',
    r'/dana-',
    r'/CFIDE',
    r'/geoserver',
    r'/fog/',
    r'/magento',
    r'/webui',
    r'/\.bak',
    r'/\.old',
    r'/\.backup',
]

# User-Agents suspeitos
SUSPICIOUS_USER_AGENTS = [
    r'sqlmap',
    r'nikto',
    r'nmap',
    r'masscan',
    r'acunetix',
    r'nessus',
    r'openvas',
    r'metasploit',
    r'havij',
    r'zgrab',
]

# Endpoints válidos da aplicação (whitelist)
VALID_ENDPOINTS = {
    # Públicos
    '/',
    '/api/transacao',
    '/webhook-whatsapp',
    '/webhook-google-calendar',
    '/webhook-automate',
    '/webhook-sms-payment',
    '/connect-calendar',  # Aceita qualquer ID
    '/oauth2callback',
    '/disconnect-calendar',  # Aceita qualquer ID

    # Admin - Setup & Configuração
    '/admin/setup-database',
    '/admin/populate-global-categories',
    '/admin/setup-user-data',
    '/admin/setup-calendar-table',
    '/admin/setup-monthly-reports-table',
    '/admin/setup-resumo-matinal',
    '/admin/setup-checkin-noturno',
    '/admin/setup-potes-alerts',
    '/admin/setup-alertas-financeiros',
    '/admin/cleanup-deprecated-notification-fields',
    '/admin/setup-api-keys-tables',
    '/admin/gemini-cache-clear',

    # Admin - Auth
    '/auth/login',
    '/auth/register',
    '/auth/verify',

    # Admin - Triggers
    '/admin/run-motor-agendamentos',
    '/admin/trigger-agenda-notifications',
    '/admin/trigger-bills-notifications',
    '/admin/trigger-daily-briefing',
    '/admin/trigger-monthly-reports-inicio',
    '/admin/trigger-monthly-reports-fim',

    # Admin - Testes & Debug
    '/admin/test-notification',
    '/admin/test-monthly-report',  # Aceita qualquer ID
    '/admin/test-daily-briefing',
    '/admin/debug-calendar',

    # Admin - Configurações & Info
    '/admin/get-notification-config',  # Aceita qualquer ID
    '/admin/config-alertas-financeiros',
    '/admin/oauth-config-check',
    '/admin/security-stats',
    '/admin/security-blacklist-add',
    '/admin/security-blacklist-remove',
    '/admin/gemini-cache-stats',

    # Admin - Utilidades
    '/admin/clear-bot-session',

    # API REST - Dashboard (protegidas por JWT)
    '/api/health',
    '/api/dashboard/summary',
    '/api/dashboard/resumo',
    '/api/dashboard/stats',  # Alias em inglês para /summary
    '/api/dashboard/charts',
    '/api/dashboard/recent',  # Transações recentes para dashboard
    '/api/accounts',
    '/api/contas',
    '/api/transactions',
    '/api/transactions/recent',  # Alias em inglês para /transacoes/recentes
    '/api/transacoes/recentes',
    '/api/categories',
    '/api-keys/validate',
}

def is_trusted_ip(ip):
    """
    Verifica se o IP é confiável (localhost ou rede privada)
    """
    try:
        ip_obj = ipaddress.ip_address(ip)
        # Trust loopback
        if ip_obj.is_loopback:
            return True
        # Trust private networks (Docker networks are usually private)
        if ip_obj.is_private:
            return True
    except ValueError:
        pass
    return False

def is_suspicious_request(path, user_agent):
    """
    Verifica se a requisição é suspeita baseado em padrões conhecidos
    """
    # Verificar URL suspeita
    for pattern in SUSPICIOUS_PATTERNS:
        if re.search(pattern, path, re.IGNORECASE):
            return True, f"Suspicious URL pattern: {pattern}"

    # Verificar User-Agent suspeito
    if user_agent:
        for pattern in SUSPICIOUS_USER_AGENTS:
            if re.search(pattern, user_agent, re.IGNORECASE):
                return True, f"Suspicious User-Agent: {pattern}"

    # Verificar se é endpoint válido
    # Endpoints com IDs dinâmicos
    dynamic_endpoints = [
        '/admin/get-notification-config/',
        '/admin/test-monthly-report/',
        '/connect-calendar/',
        '/disconnect-calendar/',
        '/api/fatura/',  # Permite /api/fatura/<id>/reprocessar
        '/settings/',
        '/api-keys/preferencias/',
        '/calendar-alerts/config/',
        '/addresses/',
    ]

    is_dynamic = any(path.startswith(prefix) for prefix in dynamic_endpoints)

    if path not in VALID_ENDPOINTS and not is_dynamic:
        return True, f"Unknown endpoint: {path}"

    return False, None

def is_ip_blacklisted(ip):
    """
    Verifica se o IP está na blacklist permanente (usando Redis)
    """
    if not redis_service.is_connected():
        return False

    key = f"{REDIS_PREFIX_BLACKLIST}{ip}"
    return redis_service.get(key) is not None

def blacklist_ip(ip, reason="Manual block"):
    """
    Adiciona IP à blacklist permanente (bloqueado por 1 ano)
    """
    if not redis_service.is_connected():
        security_logger.error(
            f"[SECURITY-BLACKLIST] Redis indisponível, não foi possível adicionar IP à blacklist: {ip}"
        )
        return False

    key = f"{REDIS_PREFIX_BLACKLIST}{ip}"

    blacklist_data = {
        'ip': ip,
        'reason': reason,
        'blacklisted_at': datetime.now().isoformat(),
        'permanent': True
    }

    redis_service.set_with_ttl(
        key,
        json.dumps(blacklist_data),
        ttl_seconds=BLACKLIST_TTL
    )

    security_logger.warning(
        f"[SECURITY-BLACKLIST] IP adicionado à blacklist permanente: {ip} | "
        f"Razão: {reason}"
    )

    return True

def remove_from_blacklist(ip):
    """
    Remove IP da blacklist permanente
    """
    if not redis_service.is_connected():
        return False

    key = f"{REDIS_PREFIX_BLACKLIST}{ip}"
    result = redis_service.delete(key)

    if result:
        security_logger.warning(
            f"[SECURITY-BLACKLIST] IP removido da blacklist: {ip}"
        )

    return result

def is_ip_blocked(ip):
    """
    Verifica se o IP está temporariamente bloqueado (usando Redis)
    """
    if not redis_service.is_connected():
        # Fallback: se Redis não disponível, não bloquear
        return False

    key = f"{REDIS_PREFIX_BLOCKED}{ip}"
    blocked_data = redis_service.get(key)

    if blocked_data:
        try:
            data = json.loads(blocked_data)
            blocked_until = datetime.fromisoformat(data['blocked_until'])
            if datetime.now() < blocked_until:
                return True
        except (json.JSONDecodeError, KeyError, ValueError):
            # Dados corrompidos, remover
            redis_service.delete(key)

    return False

def block_ip(ip, duration_minutes=60):
    """
    Bloqueia um IP por um período de tempo (armazenado no Redis)
    """
    if not redis_service.is_connected():
        security_logger.warning(
            f"[SECURITY-BLOCK] Redis indisponível, bloqueio não persistido: {ip}"
        )
        return

    blocked_until = datetime.now() + timedelta(minutes=duration_minutes)
    key = f"{REDIS_PREFIX_BLOCKED}{ip}"

    # Buscar contagem anterior ou iniciar em 1
    existing_data = redis_service.get(key)
    count = 1
    if existing_data:
        try:
            count = json.loads(existing_data).get('count', 0) + 1
        except (json.JSONDecodeError, KeyError):
            count = 1

    # Armazenar dados do bloqueio
    block_data = {
        'blocked_until': blocked_until.isoformat(),
        'count': count,
        'first_blocked': datetime.now().isoformat()
    }

    redis_service.set_with_ttl(
        key,
        json.dumps(block_data),
        ttl_seconds=duration_minutes * 60
    )

    # Log do bloqueio
    security_logger.warning(
        f"[SECURITY-BLOCK] IP bloqueado: {ip} | "
        f"Tentativas: {count} | "
        f"Bloqueado até: {blocked_until.strftime('%Y-%m-%d %H:%M:%S')}"
    )

def track_failed_attempt(ip, reason):
    """
    Registra tentativa suspeita e bloqueia se necessário (usando Redis)
    """
    if not redis_service.is_connected():
        # Fallback: log apenas, sem tracking
        security_logger.warning(
            f"[SECURITY-ATTEMPT] IP: {ip} | Razão: {reason} (Redis indisponível)"
        )
        return False

    now = datetime.now()
    key = f"{REDIS_PREFIX_ATTEMPTS}{ip}"

    # Buscar tentativas anteriores
    attempts_data = redis_service.get(key)
    attempts = []

    if attempts_data:
        try:
            attempts = json.loads(attempts_data)
            # Filtrar apenas tentativas recentes (últimos 10 minutos)
            attempts = [
                att for att in attempts
                if datetime.fromisoformat(att['timestamp']) > now - timedelta(minutes=10)
            ]
        except (json.JSONDecodeError, KeyError, ValueError):
            attempts = []

    # Adicionar nova tentativa
    attempts.append({
        'timestamp': now.isoformat(),
        'reason': reason
    })

    # Salvar no Redis com TTL de 10 minutos
    redis_service.set_with_ttl(
        key,
        json.dumps(attempts),
        ttl_seconds=ATTEMPTS_TTL
    )

    # Bloquear se muitas tentativas suspeitas (5 em 10 minutos)
    if len(attempts) >= 5:
        block_ip(ip, duration_minutes=60)
        security_logger.warning(
            f"[SECURITY-AUTO-BLOCK] IP {ip} bloqueado automaticamente | "
            f"Razão: {len(attempts)} tentativas suspeitas"
        )
        # Limpar tentativas (já bloqueado)
        redis_service.delete(key)
        return True

    # Log da tentativa
    security_logger.warning(
        f"[SECURITY-ATTEMPT] IP: {ip} | "
        f"Tentativa: {len(attempts)}/5 | "
        f"Razão: {reason}"
    )

    return False

def security_filter():
    """
    Middleware que filtra requisições suspeitas ANTES de chegar aos endpoints
    """
    ip = request.remote_addr
    path = request.path
    user_agent = request.headers.get('User-Agent', '')

    # 0. RESOLUÇÃO DE IP ATRÁS DE PROXY
    # Se o IP da requisição for confiável (ex: Nginx interno, Localhost),
    # tentamos descobrir o IP real do cliente via X-Forwarded-For.
    # Isso evita bloquear o Proxy (Self-DoS) e permite bloquear o cliente real.
    if is_trusted_ip(ip):
        x_forwarded_for = request.headers.get('X-Forwarded-For')
        if x_forwarded_for:
            # Pega o primeiro IP da lista (Client Real, Proxy1...)
            # Só confiamos nisso porque a origem (remote_addr) é confiável.
            ip = x_forwarded_for.split(',')[0].strip()
        else:
            # Se é confiável e não tem header (ex: Health Check local, Cron interno),
            # permitimos o acesso direto sem passar pelo filtro.
            return None

    # 1. PRIMEIRA VERIFICAÇÃO: Blacklist permanente
    if is_ip_blacklisted(ip):
        security_logger.warning(
            f"[SECURITY-BLACKLISTED] IP na blacklist tentou acessar | "
            f"IP: {ip} | Path: {path}"
        )
        return jsonify({
            'error': 'Access permanently denied',
            'message': 'Your IP has been permanently blocked'
        }), 403

    # 2. Verificar se IP está bloqueado temporariamente
    if is_ip_blocked(ip):
        security_logger.warning(
            f"[SECURITY-BLOCKED] IP bloqueado tentou acessar | "
            f"IP: {ip} | Path: {path}"
        )
        return jsonify({
            'error': 'Access denied',
            'message': 'Your IP has been temporarily blocked due to suspicious activity'
        }), 403

    # 3. Verificar se requisição é suspeita
    is_suspicious, reason = is_suspicious_request(path, user_agent)

    if is_suspicious:
        # Registrar tentativa e verificar se deve bloquear
        should_block = track_failed_attempt(ip, reason)

        # Se acabou de ser bloqueado, retornar 403
        if should_block:
            return jsonify({
                'error': 'Access denied',
                'message': 'Too many suspicious requests. IP blocked.'
            }), 403

        # Caso contrário, apenas retornar 404 (não revelar que detectamos)
        return jsonify({'error': 'Not found'}), 404

    # 4. Requisição válida, continuar
    return None

def get_security_stats():
    """
    Retorna estatísticas de segurança (para endpoint administrativo) usando Redis
    """
    if not redis_service.is_connected():
        return {
            'error': 'Redis não disponível',
            'blacklisted_ips': [],
            'blocked_ips': [],
            'suspicious_activity': [],
            'total_blacklisted': 0,
            'total_blocked': 0,
            'total_suspicious': 0
        }

    now = datetime.now()
    blacklisted_ips = []
    blocked_ips = []
    suspicious_activity = []

    # Buscar todos os IPs na blacklist permanente
    blacklist_keys = redis_service.get_keys_by_pattern(f"{REDIS_PREFIX_BLACKLIST}*")
    for key in blacklist_keys:
        ip = key.replace(REDIS_PREFIX_BLACKLIST, '')
        data_str = redis_service.get(key)

        if data_str:
            try:
                data = json.loads(data_str)
                blacklisted_ips.append({
                    'ip': ip,
                    'reason': data.get('reason', 'Unknown'),
                    'blacklisted_at': data.get('blacklisted_at', 'Unknown'),
                    'permanent': data.get('permanent', True)
                })
            except (json.JSONDecodeError, KeyError):
                continue

    # Buscar todos os IPs bloqueados temporariamente
    blocked_keys = redis_service.get_keys_by_pattern(f"{REDIS_PREFIX_BLOCKED}*")
    for key in blocked_keys:
        ip = key.replace(REDIS_PREFIX_BLOCKED, '')
        data_str = redis_service.get(key)

        if data_str:
            try:
                data = json.loads(data_str)
                blocked_until = datetime.fromisoformat(data['blocked_until'])

                if now < blocked_until:
                    blocked_ips.append({
                        'ip': ip,
                        'attempts': data.get('count', 1),
                        'blocked_until': blocked_until.isoformat(),
                        'time_remaining': str(blocked_until - now).split('.')[0]  # Remove microsegundos
                    })
            except (json.JSONDecodeError, KeyError, ValueError):
                continue

    # Buscar atividade suspeita (tentativas não bloqueadas ainda)
    attempt_keys = redis_service.get_keys_by_pattern(f"{REDIS_PREFIX_ATTEMPTS}*")
    for key in attempt_keys:
        ip = key.replace(REDIS_PREFIX_ATTEMPTS, '')
        attempts_str = redis_service.get(key)

        if attempts_str:
            try:
                attempts = json.loads(attempts_str)
                if attempts:
                    suspicious_activity.append({
                        'ip': ip,
                        'recent_attempts': len(attempts),
                        'last_attempt': attempts[-1]['timestamp'],
                        'reasons': [a['reason'] for a in attempts[-3:]]  # Últimas 3 razões
                    })
            except (json.JSONDecodeError, KeyError):
                continue

    return {
        'blacklisted_ips': blacklisted_ips,
        'blocked_ips': blocked_ips,
        'suspicious_activity': suspicious_activity,
        'total_blacklisted': len(blacklisted_ips),
        'total_blocked': len(blocked_ips),
        'total_suspicious': len(suspicious_activity),
        'redis_connected': True
    }
