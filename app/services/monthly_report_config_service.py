"""
Serviço de configuração de relatórios mensais automáticos.
Gerencia preferências de envio de relatórios para cada usuário.
"""

from datetime import datetime, time, timedelta
from zoneinfo import ZoneInfo
from sqlalchemy import text
from app import db_engine
from app.utils import with_db_retry

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
MOMENTOS_VALIDOS = ['INICIO_MES', 'FIM_MES']


@with_db_retry
def criar_tabela_monthly_report_configs():
    """
    Cria a tabela de configurações de relatórios mensais se não existir.

    Returns:
        bool: True se criada com sucesso, False se já existia
    """
    create_table_sql = text("""
        CREATE TABLE IF NOT EXISTS MonthlyReportConfigs (
            usuario_id INT PRIMARY KEY,
            ativo BIT DEFAULT 1,
            momento_envio VARCHAR(20) DEFAULT 'INICIO_MES'
                CHECK (momento_envio IN ('INICIO_MES', 'FIM_MES')),
            hora_envio TIME DEFAULT '08:00:00',
            created_at DATETIME DEFAULT GETDATE(),
            updated_at DATETIME DEFAULT GETDATE(),
            FOREIGN KEY (usuario_id) REFERENCES Usuarios(id) ON DELETE CASCADE
        )
    """)

    with db_engine.connect() as conn:
        conn.execute(create_table_sql)
        conn.commit()

    return True


@with_db_retry
def get_or_create_config(usuario_id: int) -> dict:
    """
    Obtém a configuração de relatório mensal do usuário ou cria uma padrão.

    Args:
        usuario_id: ID do usuário

    Returns:
        dict: Configuração do usuário com chaves:
            - usuario_id
            - ativo
            - momento_envio ('INICIO_MES' ou 'FIM_MES')
            - hora_envio (time)
            - created_at
            - updated_at
    """
    select_sql = text("""
        SELECT usuario_id, ativo, momento_envio, hora_envio,
               created_at, updated_at
        FROM MonthlyReportConfigs
        WHERE usuario_id = :usuario_id
    """)

    with db_engine.connect() as conn:
        result = conn.execute(select_sql, {"usuario_id": usuario_id}).fetchone()

        if result:
            return {
                'usuario_id': result[0],
                'ativo': bool(result[1]),
                'momento_envio': result[2],
                'hora_envio': result[3],
                'created_at': result[4],
                'updated_at': result[5]
            }

        # Criar configuração padrão
        insert_sql = text("""
            INSERT INTO MonthlyReportConfigs
                (usuario_id, ativo, momento_envio, hora_envio)
            VALUES
                (:usuario_id, 1, 'INICIO_MES', '08:00:00')
        """)

        conn.execute(insert_sql, {"usuario_id": usuario_id})
        conn.commit()

        # Retornar configuração recém-criada
        result = conn.execute(select_sql, {"usuario_id": usuario_id}).fetchone()

        return {
            'usuario_id': result[0],
            'ativo': bool(result[1]),
            'momento_envio': result[2],
            'hora_envio': result[3],
            'created_at': result[4],
            'updated_at': result[5]
        }


@with_db_retry
def update_config(usuario_id: int, ativo: bool = None,
                  momento_envio: str = None, hora_envio: str = None) -> dict:
    """
    Atualiza a configuração de relatório mensal do usuário.

    Args:
        usuario_id: ID do usuário
        ativo: Se o relatório está ativo (opcional)
        momento_envio: 'INICIO_MES' ou 'FIM_MES' (opcional)
        hora_envio: Hora no formato 'HH:MM' ou 'HH:MM:SS' (opcional)

    Returns:
        dict: Configuração atualizada

    Raises:
        ValueError: Se momento_envio ou hora_envio forem inválidos
    """
    # Validações
    if momento_envio and momento_envio not in MOMENTOS_VALIDOS:
        raise ValueError(f"momento_envio deve ser {' ou '.join(MOMENTOS_VALIDOS)}")

    if hora_envio:
        try:
            # Valida formato de hora
            if len(hora_envio) == 5:  # HH:MM
                hora_envio = f"{hora_envio}:00"
            datetime.strptime(hora_envio, '%H:%M:%S')
        except ValueError:
            raise ValueError("hora_envio deve estar no formato 'HH:MM' ou 'HH:MM:SS'")

    # Garantir que config existe
    get_or_create_config(usuario_id)

    # Construir SQL dinamicamente
    updates = []
    params = {"usuario_id": usuario_id}

    if ativo is not None:
        updates.append("ativo = :ativo")
        params["ativo"] = 1 if ativo else 0

    if momento_envio:
        updates.append("momento_envio = :momento_envio")
        params["momento_envio"] = momento_envio

    if hora_envio:
        updates.append("hora_envio = :hora_envio")
        params["hora_envio"] = hora_envio

    if updates:
        updates.append("updated_at = GETDATE()")

        update_sql = text(f"""
            UPDATE MonthlyReportConfigs
            SET {', '.join(updates)}
            WHERE usuario_id = :usuario_id
        """)

        with db_engine.connect() as conn:
            conn.execute(update_sql, params)
            conn.commit()

    return get_or_create_config(usuario_id)


@with_db_retry
def get_users_to_notify(momento_envio: str, janela_minutos: int = 5) -> list:
    """
    Retorna usuários que devem receber o relatório no momento atual.
    Considera uma janela de tempo (±janela_minutos) para evitar perder envios.

    Args:
        momento_envio: 'INICIO_MES' ou 'FIM_MES'
        janela_minutos: Tolerância em minutos (padrão: 5)

    Returns:
        list: Lista de dicts com dados dos usuários:
            - usuario_id
            - nome
            - numero_whatsapp
            - hora_envio
    """
    if momento_envio not in MOMENTOS_VALIDOS:
        raise ValueError(f"momento_envio deve ser {' ou '.join(MOMENTOS_VALIDOS)}")

    # Horário atual no Brasil
    agora = datetime.now(TIMEZONE_BR)
    hora_atual = agora.time()

    # Calcular janela de tempo
    hora_min = (datetime.combine(datetime.today(), hora_atual) -
                timedelta(minutes=janela_minutos)).time()
    hora_max = (datetime.combine(datetime.today(), hora_atual) +
                timedelta(minutes=janela_minutos)).time()

    query_sql = text("""
        SELECT
            u.id AS usuario_id,
            u.nome,
            u.numero_whatsapp,
            mrc.hora_envio
        FROM Usuarios u
        INNER JOIN MonthlyReportConfigs mrc ON u.id = mrc.usuario_id
        WHERE mrc.ativo = 1
          AND mrc.momento_envio = :momento_envio
          AND mrc.hora_envio BETWEEN :hora_min AND :hora_max
        ORDER BY u.id
    """)

    with db_engine.connect() as conn:
        results = conn.execute(query_sql, {
            "momento_envio": momento_envio,
            "hora_min": hora_min,
            "hora_max": hora_max
        }).fetchall()

        return [
            {
                'usuario_id': row[0],
                'nome': row[1],
                'numero_whatsapp': row[2],
                'hora_envio': row[3]
            }
            for row in results
        ]


@with_db_retry
def desativar_config(usuario_id: int) -> dict:
    """
    Desativa o envio automático de relatórios mensais para o usuário.

    Args:
        usuario_id: ID do usuário

    Returns:
        dict: Configuração atualizada
    """
    return update_config(usuario_id, ativo=False)


@with_db_retry
def ativar_config(usuario_id: int) -> dict:
    """
    Ativa o envio automático de relatórios mensais para o usuário.

    Args:
        usuario_id: ID do usuário

    Returns:
        dict: Configuração atualizada
    """
    return update_config(usuario_id, ativo=True)
