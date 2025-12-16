import os
import sys
from logging.config import fileConfig

from sqlalchemy import engine_from_config
from sqlalchemy import pool

from alembic import context

# Adicionar o diretório raiz ao PYTHONPATH para importar os modelos
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

# Carregar variáveis de ambiente do .env
from dotenv import load_dotenv
load_dotenv()

# this is the Alembic Config object, which provides
# access to the values within the .ini file in use.
config = context.config

# Configurar DATABASE_URL a partir da variável de ambiente
database_url = os.getenv('DATABASE_URL')
if database_url:
    config.set_main_option('sqlalchemy.url', database_url)

# Interpret the config file for Python logging.
# This line sets up loggers basically.
if config.config_file_name is not None:
    fileConfig(config.config_file_name)

# Importar Base e todos os modelos ORM
# IMPORTANTE: Os imports só funcionam quando rodando dentro do Docker
# onde Redis/PostgreSQL estão disponíveis. Para desenvolvimento local,
# comentar os imports abaixo e usar target_metadata = None
try:
    from app.infrastructure.database.models.base import Base

    # Importar TODOS os modelos ORM para que o Alembic possa detectá-los
    from app.infrastructure.database.models.user_model import UserModel
    from app.infrastructure.database.models.account_model import AccountModel
    from app.infrastructure.database.models.transaction_model import TransactionModel
    from app.infrastructure.database.models.invoice_model import InvoiceModel
    from app.infrastructure.database.models.category_group_model import CategoryGroupModel
    from app.infrastructure.database.models.macro_category_model import MacroCategoryModel
    from app.infrastructure.database.models.sub_category_model import SubCategoryModel
    from app.infrastructure.database.models.schedule_model import ScheduleModel
    from app.infrastructure.database.models.budget_pot_model import BudgetPotModel, pote_subcategorias
    from app.infrastructure.database.models.notification_config_model import NotificationConfigModel
    from app.infrastructure.database.models.monthly_report_config_model import MonthlyReportConfigModel
    from app.infrastructure.database.models.google_calendar_token_model import GoogleCalendarTokenModel
    from app.infrastructure.database.models.consent_model import ConsentModel
    from app.infrastructure.database.models.baileys_auth_model import BaileysAuthModel

    # Configurar metadata para autogenerate
    target_metadata = Base.metadata
except Exception as e:
    # Se estiver rodando localmente sem Docker, usar None
    # As migrações serão criadas manualmente
    print(f"[ALEMBIC] Aviso: Não foi possível importar modelos ({e}). Usando target_metadata=None")
    target_metadata = None

# other values from the config, defined by the needs of env.py,
# can be acquired:
# my_important_option = config.get_main_option("my_important_option")
# ... etc.


def run_migrations_offline() -> None:
    """Run migrations in 'offline' mode.

    This configures the context with just a URL
    and not an Engine, though an Engine is acceptable
    here as well.  By skipping the Engine creation
    we don't even need a DBAPI to be available.

    Calls to context.execute() here emit the given string to the
    script output.

    """
    url = config.get_main_option("sqlalchemy.url")
    context.configure(
        url=url,
        target_metadata=target_metadata,
        literal_binds=True,
        dialect_opts={"paramstyle": "named"},
    )

    with context.begin_transaction():
        context.run_migrations()


def run_migrations_online() -> None:
    """Run migrations in 'online' mode.

    In this scenario we need to create an Engine
    and associate a connection with the context.

    """
    connectable = engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )

    with connectable.connect() as connection:
        context.configure(
            connection=connection, target_metadata=target_metadata
        )

        with context.begin_transaction():
            context.run_migrations()


if context.is_offline_mode():
    run_migrations_offline()
else:
    run_migrations_online()
