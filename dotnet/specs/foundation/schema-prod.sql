-- ESTRUTURA do banco de produção (bot_whatsapp_prod), exportada pelo HeidiSQL em 2026-10-02.
-- Gerado a partir de migrations/schema.sql (que contém dados e NÃO é versionado) removendo todos os INSERT.
-- O HeidiSQL não exporta triggers: os 9 triggers e a função estão no fim deste arquivo (consulta 6/7 da inspeção).
-- Somente referência para o modelo EF/Dapper; não executar em produção.

-- --------------------------------------------------------
-- Servidor:                     127.0.0.1
-- Versão do servidor:           PostgreSQL 15.15 on x86_64-pc-linux-musl, compiled by gcc (Alpine 14.2.0) 14.2.0, 64-bit
-- OS do Servidor:               
-- HeidiSQL Versão:              12.10.0.7000
-- --------------------------------------------------------

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET NAMES  */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;

-- Copiando estrutura para função public.update_updated_at_column
DELIMITER //
CREATE FUNCTION "update_updated_at_column"() RETURNS UNKNOWN AS $$ 
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
 $$//
DELIMITER ;

-- Copiando estrutura para tabela public.agendamentos
CREATE TABLE IF NOT EXISTS "agendamentos" (
	"id" INTEGER NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"conta_id" INTEGER NOT NULL,
	"subcategoria_id" INTEGER NOT NULL,
	"descricao" VARCHAR(255) NOT NULL,
	"valor_previsto" NUMERIC(15,2) NULL DEFAULT NULL,
	"tipo_agendamento" VARCHAR(20) NOT NULL,
	"periodicidade" VARCHAR(20) NOT NULL,
	"data_inicio" DATE NOT NULL,
	"dia_execucao" INTEGER NOT NULL,
	"total_parcelas" INTEGER NULL DEFAULT NULL,
	"parcelas_executadas" INTEGER NULL DEFAULT 0,
	"notificar_antes_dias" INTEGER NULL DEFAULT 3,
	"ativo" BOOLEAN NOT NULL DEFAULT true,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"mes_execucao" INTEGER NULL DEFAULT NULL,
	"incluir_na_reserva" BOOLEAN NULL DEFAULT true,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	KEY ("usuario_id"),
	KEY ("usuario_id", "incluir_na_reserva", "periodicidade"),
	CONSTRAINT "agendamentos_conta_id_fkey" FOREIGN KEY ("conta_id") REFERENCES "contas" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "agendamentos_subcategoria_id_fkey" FOREIGN KEY ("subcategoria_id") REFERENCES "subcategoria" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT,
	CONSTRAINT "agendamentos_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "agendamentos_tipo_agendamento_check" CHECK ((((tipo_agendamento)::text = ANY ((ARRAY['FIXO'::character varying, 'PARCELADO'::character varying, 'LEMBRETE_VARIAVEL'::character varying])::text[])))),
	CONSTRAINT "agendamentos_periodicidade_check" CHECK ((((periodicidade)::text = ANY ((ARRAY['DIARIA'::character varying, 'SEMANAL'::character varying, 'QUINZENAL'::character varying, 'MENSAL'::character varying, 'ANUAL'::character varying])::text[])))),
	CONSTRAINT "agendamentos_dia_execucao_check" CHECK ((((dia_execucao >= 1) AND (dia_execucao <= 31)))),
	CONSTRAINT "chk_parcelado" CHECK (((((tipo_agendamento)::text = 'PARCELADO'::text) OR ((total_parcelas IS NULL) AND (parcelas_executadas = 0))))),
	CONSTRAINT "agendamentos_mes_execucao_check" CHECK ((((mes_execucao IS NULL) OR ((mes_execucao >= 1) AND (mes_execucao <= 12))))),
	CONSTRAINT "chk_anual_mes" CHECK ((((((periodicidade)::text = 'ANUAL'::text) AND (mes_execucao IS NOT NULL)) OR (((periodicidade)::text <> 'ANUAL'::text) AND (mes_execucao IS NULL)))))
);

-- Copiando dados para a tabela public.agendamentos: 38 rows

-- Copiando estrutura para tabela public.alembic_version
CREATE TABLE IF NOT EXISTS "alembic_version" (
	"version_num" VARCHAR(32) NOT NULL,
	PRIMARY KEY ("version_num")
);

-- Copiando dados para a tabela public.alembic_version: -1 rows

-- Copiando estrutura para tabela public.assinaturasusuario
CREATE TABLE IF NOT EXISTS "assinaturasusuario" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"plano_id" INTEGER NOT NULL,
	"data_inicio" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"data_fim" TIMESTAMPTZ NULL DEFAULT NULL,
	"status" VARCHAR(20) NOT NULL DEFAULT 'ativo',
	"criado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"atualizado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	KEY ("usuario_id", "status"),
	CONSTRAINT "assinaturasusuario_plano_id_fkey" FOREIGN KEY ("plano_id") REFERENCES "planos" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT,
	CONSTRAINT "assinaturasusuario_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.assinaturasusuario: -1 rows

-- Copiando estrutura para tabela public.baileys_auth
CREATE TABLE IF NOT EXISTS "baileys_auth" (
	"session_id" VARCHAR(100) NOT NULL,
	"data_key" VARCHAR(100) NOT NULL,
	"data_value" TEXT NULL DEFAULT NULL,
	PRIMARY KEY ("session_id", "data_key")
);

-- Copiando dados para a tabela public.baileys_auth: 3.228 rows

-- Copiando estrutura para tabela public.calendaralertconfigs
CREATE TABLE IF NOT EXISTS "calendaralertconfigs" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"alertas_tarefas_ativo" BOOLEAN NOT NULL DEFAULT false,
	"minutos_antes" INTEGER NOT NULL DEFAULT 1,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id"),
	KEY ("usuario_id"),
	CONSTRAINT "calendaralertconfigs_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "chk_minutos_antes" CHECK ((((minutos_antes >= 1) AND (minutos_antes <= 60))))
);

-- Copiando dados para a tabela public.calendaralertconfigs: -1 rows

-- Copiando estrutura para tabela public.chavesapiusuario
CREATE TABLE IF NOT EXISTS "chavesapiusuario" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"provedor" VARCHAR(50) NOT NULL,
	"chave_api_criptografada" TEXT NOT NULL,
	"ativo" BOOLEAN NULL DEFAULT true,
	"criado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"atualizado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"ultimo_uso_em" TIMESTAMPTZ NULL DEFAULT NULL,
	"consentimento_dado" BOOLEAN NULL DEFAULT true,
	"consentimento_data" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "provedor"),
	KEY ("usuario_id"),
	KEY ("usuario_id", "provedor"),
	CONSTRAINT "chavesapiusuario_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.chavesapiusuario: 3 rows

-- Copiando estrutura para tabela public.consentimentousuario
CREATE TABLE IF NOT EXISTS "consentimentousuario" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"tipo_consentimento" VARCHAR(50) NOT NULL,
	"versao_consentimento" VARCHAR(20) NOT NULL,
	"consentimento_dado" BOOLEAN NOT NULL,
	"data_consentimento" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"ip_consentimento" VARCHAR(45) NULL DEFAULT NULL,
	"texto_consentimento" TEXT NULL DEFAULT NULL,
	"revogado_em" TIMESTAMPTZ NULL DEFAULT NULL,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "tipo_consentimento", "versao_consentimento"),
	KEY ("usuario_id", "tipo_consentimento"),
	CONSTRAINT "consentimentousuario_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.consentimentousuario: -1 rows

-- Copiando estrutura para tabela public.contas
CREATE TABLE IF NOT EXISTS "contas" (
	"id" INTEGER NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"nome_conta" VARCHAR(100) NOT NULL,
	"tipo_conta" VARCHAR(50) NOT NULL,
	"saldo_inicial" NUMERIC(15,2) NOT NULL DEFAULT 0.00,
	"dia_vencimento" INTEGER NULL DEFAULT NULL,
	"dia_fechamento" INTEGER NULL DEFAULT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"limite_credito" NUMERIC(15,2) NULL DEFAULT NULL,
	"inclui_saldo_total" BOOLEAN NOT NULL DEFAULT true,
	"cor_hex" VARCHAR(7) NULL DEFAULT NULL,
	"icone" VARCHAR(50) NULL DEFAULT NULL,
	"ativa" BOOLEAN NOT NULL DEFAULT true,
	"ordem" INTEGER NULL DEFAULT NULL,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "nome_conta"),
	KEY ("usuario_id"),
	KEY ("ativa"),
	CONSTRAINT "contas_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "contas_tipo_conta_check" CHECK ((((tipo_conta)::text = ANY ((ARRAY['Conta Corrente'::character varying, 'Conta Poupança'::character varying, 'Investimento'::character varying, 'Cartão de Crédito'::character varying, 'Dinheiro'::character varying, 'Outro'::character varying])::text[])))),
	CONSTRAINT "contas_dia_vencimento_check" CHECK ((((dia_vencimento >= 1) AND (dia_vencimento <= 31)))),
	CONSTRAINT "contas_dia_fechamento_check" CHECK ((((dia_fechamento >= 1) AND (dia_fechamento <= 31)))),
	CONSTRAINT "chk_cartao_credito" CHECK (((((tipo_conta)::text = 'Cartão de Crédito'::text) OR ((dia_vencimento IS NULL) AND (dia_fechamento IS NULL)))))
);

-- Copiando dados para a tabela public.contas: 4 rows

-- Copiando estrutura para tabela public.enderecosfavoritos
CREATE TABLE IF NOT EXISTS "enderecosfavoritos" (
	"id" INTEGER NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"label" VARCHAR(50) NOT NULL,
	"endereco_completo" TEXT NOT NULL,
	"latitude" NUMERIC(10,8) NULL DEFAULT NULL,
	"longitude" NUMERIC(11,8) NULL DEFAULT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "label"),
	KEY ("usuario_id"),
	CONSTRAINT "enderecosfavoritos_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "enderecosfavoritos_label_check" CHECK ((((label)::text = ANY ((ARRAY['casa'::character varying, 'trabalho'::character varying, 'outro'::character varying])::text[]))))
);

-- Copiando dados para a tabela public.enderecosfavoritos: -1 rows

-- Copiando estrutura para tabela public.faturas
CREATE TABLE IF NOT EXISTS "faturas" (
	"id" INTEGER NOT NULL,
	"conta_id" INTEGER NOT NULL,
	"data_vencimento" DATE NOT NULL,
	"data_fechamento" DATE NOT NULL,
	"status" VARCHAR(20) NOT NULL DEFAULT 'Aberta',
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("conta_id", "data_vencimento"),
	KEY ("conta_id"),
	CONSTRAINT "faturas_conta_id_fkey" FOREIGN KEY ("conta_id") REFERENCES "contas" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "faturas_status_check" CHECK ((((status)::text = ANY ((ARRAY['Aberta'::character varying, 'Fechada'::character varying, 'Paga'::character varying])::text[]))))
);

-- Copiando dados para a tabela public.faturas: -1 rows

-- Copiando estrutura para tabela public.googlecalendartokens
CREATE TABLE IF NOT EXISTS "googlecalendartokens" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"access_token" TEXT NOT NULL,
	"refresh_token" TEXT NULL DEFAULT NULL,
	"token_expiry" TIMESTAMPTZ NULL DEFAULT NULL,
	"scopes" TEXT NULL DEFAULT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"needs_reconnect" BOOLEAN NOT NULL DEFAULT false,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id"),
	KEY ("usuario_id"),
	CONSTRAINT "googlecalendartokens_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.googlecalendartokens: 1 rows

-- Copiando estrutura para tabela public.grupocategoria
CREATE TABLE IF NOT EXISTS "grupocategoria" (
	"id" SERIAL NOT NULL,
	"nome_grupo" VARCHAR(100) NOT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("nome_grupo")
);

-- Copiando dados para a tabela public.grupocategoria: -1 rows

-- Copiando estrutura para tabela public.logacessochaveapi
CREATE TABLE IF NOT EXISTS "logacessochaveapi" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"provedor" VARCHAR(50) NOT NULL,
	"tipo_chave" VARCHAR(20) NOT NULL,
	"operacao" VARCHAR(50) NOT NULL,
	"sucesso" BOOLEAN NOT NULL,
	"mensagem_erro" TEXT NULL DEFAULT NULL,
	"timestamp_acesso" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"endereco_ip" VARCHAR(45) NULL DEFAULT NULL,
	"criado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	KEY ("usuario_id", "timestamp_acesso"),
	KEY ("provedor", "timestamp_acesso"),
	CONSTRAINT "logacessochaveapi_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.logacessochaveapi: -1 rows

-- Copiando estrutura para tabela public.macrocategoria
CREATE TABLE IF NOT EXISTS "macrocategoria" (
	"id" SERIAL NOT NULL,
	"grupo_id" INTEGER NOT NULL,
	"usuario_id" INTEGER NULL DEFAULT NULL,
	"nome_macro" VARCHAR(255) NOT NULL,
	"ordem_macro" INTEGER NULL DEFAULT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	KEY ("usuario_id"),
	CONSTRAINT "macrocategoria_grupo_id_fkey" FOREIGN KEY ("grupo_id") REFERENCES "grupocategoria" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT,
	CONSTRAINT "macrocategoria_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.macrocategoria: -1 rows

-- Copiando estrutura para tabela public.monthlyreportconfigs
CREATE TABLE IF NOT EXISTS "monthlyreportconfigs" (
	"usuario_id" INTEGER NOT NULL,
	"ativo" BOOLEAN NULL DEFAULT true,
	"momento_envio" VARCHAR(20) NULL DEFAULT 'INICIO_MES',
	"hora_envio" TIME NULL DEFAULT '08:00:00',
	"created_at" TIMESTAMP NULL DEFAULT now(),
	"updated_at" TIMESTAMP NULL DEFAULT now(),
	PRIMARY KEY ("usuario_id"),
	CONSTRAINT "monthlyreportconfigs_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "monthlyreportconfigs_momento_envio_check" CHECK ((((momento_envio)::text = ANY ((ARRAY['INICIO_MES'::character varying, 'FIM_MES'::character varying])::text[]))))
);

-- Copiando dados para a tabela public.monthlyreportconfigs: -1 rows

-- Copiando estrutura para tabela public.notificationconfigs
CREATE TABLE IF NOT EXISTS "notificationconfigs" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"resumo_matinal_ativo" BOOLEAN NOT NULL DEFAULT true,
	"resumo_matinal_hora" TIME NOT NULL DEFAULT '07:00:00',
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"alerta_potes_ativo" BOOLEAN NOT NULL DEFAULT true,
	"alerta_potes_threshold" INTEGER NOT NULL DEFAULT 0,
	"alertas_financeiros_ativos" BOOLEAN NULL DEFAULT true,
	"checkin_noturno_ativo" BOOLEAN NOT NULL DEFAULT true,
	"checkin_noturno_hora" TIME NOT NULL DEFAULT '19:00:00',
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id"),
	KEY ("usuario_id"),
	CONSTRAINT "notificationconfigs_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.notificationconfigs: 1 rows

-- Copiando estrutura para tabela public.planos
CREATE TABLE IF NOT EXISTS "planos" (
	"id" SERIAL NOT NULL,
	"nome_plano" VARCHAR(50) NOT NULL,
	"descricao" TEXT NULL DEFAULT NULL,
	"preco_mensal" NUMERIC(10,2) NOT NULL,
	"limite_gemini" INTEGER NULL DEFAULT NULL,
	"limite_weather" INTEGER NULL DEFAULT NULL,
	"limite_openroute" INTEGER NULL DEFAULT NULL,
	"custo_por_chamada_gemini" NUMERIC(10,4) NULL DEFAULT 0.01,
	"custo_por_chamada_weather" NUMERIC(10,4) NULL DEFAULT 0.005,
	"custo_por_chamada_openroute" NUMERIC(10,4) NULL DEFAULT 0.02,
	"permite_chaves_proprias" BOOLEAN NULL DEFAULT true,
	"ativo" BOOLEAN NULL DEFAULT true,
	"criado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"atualizado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("nome_plano")
);

-- Copiando dados para a tabela public.planos: -1 rows

-- Copiando estrutura para tabela public.potesdegastos
CREATE TABLE IF NOT EXISTS "potesdegastos" (
	"id" INTEGER NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"nome_pote" VARCHAR(100) NOT NULL,
	"valor_limite" NUMERIC(15,2) NOT NULL,
	"periodicidade" VARCHAR(20) NOT NULL DEFAULT 'MENSAL',
	"data_inicio" DATE NOT NULL DEFAULT CURRENT_DATE,
	"ativo" BOOLEAN NOT NULL DEFAULT true,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "nome_pote"),
	KEY ("usuario_id"),
	CONSTRAINT "potesdegastos_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "potesdegastos_periodicidade_check" CHECK ((((periodicidade)::text = ANY ((ARRAY['SEMANAL'::character varying, 'QUINZENAL'::character varying, 'MENSAL'::character varying, 'ANUAL'::character varying])::text[]))))
);

-- Copiando dados para a tabela public.potesdegastos: -1 rows

-- Copiando estrutura para tabela public.potesubcategorias
CREATE TABLE IF NOT EXISTS "potesubcategorias" (
	"id" INTEGER NOT NULL,
	"pote_id" INTEGER NOT NULL,
	"subcategoria_id" INTEGER NOT NULL,
	PRIMARY KEY ("id"),
	UNIQUE ("pote_id", "subcategoria_id"),
	KEY ("pote_id"),
	KEY ("subcategoria_id"),
	CONSTRAINT "potesubcategorias_pote_id_fkey" FOREIGN KEY ("pote_id") REFERENCES "potesdegastos" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "potesubcategorias_subcategoria_id_fkey" FOREIGN KEY ("subcategoria_id") REFERENCES "subcategoria" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.potesubcategorias: -1 rows

-- Copiando estrutura para tabela public.preferenciaschaveapi
CREATE TABLE IF NOT EXISTS "preferenciaschaveapi" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"provedor" VARCHAR(50) NOT NULL,
	"usar_chave_propria" BOOLEAN NOT NULL DEFAULT false,
	"criado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"atualizado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "provedor"),
	KEY ("usuario_id"),
	CONSTRAINT "preferenciaschaveapi_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.preferenciaschaveapi: -1 rows

-- Copiando estrutura para tabela public.rastreamentousoapi
CREATE TABLE IF NOT EXISTS "rastreamentousoapi" (
	"id" SERIAL NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"provedor" VARCHAR(50) NOT NULL,
	"tipo_chave" VARCHAR(20) NOT NULL,
	"quantidade_chamadas" INTEGER NULL DEFAULT 0,
	"mes_ano" VARCHAR(7) NOT NULL,
	"valor_cobrado" NUMERIC(10,2) NULL DEFAULT 0.00,
	"criado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"atualizado_em" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("usuario_id", "provedor", "tipo_chave", "mes_ano"),
	KEY ("mes_ano", "provedor"),
	KEY ("usuario_id", "mes_ano"),
	CONSTRAINT "rastreamentousoapi_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.rastreamentousoapi: 10 rows

-- Copiando estrutura para tabela public.subcategoria
CREATE TABLE IF NOT EXISTS "subcategoria" (
	"id" SERIAL NOT NULL,
	"macro_id" INTEGER NOT NULL,
	"usuario_id" INTEGER NULL DEFAULT NULL,
	"nome_sub" VARCHAR(255) NOT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("macro_id", "nome_sub", "usuario_id"),
	KEY ("usuario_id"),
	CONSTRAINT "subcategoria_macro_id_fkey" FOREIGN KEY ("macro_id") REFERENCES "macrocategoria" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "subcategoria_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE
);

-- Copiando dados para a tabela public.subcategoria: 60 rows

-- Copiando estrutura para tabela public.transacoes
CREATE TABLE IF NOT EXISTS "transacoes" (
	"id" INTEGER NOT NULL,
	"usuario_id" INTEGER NOT NULL,
	"conta_id" INTEGER NOT NULL,
	"subcategoria_id" INTEGER NOT NULL,
	"fatura_id" INTEGER NULL DEFAULT NULL,
	"transferencia_par_id" INTEGER NULL DEFAULT NULL,
	"descricao" VARCHAR(255) NOT NULL,
	"valor" NUMERIC(15,2) NOT NULL,
	"data_transacao" DATE NOT NULL DEFAULT CURRENT_DATE,
	"tipo_transacao" VARCHAR(20) NOT NULL,
	"consolidada" BOOLEAN NOT NULL DEFAULT true,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"agendamento_id" INTEGER NULL DEFAULT NULL,
	PRIMARY KEY ("id"),
	KEY ("usuario_id"),
	KEY ("fatura_id"),
	KEY ("transferencia_par_id"),
	KEY ("agendamento_id"),
	CONSTRAINT "fk_transacoes_agendamento" FOREIGN KEY ("agendamento_id") REFERENCES "agendamentos" ("id") ON UPDATE NO ACTION ON DELETE SET NULL,
	CONSTRAINT "transacoes_conta_id_fkey" FOREIGN KEY ("conta_id") REFERENCES "contas" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT,
	CONSTRAINT "transacoes_fatura_id_fkey" FOREIGN KEY ("fatura_id") REFERENCES "faturas" ("id") ON UPDATE NO ACTION ON DELETE SET NULL,
	CONSTRAINT "transacoes_subcategoria_id_fkey" FOREIGN KEY ("subcategoria_id") REFERENCES "subcategoria" ("id") ON UPDATE NO ACTION ON DELETE RESTRICT,
	CONSTRAINT "transacoes_transferencia_par_id_fkey" FOREIGN KEY ("transferencia_par_id") REFERENCES "transacoes" ("id") ON UPDATE NO ACTION ON DELETE SET NULL,
	CONSTRAINT "transacoes_usuario_id_fkey" FOREIGN KEY ("usuario_id") REFERENCES "usuarios" ("id") ON UPDATE NO ACTION ON DELETE CASCADE,
	CONSTRAINT "transacoes_tipo_transacao_check" CHECK ((((tipo_transacao)::text = ANY ((ARRAY['Renda'::character varying, 'Despesa'::character varying, 'Transferência'::character varying, 'Pagamento Fatura'::character varying])::text[]))))
);

-- Copiando dados para a tabela public.transacoes: 321 rows

-- Copiando estrutura para tabela public.usuarios
CREATE TABLE IF NOT EXISTS "usuarios" (
	"id" INTEGER NOT NULL,
	"nome" VARCHAR(255) NOT NULL,
	"numero_whatsapp" VARCHAR(50) NOT NULL,
	"api_key_automate" VARCHAR(100) NULL DEFAULT NULL,
	"created_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	"cidade" VARCHAR(100) NULL DEFAULT 'Sao Paulo',
	"estado" VARCHAR(2) NULL DEFAULT 'SP',
	"conta_padrao_renda_id" INTEGER NULL DEFAULT NULL,
	"conta_padrao_despesa_id" INTEGER NULL DEFAULT NULL,
	"meses_reserva_emergencia" INTEGER NULL DEFAULT 6,
	"senha_hash" VARCHAR(255) NULL DEFAULT NULL,
	"email" VARCHAR(255) NULL DEFAULT NULL,
	"conta_padrao_id" INTEGER NULL DEFAULT NULL,
	"fuso_horario" VARCHAR(50) NOT NULL DEFAULT 'America/Sao_Paulo',
	"ativo" BOOLEAN NOT NULL DEFAULT true,
	"ultimo_acesso" TIMESTAMPTZ NULL DEFAULT NULL,
	"updated_at" TIMESTAMPTZ NULL DEFAULT CURRENT_TIMESTAMP,
	PRIMARY KEY ("id"),
	UNIQUE ("numero_whatsapp"),
	UNIQUE ("api_key_automate"),
	KEY ("api_key_automate"),
	KEY ("cidade", "estado"),
	KEY ("conta_padrao_renda_id"),
	KEY ("conta_padrao_despesa_id"),
	KEY ("id", "meses_reserva_emergencia"),
	KEY ("numero_whatsapp"),
	UNIQUE ("email"),
	CONSTRAINT "fk_usuarios_conta_padrao" FOREIGN KEY ("conta_padrao_id") REFERENCES "contas" ("id") ON UPDATE NO ACTION ON DELETE SET NULL,
	CONSTRAINT "usuarios_conta_padrao_despesa_id_fkey" FOREIGN KEY ("conta_padrao_despesa_id") REFERENCES "contas" ("id") ON UPDATE NO ACTION ON DELETE SET NULL,
	CONSTRAINT "usuarios_conta_padrao_renda_id_fkey" FOREIGN KEY ("conta_padrao_renda_id") REFERENCES "contas" ("id") ON UPDATE NO ACTION ON DELETE SET NULL
);

-- Copiando dados para a tabela public.usuarios: 1 rows

/*!40103 SET TIME_ZONE=IFNULL(@OLD_TIME_ZONE, 'system') */;
/*!40101 SET SQL_MODE=IFNULL(@OLD_SQL_MODE, '') */;
/*!40014 SET FOREIGN_KEY_CHECKS=IFNULL(@OLD_FOREIGN_KEY_CHECKS, 1) */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40111 SET SQL_NOTES=IFNULL(@OLD_SQL_NOTES, 1) */;


-- ============================================================================
-- Triggers e função (obtidos por consulta ao catálogo; ausentes no export do HeidiSQL)
-- ============================================================================
CREATE OR REPLACE FUNCTION public.update_updated_at_column()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$function$;

CREATE TRIGGER trigger_update_agendamentos_updated_at BEFORE UPDATE ON public.agendamentos FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_contas_updated_at BEFORE UPDATE ON public.contas FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_faturas_updated_at BEFORE UPDATE ON public.faturas FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_grupo_categoria_updated_at BEFORE UPDATE ON public.grupocategoria FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_macro_categoria_updated_at BEFORE UPDATE ON public.macrocategoria FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_potes_updated_at BEFORE UPDATE ON public.potesdegastos FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_sub_categoria_updated_at BEFORE UPDATE ON public.subcategoria FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_transacoes_updated_at BEFORE UPDATE ON public.transacoes FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
CREATE TRIGGER trigger_update_usuarios_updated_at BEFORE UPDATE ON public.usuarios FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();
