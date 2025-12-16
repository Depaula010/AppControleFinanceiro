-- ============================================================================
-- MIGRAÇÃO: Adicionar Campos Faltantes ao Schema Existente
-- ============================================================================
-- Data: 2025-12-16
-- Objetivo: Adequar banco de dados aos modelos ORM SQLAlchemy criados
--
-- IMPORTANTE: Execute este script em DEV primeiro, depois em PROD
-- ============================================================================

BEGIN;

-- ============================================================================
-- 1. TABELA: Usuarios
-- ============================================================================
-- Adicionar campos de configuração e status do usuário

-- Email (opcional, único)
ALTER TABLE "Usuarios"
ADD COLUMN IF NOT EXISTS email VARCHAR(255) UNIQUE;

COMMENT ON COLUMN "Usuarios".email IS 'Email do usuário (opcional, único)';

-- Conta padrão para transações
ALTER TABLE "Usuarios"
ADD COLUMN IF NOT EXISTS conta_padrao_id INT;

COMMENT ON COLUMN "Usuarios".conta_padrao_id IS 'ID da conta padrão para transações';

-- Fuso horário do usuário
ALTER TABLE "Usuarios"
ADD COLUMN IF NOT EXISTS fuso_horario VARCHAR(50) NOT NULL DEFAULT 'America/Sao_Paulo';

COMMENT ON COLUMN "Usuarios".fuso_horario IS 'Timezone do usuário para agendamentos';

-- Status ativo/inativo
ALTER TABLE "Usuarios"
ADD COLUMN IF NOT EXISTS ativo BOOLEAN NOT NULL DEFAULT TRUE;

COMMENT ON COLUMN "Usuarios".ativo IS 'Se o usuário está ativo no sistema';

-- Último acesso
ALTER TABLE "Usuarios"
ADD COLUMN IF NOT EXISTS ultimo_acesso TIMESTAMP WITH TIME ZONE;

COMMENT ON COLUMN "Usuarios".ultimo_acesso IS 'Data/hora do último acesso do usuário';

-- Updated at (para tracking de alterações)
ALTER TABLE "Usuarios"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "Usuarios".updated_at IS 'Data/hora da última atualização do registro';

-- Criar índices para performance
CREATE INDEX IF NOT EXISTS idx_usuarios_email ON "Usuarios"(email) WHERE email IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_usuarios_ativo ON "Usuarios"(ativo);


-- ============================================================================
-- 2. TABELA: Contas
-- ============================================================================
-- Adicionar campos de configuração de contas

-- Limite de crédito (apenas para cartões)
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS limite_credito NUMERIC(15, 2);

COMMENT ON COLUMN "Contas".limite_credito IS 'Limite do cartão de crédito';

-- Se inclui no saldo total
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS inclui_saldo_total BOOLEAN NOT NULL DEFAULT TRUE;

COMMENT ON COLUMN "Contas".inclui_saldo_total IS 'Se deve incluir no cálculo do saldo total consolidado';

-- Cor para UI
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS cor_hex VARCHAR(7);

COMMENT ON COLUMN "Contas".cor_hex IS 'Cor da conta em hex (ex: #FF5733) para UI';

-- Ícone para UI
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS icone VARCHAR(50);

COMMENT ON COLUMN "Contas".icone IS 'Nome do ícone para UI (ex: credit-card, bank)';

-- Status ativa/inativa
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS ativa BOOLEAN NOT NULL DEFAULT TRUE;

COMMENT ON COLUMN "Contas".ativa IS 'Se a conta está ativa (visível na UI)';

-- Ordem de exibição
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS ordem INT;

COMMENT ON COLUMN "Contas".ordem IS 'Ordem de exibição na lista de contas';

-- Updated at
ALTER TABLE "Contas"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "Contas".updated_at IS 'Data/hora da última atualização do registro';

-- Criar índices
CREATE INDEX IF NOT EXISTS idx_contas_ativa ON "Contas"(ativa);


-- ============================================================================
-- 3. TABELA: Transacoes
-- ============================================================================
-- Adicionar campo de tracking de alterações

ALTER TABLE "Transacoes"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "Transacoes".updated_at IS 'Data/hora da última atualização da transação';


-- ============================================================================
-- 4. TABELA: Faturas
-- ============================================================================
-- Adicionar campos de tracking (created_at e updated_at)

ALTER TABLE "Faturas"
ADD COLUMN IF NOT EXISTS created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "Faturas".created_at IS 'Data/hora de criação da fatura';

ALTER TABLE "Faturas"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "Faturas".updated_at IS 'Data/hora da última atualização da fatura';


-- ============================================================================
-- 5. TABELA: Agendamentos
-- ============================================================================
-- Adicionar campo de tracking de alterações

ALTER TABLE "Agendamentos"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "Agendamentos".updated_at IS 'Data/hora da última atualização do agendamento';


-- ============================================================================
-- 6. TABELA: PotesDeGastos
-- ============================================================================
-- Adicionar campos de tracking

ALTER TABLE "PotesDeGastos"
ADD COLUMN IF NOT EXISTS created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "PotesDeGastos".created_at IS 'Data/hora de criação do pote';

ALTER TABLE "PotesDeGastos"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "PotesDeGastos".updated_at IS 'Data/hora da última atualização do pote';


-- ============================================================================
-- 7. TABELA: GrupoCategoria
-- ============================================================================
-- Adicionar campos de tracking (se necessário)

ALTER TABLE "GrupoCategoria"
ADD COLUMN IF NOT EXISTS created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "GrupoCategoria".created_at IS 'Data/hora de criação do grupo';

ALTER TABLE "GrupoCategoria"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "GrupoCategoria".updated_at IS 'Data/hora da última atualização';


-- ============================================================================
-- 8. TABELA: MacroCategoria
-- ============================================================================
-- Adicionar campos de tracking

ALTER TABLE "MacroCategoria"
ADD COLUMN IF NOT EXISTS created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "MacroCategoria".created_at IS 'Data/hora de criação da macrocategoria';

ALTER TABLE "MacroCategoria"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "MacroCategoria".updated_at IS 'Data/hora da última atualização';


-- ============================================================================
-- 9. TABELA: SubCategoria
-- ============================================================================
-- Adicionar campos de tracking

ALTER TABLE "SubCategoria"
ADD COLUMN IF NOT EXISTS created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "SubCategoria".created_at IS 'Data/hora de criação da subcategoria';

ALTER TABLE "SubCategoria"
ADD COLUMN IF NOT EXISTS updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP;

COMMENT ON COLUMN "SubCategoria".updated_at IS 'Data/hora da última atualização';


-- ============================================================================
-- 10. CRIAR FOREIGN KEY: Usuarios.conta_padrao_id -> Contas.id
-- ============================================================================
-- Adicionar constraint apenas se ainda não existir

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_usuarios_conta_padrao'
    ) THEN
        ALTER TABLE "Usuarios"
        ADD CONSTRAINT fk_usuarios_conta_padrao
        FOREIGN KEY (conta_padrao_id)
        REFERENCES "Contas"(id)
        ON DELETE SET NULL;
    END IF;
END $$;


-- ============================================================================
-- 11. TRIGGERS PARA AUTO-UPDATE DE updated_at
-- ============================================================================
-- Criar função genérica para atualizar updated_at automaticamente

CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Aplicar trigger em todas as tabelas com updated_at

-- Usuarios
DROP TRIGGER IF EXISTS trigger_update_usuarios_updated_at ON "Usuarios";
CREATE TRIGGER trigger_update_usuarios_updated_at
    BEFORE UPDATE ON "Usuarios"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Contas
DROP TRIGGER IF EXISTS trigger_update_contas_updated_at ON "Contas";
CREATE TRIGGER trigger_update_contas_updated_at
    BEFORE UPDATE ON "Contas"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Transacoes
DROP TRIGGER IF EXISTS trigger_update_transacoes_updated_at ON "Transacoes";
CREATE TRIGGER trigger_update_transacoes_updated_at
    BEFORE UPDATE ON "Transacoes"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Faturas
DROP TRIGGER IF EXISTS trigger_update_faturas_updated_at ON "Faturas";
CREATE TRIGGER trigger_update_faturas_updated_at
    BEFORE UPDATE ON "Faturas"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- Agendamentos
DROP TRIGGER IF EXISTS trigger_update_agendamentos_updated_at ON "Agendamentos";
CREATE TRIGGER trigger_update_agendamentos_updated_at
    BEFORE UPDATE ON "Agendamentos"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- PotesDeGastos
DROP TRIGGER IF EXISTS trigger_update_potes_updated_at ON "PotesDeGastos";
CREATE TRIGGER trigger_update_potes_updated_at
    BEFORE UPDATE ON "PotesDeGastos"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- GrupoCategoria
DROP TRIGGER IF EXISTS trigger_update_grupo_categoria_updated_at ON "GrupoCategoria";
CREATE TRIGGER trigger_update_grupo_categoria_updated_at
    BEFORE UPDATE ON "GrupoCategoria"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- MacroCategoria
DROP TRIGGER IF EXISTS trigger_update_macro_categoria_updated_at ON "MacroCategoria";
CREATE TRIGGER trigger_update_macro_categoria_updated_at
    BEFORE UPDATE ON "MacroCategoria"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();

-- SubCategoria
DROP TRIGGER IF EXISTS trigger_update_sub_categoria_updated_at ON "SubCategoria";
CREATE TRIGGER trigger_update_sub_categoria_updated_at
    BEFORE UPDATE ON "SubCategoria"
    FOR EACH ROW
    EXECUTE FUNCTION update_updated_at_column();


-- ============================================================================
-- 12. VALIDAÇÕES E VERIFICAÇÕES
-- ============================================================================

-- Verificar se todos os campos foram criados
DO $$
DECLARE
    v_count INT;
BEGIN
    -- Verificar Usuarios
    SELECT COUNT(*) INTO v_count
    FROM information_schema.columns
    WHERE table_name = 'Usuarios'
    AND column_name IN ('email', 'conta_padrao_id', 'fuso_horario', 'ativo', 'ultimo_acesso', 'updated_at');

    IF v_count < 6 THEN
        RAISE EXCEPTION 'ERRO: Campos faltando na tabela Usuarios. Esperado: 6, Encontrado: %', v_count;
    END IF;

    -- Verificar Contas
    SELECT COUNT(*) INTO v_count
    FROM information_schema.columns
    WHERE table_name = 'Contas'
    AND column_name IN ('limite_credito', 'inclui_saldo_total', 'cor_hex', 'icone', 'ativa', 'ordem', 'updated_at');

    IF v_count < 7 THEN
        RAISE EXCEPTION 'ERRO: Campos faltando na tabela Contas. Esperado: 7, Encontrado: %', v_count;
    END IF;

    RAISE NOTICE '✅ VALIDAÇÃO CONCLUÍDA: Todos os campos foram adicionados com sucesso!';
END $$;

COMMIT;

-- ============================================================================
-- FIM DA MIGRAÇÃO
-- ============================================================================

-- Para reverter esta migração (CUIDADO - perda de dados!)
-- Descomente e execute o script abaixo:

/*
BEGIN;

-- Remover triggers
DROP TRIGGER IF EXISTS trigger_update_usuarios_updated_at ON "Usuarios";
DROP TRIGGER IF EXISTS trigger_update_contas_updated_at ON "Contas";
DROP TRIGGER IF EXISTS trigger_update_transacoes_updated_at ON "Transacoes";
DROP TRIGGER IF EXISTS trigger_update_faturas_updated_at ON "Faturas";
DROP TRIGGER IF EXISTS trigger_update_agendamentos_updated_at ON "Agendamentos";
DROP TRIGGER IF EXISTS trigger_update_potes_updated_at ON "PotesDeGastos";
DROP TRIGGER IF EXISTS trigger_update_grupo_categoria_updated_at ON "GrupoCategoria";
DROP TRIGGER IF EXISTS trigger_update_macro_categoria_updated_at ON "MacroCategoria";
DROP TRIGGER IF EXISTS trigger_update_sub_categoria_updated_at ON "SubCategoria";

-- Remover função
DROP FUNCTION IF EXISTS update_updated_at_column();

-- Remover constraint
ALTER TABLE "Usuarios" DROP CONSTRAINT IF EXISTS fk_usuarios_conta_padrao;

-- Remover campos de Usuarios
ALTER TABLE "Usuarios" DROP COLUMN IF EXISTS email;
ALTER TABLE "Usuarios" DROP COLUMN IF EXISTS conta_padrao_id;
ALTER TABLE "Usuarios" DROP COLUMN IF EXISTS fuso_horario;
ALTER TABLE "Usuarios" DROP COLUMN IF EXISTS ativo;
ALTER TABLE "Usuarios" DROP COLUMN IF EXISTS ultimo_acesso;
ALTER TABLE "Usuarios" DROP COLUMN IF EXISTS updated_at;

-- Remover campos de Contas
ALTER TABLE "Contas" DROP COLUMN IF EXISTS limite_credito;
ALTER TABLE "Contas" DROP COLUMN IF EXISTS inclui_saldo_total;
ALTER TABLE "Contas" DROP COLUMN IF EXISTS cor_hex;
ALTER TABLE "Contas" DROP COLUMN IF EXISTS icone;
ALTER TABLE "Contas" DROP COLUMN IF EXISTS ativa;
ALTER TABLE "Contas" DROP COLUMN IF EXISTS ordem;
ALTER TABLE "Contas" DROP COLUMN IF EXISTS updated_at;

-- Remover campos de outras tabelas
ALTER TABLE "Transacoes" DROP COLUMN IF EXISTS updated_at;
ALTER TABLE "Faturas" DROP COLUMN IF EXISTS created_at;
ALTER TABLE "Faturas" DROP COLUMN IF EXISTS updated_at;
ALTER TABLE "Agendamentos" DROP COLUMN IF EXISTS updated_at;
ALTER TABLE "PotesDeGastos" DROP COLUMN IF EXISTS created_at;
ALTER TABLE "PotesDeGastos" DROP COLUMN IF EXISTS updated_at;
ALTER TABLE "GrupoCategoria" DROP COLUMN IF EXISTS created_at;
ALTER TABLE "GrupoCategoria" DROP COLUMN IF EXISTS updated_at;
ALTER TABLE "MacroCategoria" DROP COLUMN IF EXISTS created_at;
ALTER TABLE "MacroCategoria" DROP COLUMN IF EXISTS updated_at;
ALTER TABLE "SubCategoria" DROP COLUMN IF EXISTS created_at;
ALTER TABLE "SubCategoria" DROP COLUMN IF EXISTS updated_at;

COMMIT;
*/
