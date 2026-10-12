-- ============================================================================
-- 04 — CONFERÊNCIA (rodar conectado em bot_whatsapp_homol)
-- ============================================================================

-- Tabelas criadas (compare com a produção: devem ser as mesmas)
SELECT table_name FROM information_schema.tables
WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
ORDER BY table_name;

-- Dados de referência copiados; usuarios deve ser 0
SELECT 'grupocategoria' AS tabela, count(*) AS linhas FROM grupocategoria
UNION ALL SELECT 'macrocategoria', count(*) FROM macrocategoria
UNION ALL SELECT 'subcategoria',   count(*) FROM subcategoria
UNION ALL SELECT 'planos',         count(*) FROM planos
UNION ALL SELECT 'usuarios',       count(*) FROM usuarios
UNION ALL SELECT 'transacoes',     count(*) FROM transacoes;

-- Nomes de categoria exigidos pelo código (as 3 linhas devem aparecer)
SELECT nome_sub FROM subcategoria
WHERE usuario_id IS NULL
  AND nome_sub IN ('Investimentos de Curto Prazo', 'Quitação de Empréstimos (Principal)', 'Outros');
