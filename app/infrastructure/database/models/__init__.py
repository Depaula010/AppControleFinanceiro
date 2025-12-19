# app/infrastructure/database/models/__init__.py
"""
Módulo de modelos SQLAlchemy ORM.

Este módulo exporta todos os modelos ORM para facilitar imports e uso com Alembic.

Usage:
    from app.infrastructure.database.models import UserModel, AccountModel, TransactionModel
    from app.infrastructure.database.models import Base

Para Alembic (migrations):
    from app.infrastructure.database.models import Base
    target_metadata = Base.metadata
"""

# Base classes
from .base import Base, TimestampMixin

# Modelos principais
from .user_model import UserModel
from .account_model import AccountModel
from .transaction_model import TransactionModel
from .invoice_model import InvoiceModel

# Modelos de categorização
from .category_group_model import CategoryGroupModel
from .macro_category_model import MacroCategoryModel
from .sub_category_model import SubCategoryModel

# Modelos de agendamento e orçamento
from .schedule_model import ScheduleModel
from .budget_pot_model import BudgetPotModel, pote_subcategorias

# Modelos de configuração
from .notification_config_model import NotificationConfigModel
from .monthly_report_config_model import MonthlyReportConfigModel
from .google_calendar_token_model import GoogleCalendarTokenModel

# Modelos de segurança e compliance
from .consent_model import ConsentModel
from .baileys_auth_model import BaileysAuthModel

__all__ = [
    # Base classes
    "Base",
    "TimestampMixin",

    # Modelos principais
    "UserModel",
    "AccountModel",
    "TransactionModel",
    "InvoiceModel",

    # Modelos de categorização
    "CategoryGroupModel",
    "MacroCategoryModel",
    "SubCategoryModel",

    # Modelos de agendamento e orçamento
    "ScheduleModel",
    "BudgetPotModel",
    "pote_subcategorias",

    # Modelos de configuração
    "NotificationConfigModel",
    "MonthlyReportConfigModel",
    "GoogleCalendarTokenModel",

    # Modelos de segurança e compliance
    "ConsentModel",
    "BaileysAuthModel",
]
