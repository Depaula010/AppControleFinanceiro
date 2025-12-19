# app/services/__init__.py
"""
Modulo de services - logica de negocio da aplicacao.

IMPORTANTE: Nao importar services aqui para evitar import circular!
Os services devem ser importados diretamente onde necessarios:

    from app.services import finance_service
    from app.services import gemini_service
    from app.services.notification_service import enviar_notificacao_whatsapp
"""
