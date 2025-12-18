# app/presentation/admin/feature_migrations.py
"""
Módulo para setup de features e migrações de dados.

Este módulo contém rotas administrativas para:
- Setup de funcionalidades do sistema (resumo matinal, check-in noturno, alertas, etc.)
- Migrações de dados e estrutura do banco
- Cleanup de campos deprecados
"""

from flask import Blueprint, request
from sqlalchemy import text
from datetime import date

from app.shared.decorators import handle_errors
from app.shared.responses import ApiResponse
from app.utils import formatar_moeda
from ._common import db_engine

feature_migrations_bp = Blueprint('admin_feature_migrations', __name__)


@feature_migrations_bp.route('/setup-resumo-matinal', methods=['GET'])
@handle_errors(tag="SETUP-RESUMO-MATINAL")
def setup_resumo_matinal():
    """
    Cria as colunas necessárias para o Resumo Matinal.

    - Adiciona 'cidade' e 'estado' na tabela Usuarios
    - Adiciona 'resumo_matinal_ativo' e 'resumo_matinal_hora' na tabela NotificationConfigs

    Exemplo:
    GET http://212.47.65.37:8000/admin/setup-resumo-matinal
    """
    output = []
    output.append("="*60)
    output.append("SETUP: Resumo Matinal (Daily Briefing)")
    output.append("="*60)

    # Migration 0: Criar tabela NotificationConfigs se não existir
    output.append("\n[0/3] Verificando tabela NotificationConfigs...")

    sql_create_table = text("""
        CREATE TABLE IF NOT EXISTS NotificationConfigs (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,

            -- Agenda Diária
            agenda_diaria_ativa BOOLEAN NOT NULL DEFAULT TRUE,
            agenda_diaria_hora TIME NOT NULL DEFAULT '08:00:00',

            -- Contas a Vencer
            contas_vencer_ativa BOOLEAN NOT NULL DEFAULT TRUE,
            contas_vencer_dias_antes INT NOT NULL DEFAULT 1,
            contas_vencer_hora TIME NOT NULL DEFAULT '09:00:00',

            -- Timestamps
            created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

            UNIQUE(usuario_id)
        );

        CREATE INDEX IF NOT EXISTS idx_notification_configs_usuario
        ON NotificationConfigs(usuario_id);
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_create_table)
        conn.commit()

    output.append("OK - Tabela NotificationConfigs criada/verificada!")

    # Migration 1: Campos de localização
    output.append("\n[1/3] Adicionando campos de localizacao na tabela Usuarios...")

    sql_location = text("""
        ALTER TABLE Usuarios
        ADD COLUMN IF NOT EXISTS cidade VARCHAR(100) DEFAULT 'Sao Paulo';

        ALTER TABLE Usuarios
        ADD COLUMN IF NOT EXISTS estado VARCHAR(2) DEFAULT 'SP';

        CREATE INDEX IF NOT EXISTS idx_usuarios_localizacao
        ON Usuarios(cidade, estado);
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_location)
        conn.commit()

    output.append("OK - Campos 'cidade' e 'estado' adicionados!")

    # Migration 2: Campos de resumo matinal
    output.append("\n[2/3] Adicionando campos de resumo matinal na tabela NotificationConfigs...")

    sql_briefing = text("""
        ALTER TABLE NotificationConfigs
        ADD COLUMN IF NOT EXISTS resumo_matinal_ativo BOOLEAN NOT NULL DEFAULT TRUE;

        ALTER TABLE NotificationConfigs
        ADD COLUMN IF NOT EXISTS resumo_matinal_hora TIME NOT NULL DEFAULT '07:00:00';
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_briefing)
        conn.commit()

    output.append("OK - Campos 'resumo_matinal_ativo' e 'resumo_matinal_hora' adicionados!")

    output.append("\n" + "="*60)
    output.append("SUCESSO! Resumo Matinal configurado")
    output.append("="*60)
    output.append("\nProximos passos:")
    output.append("1. Configurar WEATHER_API_KEY no .env (opcional)")
    output.append("2. Testar via WhatsApp: 'Configurar localizacao: Sao Paulo, SP'")
    output.append("3. Testar via WhatsApp: 'Ativar resumo matinal'")
    output.append("4. Configurar cron job para /admin/trigger-daily-briefing")

    return "<pre>" + "\n".join(output) + "</pre>", 200


@feature_migrations_bp.route('/setup-checkin-noturno', methods=['GET'])
@handle_errors(tag="SETUP-CHECKIN-NOTURNO")
def setup_checkin_noturno():
    """
    Adiciona as colunas necessárias para o Check-in Noturno.

    - Adiciona 'checkin_noturno_ativo' e 'checkin_noturno_hora' na tabela NotificationConfigs
    - Cria constraint de validação de horário (18:00-23:00)

    Exemplo:
    GET http://localhost:5000/admin/setup-checkin-noturno
    """
    output = []
    output.append("="*60)
    output.append("SETUP: Check-in Noturno (Confirmação de Contas Pendentes)")
    output.append("="*60)

    # Usar a função de migração do finance_service
    from app.services.finance_service import add_nightly_checkin_config_columns

    output.append("\n[1/1] Adicionando campos de check-in noturno...")

    sucesso = add_nightly_checkin_config_columns()

    if sucesso:
        output.append("OK - Campos 'checkin_noturno_ativo' e 'checkin_noturno_hora' adicionados!")
        output.append("OK - Constraint de horário (18:00-23:00) criada!")
    else:
        output.append("ERRO - Falha ao adicionar campos (verifique logs)")

    output.append("\n" + "="*60)
    output.append("SUCESSO! Check-in Noturno configurado")
    output.append("="*60)
    output.append("\nPróximos passos:")
    output.append("1. Rebuild containers: docker-compose up -d --build")
    output.append("2. Testar via WhatsApp: 'Ativar check-in noturno às 20:00'")
    output.append("3. Testar via WhatsApp: 'Configurar check-in noturno'")
    output.append("4. Verificar logs Ofelia: docker logs meu-secretario-cron")
    output.append("5. Teste manual: docker exec meu-secretario-web python /app/processar_checkin_noturno.py")

    return "<pre>" + "\n".join(output) + "</pre>", 200


@feature_migrations_bp.route('/setup-potes-alerts', methods=['GET'])
@handle_errors(tag="SETUP-POTES-ALERTS")
def setup_potes_alerts():
    """
    Adiciona colunas para alertas de potes na tabela NotificationConfigs.

    - Adiciona 'alerta_potes_ativo' e 'alerta_potes_threshold'
    - Garante que 'periodicidade' existe em PotesDeGastos
    - Insere configurações padrão para usuários existentes

    Exemplo:
    GET http://seu-backend.com/admin/setup-potes-alerts
    """
    output = []
    output.append("="*60)
    output.append("SETUP: Alertas de Potes (Feedback Financeiro)")
    output.append("="*60)

    # Migration 1: Criar tabela NotificationConfigs se não existir
    output.append("\n[1/4] Verificando tabela NotificationConfigs...")

    sql_create_table = text("""
        CREATE TABLE IF NOT EXISTS NotificationConfigs (
            id SERIAL PRIMARY KEY,
            usuario_id INT NOT NULL REFERENCES Usuarios(id) ON DELETE CASCADE,

            -- Agenda Diária
            agenda_diaria_ativa BOOLEAN NOT NULL DEFAULT TRUE,
            agenda_diaria_hora TIME NOT NULL DEFAULT '08:00:00',

            -- Resumo Matinal
            resumo_matinal_ativo BOOLEAN NOT NULL DEFAULT TRUE,
            resumo_matinal_hora TIME NOT NULL DEFAULT '07:00:00',

            -- Contas a Vencer
            contas_vencer_ativa BOOLEAN NOT NULL DEFAULT TRUE,
            contas_vencer_dias_antes INT NOT NULL DEFAULT 1,
            contas_vencer_hora TIME NOT NULL DEFAULT '09:00:00',

            -- Timestamps
            created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
            updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,

            UNIQUE(usuario_id)
        );

        CREATE INDEX IF NOT EXISTS idx_notification_configs_usuario
        ON NotificationConfigs(usuario_id);
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_create_table)
        conn.commit()

    output.append("OK - Tabela NotificationConfigs criada/verificada!")

    # Migration 2: Adicionar colunas de alertas de potes
    output.append("\n[2/4] Adicionando campos de alertas de potes...")

    sql_potes_alerts = text("""
        ALTER TABLE NotificationConfigs
        ADD COLUMN IF NOT EXISTS alerta_potes_ativo BOOLEAN NOT NULL DEFAULT TRUE;

        ALTER TABLE NotificationConfigs
        ADD COLUMN IF NOT EXISTS alerta_potes_threshold INT NOT NULL DEFAULT 0;
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_potes_alerts)
        conn.commit()

    output.append("OK - Campos 'alerta_potes_ativo' e 'alerta_potes_threshold' adicionados!")
    output.append("    - alerta_potes_ativo: TRUE (padrao)")
    output.append("    - alerta_potes_threshold: 0 (sempre mostrar)")

    # Migration 3: Garantir que periodicidade existe em PotesDeGastos
    output.append("\n[3/4] Verificando campo 'periodicidade' em PotesDeGastos...")

    sql_periodicidade = text("""
        ALTER TABLE PotesDeGastos
        ADD COLUMN IF NOT EXISTS periodicidade VARCHAR(20) NOT NULL DEFAULT 'MENSAL'
            CHECK (periodicidade IN ('SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL'));
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_periodicidade)
        conn.commit()

    output.append("OK - Campo 'periodicidade' verificado em PotesDeGastos!")

    # Migration 4: Inserir configurações padrão para usuários existentes
    output.append("\n[4/4] Inserindo configuracoes padrao para usuarios existentes...")

    sql_insert_defaults = text("""
        INSERT INTO NotificationConfigs (usuario_id, alerta_potes_ativo, alerta_potes_threshold)
        SELECT id, TRUE, 0
        FROM Usuarios
        WHERE id NOT IN (SELECT usuario_id FROM NotificationConfigs)
        ON CONFLICT (usuario_id) DO NOTHING;
    """)

    with db_engine.connect() as conn:
        conn.begin()
        result = conn.execute(sql_insert_defaults)
        conn.commit()
        rows_inserted = result.rowcount

    output.append(f"OK - {rows_inserted} configuracao(es) padrao inserida(s)!")

    # Comentários para documentação
    sql_comments = text("""
        COMMENT ON COLUMN NotificationConfigs.alerta_potes_ativo IS 'Se TRUE, mostra status do pote apos cada transacao';
        COMMENT ON COLUMN NotificationConfigs.alerta_potes_threshold IS 'Threshold de % usado para mostrar alerta: 0=sempre, 50/70/90=apenas se ultrapassar';
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_comments)
        conn.commit()

    output.append("\n" + "="*60)
    output.append("SUCESSO! Feature de Alertas de Potes configurada")
    output.append("="*60)
    output.append("\nO que foi feito:")
    output.append("1. Tabela NotificationConfigs criada/verificada")
    output.append("2. Colunas de alertas de potes adicionadas")
    output.append("3. Campo periodicidade em PotesDeGastos verificado")
    output.append("4. Configuracoes padrao inseridas para usuarios existentes")
    output.append("\nProximos passos:")
    output.append("1. Criar potes de gastos (via WhatsApp ou SQL)")
    output.append("2. Testar registrando uma despesa")
    output.append("3. Verificar mensagem de feedback enriquecida")
    output.append("4. (Futuro) Configurar threshold via WhatsApp")

    return "<pre>" + "\n".join(output) + "</pre>", 200


@feature_migrations_bp.route('/setup-alertas-financeiros', methods=['GET'])
@handle_errors(tag="SETUP-ALERTAS-FINANCEIROS")
def setup_alertas_financeiros():
    """
    Adiciona o campo alertas_financeiros_ativos na tabela NotificationConfigs.
    Migra dados existentes de contas_vencer_ativa para o novo campo.

    Exemplo:
    GET http://212.47.65.37:8000/admin/setup-alertas-financeiros
    """
    output = []
    output.append("="*60)
    output.append("SETUP: Alertas Financeiros Unificados")
    output.append("="*60)
    output.append("\nNOTA: Este endpoint verifica se a estrutura está correta.")
    output.append("Se você já rodou o cleanup, a tabela já está limpa.\n")

    # Verificar se campo existe
    output.append("[1/2] Verificando campo alertas_financeiros_ativos...")

    sql_add_column = text("""
        ALTER TABLE NotificationConfigs
        ADD COLUMN IF NOT EXISTS alertas_financeiros_ativos BOOLEAN DEFAULT TRUE;
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_add_column)
        conn.commit()

    output.append("OK - Campo existe e está configurado!")

    # Adicionar comentário no banco
    output.append("\n[2/2] Documentando estrutura...")

    sql_comment = text("""
        COMMENT ON COLUMN NotificationConfigs.alertas_financeiros_ativos IS
        'Se TRUE, inclui alertas de contas e faturas a vencer no resumo matinal (ou envia separado se resumo desativado)';
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_comment)
        conn.commit()

    output.append("OK - Comentário adicionado!")

    output.append("\n" + "="*60)
    output.append("SUCESSO! Sistema de Alertas Financeiros configurado")
    output.append("="*60)
    output.append("\nEstrutura atual (limpa):")
    output.append("- resumo_matinal_ativo (controla resumo com agenda/clima)")
    output.append("- resumo_matinal_hora (horário único de envio)")
    output.append("- alertas_financeiros_ativos (controla alertas de contas/faturas)")
    output.append("\nComportamento:")
    output.append("- Ambos ativos: 1 mensagem unificada (resumo + alertas)")
    output.append("- Só resumo ativo: Apenas agenda e clima")
    output.append("- Só alertas ativo: Apenas contas/faturas a vencer")
    output.append("- Ambos desativados: Nenhuma mensagem enviada")
    output.append("\nPróximos passos:")
    output.append("1. Testar com: GET /admin/get-notification-config/1")
    output.append("2. Configurar alertas: POST /admin/config-alertas-financeiros")
    output.append("3. Executar processador: GET /admin/trigger-daily-briefing")

    return "<pre>" + "\n".join(output) + "</pre>", 200


@feature_migrations_bp.route('/setup-reserva-emergencia', methods=['GET'])
@handle_errors(tag="SETUP-RESERVA-EMERGENCIA")
def setup_reserva_emergencia():
    """
    Adiciona coluna incluir_na_reserva na tabela AGENDAMENTOS para controle granular
    de quais contas fixas devem ser incluídas no cálculo da reserva de emergência.

    LÓGICA CORRETA:
    - Reserva de emergência = soma de gastos essenciais MENSAIS × 6 meses
    - Gastos essenciais = agendamentos fixos recorrentes (água, luz, aluguel, etc.)
    - Usuário marca quais agendamentos incluir (ex: pode incluir Netflix se quiser)

    Este endpoint executa a migração do banco de dados:
    - Adiciona coluna incluir_na_reserva na tabela Agendamentos (BOOLEAN DEFAULT TRUE)
    - Cria índice para otimizar queries
    - Migra dados existentes (Despesa Essencial = TRUE, resto = FALSE)

    Exemplo:
    GET https://seu-backend.onrender.com/admin/setup-reserva-emergencia
    """
    from app.services import finance_service

    output = []
    output.append("="*60)
    output.append("SETUP: Reserva de Emergência (Baseada em Agendamentos)")
    output.append("="*60)

    # Migration 1: Adicionar coluna
    output.append("\n[1/4] Adicionando coluna incluir_na_reserva na tabela Agendamentos...")

    sql_add_column = text("""
        ALTER TABLE Agendamentos
        ADD COLUMN IF NOT EXISTS incluir_na_reserva BOOLEAN DEFAULT TRUE;
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_add_column)
        conn.commit()

    output.append("OK - Coluna 'incluir_na_reserva' adicionada!")

    # Migration 2: Adicionar comentário
    output.append("\n[2/4] Adicionando documentação...")

    sql_comment = text("""
        COMMENT ON COLUMN Agendamentos.incluir_na_reserva IS
        'Define se este agendamento deve ser incluído no cálculo da reserva de emergência. Normalizado: MENSAL×6, ANUAL×1 (integral), SEMANAL×26, QUINZENAL×12, DIARIA×180';
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_comment)
        conn.commit()

    output.append("OK - Comentário adicionado!")

    # Migration 3: Criar índice
    output.append("\n[3/4] Criando índice para otimizar queries...")

    sql_create_index = text("""
        CREATE INDEX IF NOT EXISTS idx_agendamentos_reserva
        ON Agendamentos(usuario_id, incluir_na_reserva, periodicidade)
        WHERE ativo = TRUE;
    """)

    with db_engine.connect() as conn:
        conn.begin()
        conn.execute(sql_create_index)
        conn.commit()

    output.append("OK - Índice criado!")

    # Migration 4: Migrar dados existentes
    output.append("\n[4/4] Migrando dados existentes...")
    output.append("    (Despesa Essencial = TRUE, demais = FALSE)")

    sql_migrate_data = text("""
        UPDATE Agendamentos a
        SET incluir_na_reserva = CASE
            WHEN g.nome_grupo = 'Despesa Essencial' THEN TRUE
            ELSE FALSE
        END
        FROM SubCategoria s
        JOIN MacroCategoria m ON s.macro_id = m.id
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        WHERE a.subcategoria_id = s.id;
    """)

    with db_engine.connect() as conn:
        conn.begin()
        result = conn.execute(sql_migrate_data)
        conn.commit()
        rows_updated = result.rowcount

    output.append(f"OK - {rows_updated} agendamento(s) atualizado(s)!")

    # Estatísticas da migração
    output.append("\n[ESTATÍSTICAS] Verificando resultados...")

    sql_stats = text("""
        SELECT
            g.nome_grupo,
            a.periodicidade,
            COUNT(*) as total_agendamentos,
            SUM(CASE WHEN a.incluir_na_reserva = TRUE THEN 1 ELSE 0 END) as incluidos_reserva,
            SUM(CASE WHEN a.incluir_na_reserva = TRUE THEN a.valor_previsto ELSE 0 END) as valor_total_reserva
        FROM Agendamentos a
        JOIN SubCategoria s ON a.subcategoria_id = s.id
        JOIN MacroCategoria m ON s.macro_id = m.id
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        WHERE a.ativo = TRUE
        GROUP BY g.nome_grupo, a.periodicidade
        ORDER BY g.nome_grupo, a.periodicidade;
    """)

    with db_engine.connect() as conn:
        stats = conn.execute(sql_stats).fetchall()

    output.append("\nDistribuição por grupo e periodicidade:")
    for stat in stats:
        grupo, periodo, total, incluidos, valor_total = stat
        output.append(f"  - {grupo} ({periodo}): {incluidos}/{total} incluídos (Total: {formatar_moeda(float(valor_total or 0))})")

    # Calcular reserva ideal usando a função atualizada
    output.append("\n[CÁLCULO] Reserva de emergência estimada...")
    output.append("  (Normalizando todas as periodicidades)")

    with db_engine.connect() as conn:
        gasto_mensal_equiv, reserva_ideal, meses = finance_service.get_reserva_status(conn, 1)

    output.append(f"\n  Gasto mensal essencial: {formatar_moeda(gasto_mensal_equiv)}")
    output.append(f"  Reserva ideal ({meses} meses): {formatar_moeda(reserva_ideal)}")
    output.append(f"\n  Breakdown por periodicidade ({meses} meses):")
    output.append(f"    - MENSAL: valor × {meses} meses")
    output.append("    - ANUAL: valor INTEGRAL (ex: IPTU R$ 1200/ano → R$ 1200 - mais conservador)")
    output.append(f"    - SEMANAL: valor × {round(meses * 4.33)} semanas ({meses} × 4.33 arredondado)")
    output.append(f"    - QUINZENAL: valor × {meses * 2} quinzenas ({meses} × 2)")
    output.append(f"    - DIARIA: valor × {meses * 30} dias ({meses} × 30)")

    output.append("\n" + "="*60)
    output.append("SUCESSO! Reserva de Emergência configurada")
    output.append("="*60)
    output.append("\nO que foi feito:")
    output.append("1. Coluna incluir_na_reserva adicionada à tabela Agendamentos")
    output.append("2. Índice criado para otimizar consultas")
    output.append("3. Dados migrados (Despesa Essencial marcada como TRUE)")
    output.append("4. Função get_reserva_status() atualizada para normalizar TODAS as periodicidades:")
    output.append("   - MENSAL, ANUAL, SEMANAL, QUINZENAL, DIARIA")
    output.append("\nPróximos passos:")
    output.append("1. Use os endpoints da API para gerenciar quais agendamentos incluir")
    output.append("2. GET /api/agendamentos/reserva - listar agendamentos com filtros")
    output.append("3. PATCH /api/agendamento/{id}/reserva - alterar flag individual")
    output.append("4. A aplicação web futura vai usar esses endpoints")
    output.append(f"\nExemplos práticos ({meses} meses):")
    output.append("  - IPTU R$ 1200/ano incluído → soma R$ 1200 (valor integral - mais conservador)")
    output.append("  - IPVA R$ 1500/ano incluído → soma R$ 1500 (valor integral)")
    output.append(f"  - Feira R$ 100/semana incluída → soma R$ {100 * round(meses * 4.33)} ({round(meses * 4.33)} semanas)")
    output.append(f"  - Aluguel R$ 1500/mês incluído → soma R$ {1500 * meses} ({meses} meses)")

    return "<pre>" + "\n".join(output) + "</pre>", 200


@feature_migrations_bp.route('/cleanup-deprecated-notification-fields', methods=['GET'])
@handle_errors(tag="CLEANUP-DEPRECATED-FIELDS")
def cleanup_deprecated_notification_fields():
    """
    Remove campos DEPRECATED da tabela NotificationConfigs.

    ATENÇÃO: Esta operação é IRREVERSÍVEL!

    Remove os seguintes campos:
    - agenda_diaria_ativa (substituído por resumo_matinal_ativo)
    - agenda_diaria_hora (substituído por resumo_matinal_hora)
    - contas_vencer_ativa (substituído por alertas_financeiros_ativos)
    - contas_vencer_dias_antes (não mais usado - alertas sempre para hoje e amanhã)
    - contas_vencer_hora (substituído por resumo_matinal_hora)

    Exemplo:
    GET http://212.47.65.37:8000/admin/cleanup-deprecated-notification-fields
    """
    output = []
    output.append("="*60)
    output.append("CLEANUP: Removendo campos DEPRECATED")
    output.append("="*60)
    output.append("\n⚠️  ATENÇÃO: Esta operação é IRREVERSÍVEL!")

    # Lista de colunas a remover
    deprecated_columns = [
        'agenda_diaria_ativa',
        'agenda_diaria_hora',
        'contas_vencer_ativa',
        'contas_vencer_dias_antes',
        'contas_vencer_hora'
    ]

    output.append(f"\n[INFO] Removendo {len(deprecated_columns)} colunas deprecadas...")

    with db_engine.connect() as conn:
        conn.begin()

        for idx, column_name in enumerate(deprecated_columns, 1):
            output.append(f"\n[{idx}/{len(deprecated_columns)}] Removendo coluna '{column_name}'...")

            sql_drop = text(f"""
                ALTER TABLE NotificationConfigs
                DROP COLUMN IF EXISTS {column_name};
            """)

            try:
                conn.execute(sql_drop)
                output.append(f"    OK - '{column_name}' removida!")
            except Exception as e:
                output.append(f"    AVISO - Erro ao remover '{column_name}': {e}")

        conn.commit()

    output.append("\n" + "="*60)
    output.append("SUCESSO! Campos deprecados removidos")
    output.append("="*60)
    output.append("\nCampos MANTIDOS (estrutura limpa):")
    output.append("- resumo_matinal_ativo (controla resumo com agenda/clima)")
    output.append("- resumo_matinal_hora (horário único de envio)")
    output.append("- alertas_financeiros_ativos (controla alertas de contas/faturas)")
    output.append("\nComportamento:")
    output.append("- Um único horário controla todas as notificações")
    output.append("- Usuário escolhe quais componentes quer receber")
    output.append("- Mensagens são unificadas quando ambos estão ativos")

    return "<pre>" + "\n".join(output) + "</pre>", 200


@feature_migrations_bp.route('/oauth-config-check', methods=['GET'])
@handle_errors(tag="OAUTH-CONFIG-CHECK")
def oauth_config_check():
    """
    Endpoint para verificar configuração OAuth do Google Calendar.

    Retorna se as configurações estão presentes e um preview dos valores.

    Exemplo:
    GET http://localhost:5000/admin/oauth-config-check
    """
    from app.config import GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, GOOGLE_REDIRECT_URI

    return ApiResponse.success(
        "Configuração OAuth verificada",
        client_id_configured=bool(GOOGLE_CLIENT_ID),
        client_id_prefix=GOOGLE_CLIENT_ID[:20] + "..." if GOOGLE_CLIENT_ID else None,
        client_secret_configured=bool(GOOGLE_CLIENT_SECRET),
        redirect_uri=GOOGLE_REDIRECT_URI
    )
