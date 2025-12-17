"""
Módulo Admin - Rotas administrativas.

Organizado em sub-módulos por responsabilidade:
- cache_management: Gerenciamento de cache Gemini
- security: Blacklist e estatísticas de segurança
- notification_config: Configurações de notificações

Mantém compatibilidade com importações antigas via admin_bp principal.
"""

from flask import Blueprint

# Criar blueprint principal do admin
admin_bp = Blueprint('admin', __name__, url_prefix='/admin')

# Importar e registrar sub-blueprints
from .cache_management import cache_bp
from .security import security_bp
from .notification_config import notification_config_bp

# Registrar todos os sub-blueprints no blueprint principal
admin_bp.register_blueprint(cache_bp)
admin_bp.register_blueprint(security_bp)
admin_bp.register_blueprint(notification_config_bp)

# Exportar para manter compatibilidade
__all__ = ['admin_bp']
