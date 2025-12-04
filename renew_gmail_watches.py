#!/usr/bin/env python3
"""
Cron job para renovar Gmail watches antes da expiração.

Watches Gmail expiram em 7 dias e precisam ser renovados.
Este script deve rodar diariamente às 3 AM via Ofelia scheduler.

Schedule: 0 3 * * *
Command: python /app/renew_gmail_watches.py
"""

import os
import sys
from datetime import datetime, timedelta
from sqlalchemy import text

# Adicionar app ao path
sys.path.insert(0, os.path.dirname(__file__))

from app import create_app, db_engine
from app.services.google_calendar_oauth_service import GoogleOAuthService

def renew_watches():
    """Renova Gmail watches para usuários com watches expirando"""

    print(f"[WATCH-RENEW] ========================================")
    print(f"[WATCH-RENEW] Iniciando renovação às {datetime.now()}")
    print(f"[WATCH-RENEW] ========================================")

    if not db_engine:
        print("[WATCH-RENEW] ❌ ERRO: Database não configurado")
        return

    try:
        # Importar configuração
        from app.config import GMAIL_WATCH_RENEWAL_HOURS

        # Calcular threshold (agora + buffer de renovação)
        threshold_ms = int(
            (datetime.now() + timedelta(hours=GMAIL_WATCH_RENEWAL_HOURS)).timestamp() * 1000
        )

        print(f"[WATCH-RENEW] Threshold: {threshold_ms} ({datetime.fromtimestamp(threshold_ms/1000)})")

        with db_engine.connect() as conn:
            # Buscar usuários com watches expirando
            sql = text("""
                SELECT usuario_id, gmail_watch_expiration
                FROM GoogleTokens
                WHERE gmail_watch_expiration IS NOT NULL
                  AND gmail_watch_expiration < :threshold
            """)

            results = conn.execute(sql, {"threshold": threshold_ms}).fetchall()

            if not results:
                print(f"[WATCH-RENEW] ✅ Nenhum watch precisa de renovação")
                print(f"[WATCH-RENEW] (Threshold: {datetime.fromtimestamp(threshold_ms/1000)})")
                return

            print(f"[WATCH-RENEW] 📋 Encontrados {len(results)} watches para renovar")

            # Renovar cada watch
            success_count = 0
            failed_users = []

            for row in results:
                usuario_id = row.usuario_id
                old_expiration = row.gmail_watch_expiration

                old_expiry_date = datetime.fromtimestamp(old_expiration / 1000)
                print(f"\n[WATCH-RENEW] 🔄 Usuário {usuario_id} (expira: {old_expiry_date})")

                try:
                    # Chamar Gmail API para renovar watch
                    new_expiration = GoogleOAuthService.setup_gmail_watch(usuario_id)

                    # Calcular dias até nova expiração
                    days_until_expiry = (
                        datetime.fromtimestamp(new_expiration / 1000) - datetime.now()
                    ).days

                    print(f"[WATCH-RENEW] ✅ Watch renovado (expira em {days_until_expiry} dias)")
                    success_count += 1

                except Exception as e:
                    print(f"[WATCH-RENEW] ❌ Falha ao renovar watch: {e}")
                    import traceback
                    traceback.print_exc()
                    failed_users.append(usuario_id)

            # Relatório final
            print(f"\n[WATCH-RENEW] ========================================")
            print(f"[WATCH-RENEW] ✅ Sucesso: {success_count}/{len(results)}")
            if failed_users:
                print(f"[WATCH-RENEW] ❌ Falhas: {failed_users}")
                print(f"[WATCH-RENEW] ⚠️  ALERTA: {len(failed_users)} watches NÃO foram renovados!")
            print(f"[WATCH-RENEW] Concluído às {datetime.now()}")
            print(f"[WATCH-RENEW] ========================================")

    except Exception as e:
        print(f"[WATCH-RENEW] ❌ ERRO CRÍTICO: {e}")
        import traceback
        traceback.print_exc()

if __name__ == '__main__':
    # Criar contexto Flask app (necessário para db_engine)
    app = create_app()
    with app.app_context():
        renew_watches()
