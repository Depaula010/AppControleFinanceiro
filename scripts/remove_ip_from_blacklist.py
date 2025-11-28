#!/usr/bin/env python3
"""
Script para remover IP da blacklist de segurança

Uso:
    python remove_ip_from_blacklist.py 172.19.0.6

Ou dentro do Docker:
    docker exec -it meu-secretario-api python scripts/remove_ip_from_blacklist.py 172.19.0.6
"""

import sys
import os
import json
import redis
from datetime import datetime

# Configurações
REDIS_PREFIX_BLACKLIST = 'security:blacklist:'

def get_redis_connection():
    """Conecta ao Redis usando a URL do ambiente"""
    redis_url = os.getenv('REDIS_URL', 'redis://localhost:6379/0')
    print(f"[INFO] Conectando ao Redis: {redis_url}")

    try:
        r = redis.from_url(redis_url, decode_responses=True)
        r.ping()
        print("[OK] Conectado ao Redis com sucesso!")
        return r
    except Exception as e:
        print(f"[ERRO] Falha ao conectar ao Redis: {e}")
        return None

def list_blacklisted_ips(r):
    """Lista todos os IPs na blacklist"""
    print("\n" + "="*60)
    print("IPs NA BLACKLIST")
    print("="*60)

    pattern = f"{REDIS_PREFIX_BLACKLIST}*"
    keys = r.keys(pattern)

    if not keys:
        print("Nenhum IP na blacklist.")
        return []

    blacklisted_ips = []
    for key in keys:
        ip = key.replace(REDIS_PREFIX_BLACKLIST, '')
        data_str = r.get(key)

        if data_str:
            try:
                data = json.loads(data_str)
                blacklisted_ips.append({
                    'ip': ip,
                    'reason': data.get('reason', 'Unknown'),
                    'blacklisted_at': data.get('blacklisted_at', 'Unknown')
                })

                print(f"\nIP: {ip}")
                print(f"  Razão: {data.get('reason', 'Unknown')}")
                print(f"  Bloqueado em: {data.get('blacklisted_at', 'Unknown')}")
            except json.JSONDecodeError:
                print(f"\nIP: {ip} (dados corrompidos)")

    print("\n" + "="*60)
    print(f"Total: {len(blacklisted_ips)} IP(s) na blacklist")
    print("="*60 + "\n")

    return blacklisted_ips

def remove_ip_from_blacklist(r, ip):
    """Remove um IP da blacklist"""
    key = f"{REDIS_PREFIX_BLACKLIST}{ip}"

    # Verificar se existe
    if not r.exists(key):
        print(f"[AVISO] IP {ip} não está na blacklist.")
        return False

    # Buscar dados antes de remover (para log)
    data_str = r.get(key)
    reason = "Unknown"
    blacklisted_at = "Unknown"

    if data_str:
        try:
            data = json.loads(data_str)
            reason = data.get('reason', 'Unknown')
            blacklisted_at = data.get('blacklisted_at', 'Unknown')
        except json.JSONDecodeError:
            pass

    # Remover
    result = r.delete(key)

    if result:
        print(f"[SUCESSO] IP {ip} removido da blacklist!")
        print(f"  Razão original: {reason}")
        print(f"  Bloqueado em: {blacklisted_at}")
        print(f"  Removido em: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        return True
    else:
        print(f"[ERRO] Falha ao remover IP {ip}")
        return False

def main():
    print("="*60)
    print("SCRIPT: Remover IP da Blacklist")
    print("="*60)

    # Conectar ao Redis
    r = get_redis_connection()
    if not r:
        print("\n[ERRO] Não foi possível conectar ao Redis. Script abortado.")
        sys.exit(1)

    # Se nenhum IP foi fornecido, listar todos
    if len(sys.argv) < 2:
        print("\n[INFO] Nenhum IP fornecido. Listando todos os IPs na blacklist...\n")
        list_blacklisted_ips(r)
        print("\nUso: python remove_ip_from_blacklist.py <IP>")
        print("Exemplo: python remove_ip_from_blacklist.py 172.19.0.6")
        sys.exit(0)

    # IP a remover
    ip_to_remove = sys.argv[1]

    print(f"\n[INFO] Removendo IP: {ip_to_remove}\n")

    # Listar antes
    print("ANTES:")
    list_blacklisted_ips(r)

    # Remover
    success = remove_ip_from_blacklist(r, ip_to_remove)

    if success:
        # Listar depois
        print("\nDEPOIS:")
        list_blacklisted_ips(r)
        print("\n[OK] Operação concluída com sucesso!")
        sys.exit(0)
    else:
        print("\n[ERRO] Operação falhou.")
        sys.exit(1)

if __name__ == "__main__":
    main()
