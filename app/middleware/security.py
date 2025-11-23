# app/middleware/security.py
"""
Middleware de segurança para proteção contra bots, scanners e ataques
"""
import logging
import json
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

# TTL padrão para dados de segurança
BLOCKED_IP_TTL = 3600  # 1 hora em segundos
ATTEMPTS_TTL = 600  # 10 minutos em segundos

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
    '/',
    '/api/transacao',
    '/webhook-whatsapp',
    '/webhook-google-calendar',
    '/admin/setup-database',
    '/admin/populate-global-categories',
    '/admin/setup-user-data',
    '/admin/run-motor-agendamentos',
    '/admin/trigger-agenda-notifications',
    '/admin/trigger-bills-notifications',
    '/admin/trigger-monthly-reports-processing',
    '/admin/trigger-monthly-reports-generation',
    '/admin/trigger-monthly-reports-delivery',
    '/admin/trigger-daily-briefing',
    '/admin/get-notification-config',
    '/admin/oauth-config-check',
    '/admin/setup-potes-alerts',
}

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
    if path not in VALID_ENDPOINTS and not path.startswith('/admin/get-notification-config/'):
        return True, f"Unknown endpoint: {path}"

    return False, None

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

    # 1. Verificar se IP está bloqueado
    if is_ip_blocked(ip):
        security_logger.warning(
            f"[SECURITY-BLOCKED] IP bloqueado tentou acessar | "
            f"IP: {ip} | Path: {path}"
        )
        return jsonify({
            'error': 'Access denied',
            'message': 'Your IP has been temporarily blocked due to suspicious activity'
        }), 403

    # 2. Verificar se requisição é suspeita
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

    # 3. Requisição válida, continuar
    return None

def get_security_stats():
    """
    Retorna estatísticas de segurança (para endpoint administrativo) usando Redis
    """
    if not redis_service.is_connected():
        return {
            'error': 'Redis não disponível',
            'blocked_ips': [],
            'suspicious_activity': [],
            'total_blocked': 0,
            'total_suspicious': 0
        }

    now = datetime.now()
    blocked_ips = []
    suspicious_activity = []

    # Buscar todos os IPs bloqueados
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
        'blocked_ips': blocked_ips,
        'suspicious_activity': suspicious_activity,
        'total_blocked': len(blocked_ips),
        'total_suspicious': len(suspicious_activity),
        'redis_connected': True
    }
