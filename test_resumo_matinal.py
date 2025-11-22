#!/usr/bin/env python3
"""
Script de Teste: Resumo Matinal
Valida todas as funcionalidades implementadas
"""

import os
import sys
from datetime import date

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def print_section(title):
    """Imprime cabeçalho de seção"""
    print(f"\n{'='*60}")
    print(f"  {title}")
    print(f"{'='*60}\n")


def test_weather_service():
    """Testa integração com WeatherAPI"""
    print_section("Teste 1: Weather Service")

    from app.services.weather_service import WeatherService

    weather_service = WeatherService()

    # Teste 1: Buscar clima de São Paulo
    print("Buscando clima de São Paulo...")
    clima = weather_service.get_weather("São Paulo", "SP")

    if clima:
        print(f"✅ Clima obtido com sucesso!")
        print(f"   Temperatura: {clima['temperatura']}°C")
        print(f"   Condição: {clima['condicao']} {clima['emoji']}")
        print(f"   Descrição: {clima['descricao_completa']}")
    else:
        print("⚠️ Não foi possível obter clima (verifique WEATHER_API_KEY)")

    return bool(clima)


def test_location_service():
    """Testa serviço de localização"""
    print_section("Teste 2: Location Service")

    from app.services.location_service import LocationService
    from app import db_engine
    from sqlalchemy import text

    if not db_engine:
        print("❌ Banco de dados não configurado")
        return False

    # Buscar primeiro usuário
    with db_engine.connect() as conn:
        result = conn.execute(text("SELECT id, nome FROM Usuarios LIMIT 1")).fetchone()

        if not result:
            print("⚠️ Nenhum usuário cadastrado para testar")
            return False

        usuario_id = result.id
        print(f"Testando com usuário: {result.nome} (ID: {usuario_id})")

    # Teste: Atualizar localização
    print("\nAtualizando localização para 'Campinas, SP'...")
    sucesso, mensagem = LocationService.update_user_location(usuario_id, "Campinas", "SP")

    if sucesso:
        print(f"✅ {mensagem}")
    else:
        print(f"❌ {mensagem}")
        return False

    # Teste: Buscar localização
    print("\nBuscando localização...")
    cidade, estado = LocationService.get_user_location(usuario_id)

    if cidade and estado:
        print(f"✅ Localização: {cidade}, {estado}")
    else:
        print("❌ Falha ao buscar localização")
        return False

    # Reverter para São Paulo
    LocationService.update_user_location(usuario_id, "São Paulo", "SP")

    return True


def test_notification_config():
    """Testa configuração de notificações"""
    print_section("Teste 3: Notification Config")

    from app.services.notification_config_service import NotificationConfigService
    from app import db_engine
    from sqlalchemy import text

    if not db_engine:
        print("❌ Banco de dados não configurado")
        return False

    # Buscar primeiro usuário
    with db_engine.connect() as conn:
        result = conn.execute(text("SELECT id FROM Usuarios LIMIT 1")).fetchone()

        if not result:
            print("⚠️ Nenhum usuário cadastrado")
            return False

        usuario_id = result.id

    # Teste: Obter/criar configuração
    print(f"Obtendo configuração do usuário {usuario_id}...")
    config = NotificationConfigService.get_or_create_config(usuario_id)

    if config:
        print(f"✅ Configuração obtida:")
        print(f"   Resumo matinal ativo: {config.get('resumo_matinal_ativo')}")
        print(f"   Horário: {config.get('resumo_matinal_hora')}")
    else:
        print("❌ Falha ao obter configuração")
        return False

    # Teste: Atualizar configuração
    print("\nAtualizando horário para 08:00...")
    sucesso, mensagem = NotificationConfigService.update_resumo_matinal_config(
        usuario_id,
        ativo=True,
        hora="08:00"
    )

    if sucesso:
        print(f"✅ {mensagem}")
    else:
        print(f"❌ {mensagem}")
        return False

    return True


def test_daily_briefing():
    """Testa geração de resumo matinal"""
    print_section("Teste 4: Daily Briefing Service")

    from app.services.daily_briefing_service import DailyBriefingService
    from app import db_engine
    from sqlalchemy import text

    if not db_engine:
        print("❌ Banco de dados não configurado")
        return False

    # Buscar usuário com Google Calendar conectado
    with db_engine.connect() as conn:
        result = conn.execute(text("""
            SELECT u.id, u.nome
            FROM Usuarios u
            WHERE EXISTS (
                SELECT 1 FROM google_oauth_tokens
                WHERE user_id = u.id
            )
            LIMIT 1
        """)).fetchone()

        if not result:
            print("⚠️ Nenhum usuário com Google Calendar conectado")
            print("   Teste de integração com calendário ignorado")
            return True  # Não é erro crítico

        usuario_id = result.id
        print(f"Testando com usuário: {result.nome} (ID: {usuario_id})")

    # Teste: Preparar dados
    briefing_service = DailyBriefingService()

    print(f"\nPreparando resumo para hoje ({date.today()})...")
    briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

    if briefing_data:
        print(f"✅ Dados preparados com sucesso:")
        print(f"   Total de eventos: {briefing_data['total_eventos']}")
        print(f"   Eventos remotos: {briefing_data['eventos_remotos']}")
        print(f"   Eventos presenciais: {briefing_data['eventos_presenciais']}")
        print(f"   Intervalos livres: {len(briefing_data['gaps'])}")

        if briefing_data['clima_principal']:
            print(f"   Clima: {briefing_data['clima_principal']['descricao_completa']}")

    else:
        print("❌ Falha ao preparar dados")
        return False

    return True


def test_gemini_integration():
    """Testa integração com Gemini"""
    print_section("Teste 5: Gemini Integration")

    from app.services.gemini_service import extract_location_config

    # Teste: Extrair localização
    print("Testando extração de localização...")

    test_messages = [
        "Configurar localização: Rio de Janeiro, RJ",
        "Minha cidade é Belo Horizonte MG",
        "Mudar localização para Curitiba, PR"
    ]

    for msg in test_messages:
        try:
            print(f"\nMensagem: '{msg}'")
            result = extract_location_config(msg)
            print(f"✅ Extraído: {result['cidade']}, {result['estado']}")
        except Exception as e:
            print(f"❌ Erro: {e}")
            return False

    return True


def main():
    """Executa todos os testes"""
    print("\n" + "="*60)
    print("  TESTE COMPLETO: RESUMO MATINAL")
    print("="*60)

    results = {
        "Weather Service": test_weather_service(),
        "Location Service": test_location_service(),
        "Notification Config": test_notification_config(),
        "Daily Briefing": test_daily_briefing(),
        "Gemini Integration": test_gemini_integration()
    }

    # Sumário
    print_section("SUMÁRIO DOS TESTES")

    total = len(results)
    passed = sum(1 for v in results.values() if v)

    for test_name, result in results.items():
        status = "✅ PASSOU" if result else "❌ FALHOU"
        print(f"{test_name:.<40} {status}")

    print(f"\n{'='*60}")
    print(f"  RESULTADO: {passed}/{total} testes passaram")
    print(f"{'='*60}\n")

    if passed == total:
        print("🎉 Todos os testes passaram! Feature pronta para uso.")
        return 0
    else:
        print("⚠️ Alguns testes falharam. Verifique as mensagens acima.")
        return 1


if __name__ == "__main__":
    try:
        exit_code = main()
        sys.exit(exit_code)
    except Exception as e:
        print(f"\n❌ ERRO CRÍTICO: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)
