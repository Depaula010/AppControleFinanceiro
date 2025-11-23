#!/usr/bin/env python3
"""
Script de teste de segurança - NÃO EXECUTAR EM PRODUÇÃO

Este script testa os mecanismos de proteção contra bots e rate limiting.
Use apenas em ambiente de desenvolvimento local.

Uso:
    python test_security.py
"""

import requests
import time
from datetime import datetime

# Configurações
BASE_URL = "http://localhost:8000"
API_KEY = "sua_chave_api_aqui"  # Configure conforme seu .env

def print_header(text):
    """Imprime um cabeçalho formatado"""
    print("\n" + "=" * 60)
    print(f"  {text}")
    print("=" * 60 + "\n")

def test_valid_endpoint():
    """Teste 1: Endpoint válido deve funcionar"""
    print_header("TESTE 1: Endpoint válido")

    response = requests.get(f"{BASE_URL}/")
    print(f"Status: {response.status_code}")
    print(f"Resposta: {response.text[:100]}...")

    assert response.status_code == 200, "Endpoint válido falhou"
    print("✅ PASSOU - Endpoint válido funcionando")

def test_suspicious_urls():
    """Teste 2: URLs suspeitas devem retornar 404"""
    print_header("TESTE 2: URLs suspeitas")

    suspicious_urls = [
        "/.env",
        "/wp-admin",
        "/phpMyAdmin",
        "/admin/login.php",
        "/.git/config",
    ]

    for url in suspicious_urls:
        response = requests.get(f"{BASE_URL}{url}")
        print(f"URL: {url:<30} Status: {response.status_code}")

        assert response.status_code == 404, f"URL suspeita não bloqueada: {url}"

    print("✅ PASSOU - URLs suspeitas bloqueadas")

def test_auto_block():
    """Teste 3: Múltiplas tentativas suspeitas devem bloquear IP"""
    print_header("TESTE 3: Bloqueio automático após múltiplas tentativas")

    print("Enviando 6 requisições suspeitas...")

    suspicious_urls = [
        "/.env",
        "/wp-admin",
        "/phpMyAdmin",
        "/.git/config",
        "/admin/login.php",
        "/shell.php",
    ]

    for i, url in enumerate(suspicious_urls, 1):
        response = requests.get(f"{BASE_URL}{url}")
        print(f"Tentativa {i}: {url:<25} Status: {response.status_code}")

        if response.status_code == 403:
            print(f"\n✅ PASSOU - IP bloqueado após {i} tentativas")
            print(f"Resposta: {response.json()}")
            return

        time.sleep(0.5)  # Pequeno delay entre requisições

    print("⚠️  AVISO - IP não foi bloqueado após 6 tentativas")

def test_blocked_ip_access():
    """Teste 4: IP bloqueado não deve acessar endpoints válidos"""
    print_header("TESTE 4: IP bloqueado não acessa endpoints válidos")

    # Primeiro bloquear o IP
    print("Bloqueando IP...")
    for _ in range(5):
        requests.get(f"{BASE_URL}/.env")

    # Tentar acessar endpoint válido
    print("\nTentando acessar endpoint válido...")
    response = requests.get(f"{BASE_URL}/")

    if response.status_code == 403:
        print(f"✅ PASSOU - IP bloqueado não pode acessar endpoints válidos")
        print(f"Resposta: {response.json()}")
    else:
        print(f"Status: {response.status_code}")
        print("⚠️  AVISO - IP bloqueado conseguiu acessar endpoint válido")

def test_security_stats():
    """Teste 5: Endpoint de estatísticas de segurança"""
    print_header("TESTE 5: Estatísticas de segurança")

    headers = {"x-api-key": API_KEY}
    response = requests.get(f"{BASE_URL}/admin/security-stats", headers=headers)

    print(f"Status: {response.status_code}")

    if response.status_code == 200:
        stats = response.json()
        print(f"\nEstatísticas:")
        print(f"  - IPs bloqueados: {stats.get('total_blocked', 0)}")
        print(f"  - Atividade suspeita: {stats.get('total_suspicious', 0)}")

        if stats.get('blocked_ips'):
            print(f"\nIPs bloqueados:")
            for ip_info in stats['blocked_ips']:
                print(f"  - {ip_info['ip']} (tentativas: {ip_info['attempts']})")

        print("\n✅ PASSOU - Endpoint de estatísticas funcionando")
    else:
        print(f"❌ FALHOU - Não foi possível obter estatísticas")
        print(f"Resposta: {response.text}")

def test_rate_limiting():
    """Teste 6: Rate limiting deve bloquear requisições excessivas"""
    print_header("TESTE 6: Rate limiting")

    print("Enviando 50 requisições válidas rapidamente...")
    print("(Este teste pode demorar um pouco)")

    blocked = False
    for i in range(1, 51):
        response = requests.get(f"{BASE_URL}/")

        if response.status_code == 429:  # Too Many Requests
            print(f"\n✅ PASSOU - Rate limiting ativado após {i} requisições")
            print(f"Resposta: {response.text[:100]}")
            blocked = True
            break

        if i % 10 == 0:
            print(f"  {i} requisições enviadas...")

    if not blocked:
        print("\n⚠️  AVISO - Rate limiting não ativado após 50 requisições")
        print("   (Pode ser que o limite seja maior)")

def run_all_tests():
    """Executa todos os testes"""
    print("\n" + "=" * 60)
    print("  TESTES DE SEGURANÇA - Meu Secretário API")
    print("=" * 60)
    print(f"\nBase URL: {BASE_URL}")
    print(f"Horário: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")

    tests = [
        ("Endpoints válidos", test_valid_endpoint),
        ("URLs suspeitas", test_suspicious_urls),
        ("Bloqueio automático", test_auto_block),
        ("IP bloqueado", test_blocked_ip_access),
        ("Estatísticas", test_security_stats),
        ("Rate limiting", test_rate_limiting),
    ]

    results = []

    for name, test_func in tests:
        try:
            test_func()
            results.append((name, "✅ PASSOU"))
        except AssertionError as e:
            results.append((name, f"❌ FALHOU: {e}"))
        except Exception as e:
            results.append((name, f"⚠️  ERRO: {e}"))

        time.sleep(1)  # Delay entre testes

    # Resumo
    print_header("RESUMO DOS TESTES")
    for name, result in results:
        print(f"{name:<30} {result}")

    print("\n" + "=" * 60)
    print("  TESTES CONCLUÍDOS")
    print("=" * 60 + "\n")

if __name__ == "__main__":
    print("\n⚠️  ATENÇÃO: Este script deve ser executado apenas em ambiente local!")
    print("⚠️  NÃO execute em produção, pois pode bloquear seu próprio IP.\n")

    try:
        run_all_tests()
    except KeyboardInterrupt:
        print("\n\n⚠️  Testes interrompidos pelo usuário")
    except requests.exceptions.ConnectionError:
        print(f"\n❌ ERRO: Não foi possível conectar a {BASE_URL}")
        print("   Certifique-se de que a aplicação está rodando localmente")
