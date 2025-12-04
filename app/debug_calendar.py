# debug_calendar.py
# Script para testar Calendar e encontrar o erro exato

from datetime import datetime, date, timezone, time
from app import create_app
from app.services.google_calendar_oauth_service import GoogleOAuthService

app = create_app()

with app.app_context():
    print("=" * 60)
    print("🧪 TESTE DE DEBUG - GOOGLE CALENDAR")
    print("=" * 60)
    
    usuario_id = 1
    
    # TESTE 1: Verificar se usuário está conectado
    print("\n[TESTE 1] Verificando conexão...")
    try:
        is_connected = GoogleOAuthService.is_user_connected(usuario_id)
        print(f"✅ Usuário conectado? {is_connected}")
    except Exception as e:
        print(f"❌ Erro ao verificar conexão: {e}")
        import traceback
        traceback.print_exc()
        exit(1)
    
    if not is_connected:
        print("❌ Usuário não conectado. Conecte primeiro!")
        exit(1)
    
    # TESTE 2: Obter credenciais
    print("\n[TESTE 2] Obtendo credenciais...")
    try:
        credentials = GoogleOAuthService.get_credentials(usuario_id)
        if credentials:
            print(f"✅ Credenciais obtidas")
            print(f"   - Token: {credentials.token[:20]}...")
            print(f"   - Expiry: {credentials.expiry}")
            print(f"   - Expiry type: {type(credentials.expiry)}")
            print(f"   - Expiry tzinfo: {credentials.expiry.tzinfo if credentials.expiry else 'None'}")
        else:
            print("❌ Credenciais não encontradas")
            exit(1)
    except Exception as e:
        print(f"❌ Erro ao obter credenciais: {e}")
        import traceback
        traceback.print_exc()
        exit(1)
    
    # TESTE 3: Criar serviço
    print("\n[TESTE 3] Criando serviço Calendar...")
    try:
        service = GoogleOAuthService.get_calendar_service(usuario_id)
        print(f"✅ Serviço criado com sucesso")
    except Exception as e:
        print(f"❌ Erro ao criar serviço: {e}")
        import traceback
        traceback.print_exc()
        exit(1)
    
    # TESTE 4: Listar calendários (teste simples)
    print("\n[TESTE 4] Listando calendários...")
    try:
        calendars = service.calendarList().list().execute()
        print(f"✅ {len(calendars.get('items', []))} calendários encontrados:")
        for cal in calendars.get('items', [])[:3]:
            print(f"   - {cal.get('summary')}")
    except Exception as e:
        print(f"❌ Erro ao listar calendários: {e}")
        import traceback
        traceback.print_exc()
        exit(1)
    
    # TESTE 5: Buscar eventos de HOJE (onde está o erro)
    print("\n[TESTE 5] Buscando eventos de HOJE...")
    try:
        hoje = date.today()
        print(f"   Data: {hoje}")
        
        # Criar datetime timezone-aware
        start_dt = datetime.combine(hoje, time.min)
        start_of_day = start_dt.replace(tzinfo=timezone.utc)
        
        end_dt = datetime.combine(hoje, time.max)
        end_of_day = end_dt.replace(tzinfo=timezone.utc)
        
        print(f"   Start: {start_of_day}")
        print(f"   Start type: {type(start_of_day)}")
        print(f"   Start tzinfo: {start_of_day.tzinfo}")
        
        print(f"   End: {end_of_day}")
        print(f"   End type: {type(end_of_day)}")
        print(f"   End tzinfo: {end_of_day.tzinfo}")
        
        # Converter para ISO
        start_iso = start_of_day.isoformat()
        end_iso = end_of_day.isoformat()
        
        print(f"   Start ISO: {start_iso}")
        print(f"   End ISO: {end_iso}")
        
        # CHAMAR API
        print("\n   Chamando API do Google Calendar...")
        events_result = service.events().list(
            calendarId='primary',
            timeMin=start_iso,
            timeMax=end_iso,
            singleEvents=True,
            orderBy='startTime'
        ).execute()
        
        events = events_result.get('items', [])
        print(f"✅ API retornou {len(events)} eventos")
        
        if events:
            print("\n   Eventos encontrados:")
            for idx, event in enumerate(events[:3], 1):
                print(f"\n   Evento {idx}:")
                print(f"      - Summary: {event.get('summary')}")
                print(f"      - Start: {event['start']}")
                print(f"      - Start type: {type(event['start'])}")
                
                # AQUI PODE ESTAR O ERRO
                start_value = event['start'].get('dateTime', event['start'].get('date'))
                print(f"      - Start value: {start_value}")
                print(f"      - Start value type: {type(start_value)}")
        else:
            print("   ℹ️ Nenhum evento encontrado hoje")
        
        print("\n✅ TESTE 5 COMPLETO SEM ERROS!")
        
    except Exception as e:
        print(f"\n❌ ERRO NO TESTE 5: {e}")
        print(f"   Tipo: {type(e).__name__}")
        import traceback
        print("\n📍 TRACEBACK COMPLETO:")
        traceback.print_exc()
        exit(1)
    
    print("\n" + "=" * 60)
    print("✅ TODOS OS TESTES PASSARAM!")
    print("=" * 60)