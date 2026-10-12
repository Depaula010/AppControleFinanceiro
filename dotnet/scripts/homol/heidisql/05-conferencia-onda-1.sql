-- ============================================================================
-- 05 — CONFERÊNCIA DA ONDA 1 (somente leitura)
-- Rodar conectado em bot_whatsapp_prod. Só SELECT. No HeidiSQL: Shift+Ctrl+F9
-- (cada SELECT gera uma aba de resultado). Respondem perguntas das specs 002 e 003
-- (ver dotnet/specs/foundation/decisoes-onda-1.md §5).
-- ============================================================================

-- 1. Collation do banco (ordem dos nomes em GET /api/accounts e /api/categories)
SELECT datname, datcollate, datctype
FROM pg_database
WHERE datname = current_database();

-- 2. Seed de categorias: esperado 5 grupos, 18 macros globais, 61 subs globais, 0 de usuário
SELECT (SELECT count(*) FROM grupocategoria) AS grupos,
       (SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL) AS macros_globais,
       (SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL) AS subs_globais,
       (SELECT count(*) FROM macrocategoria WHERE usuario_id IS NOT NULL) AS macros_de_usuario,
       (SELECT count(*) FROM subcategoria WHERE usuario_id IS NOT NULL) AS subs_de_usuario;

-- 3. Subcategorias globais duplicadas (esperado: Outros em macro 17, 2 linhas)
SELECT macro_id, nome_sub, count(*) AS qtd
FROM subcategoria
WHERE usuario_id IS NULL
GROUP BY macro_id, nome_sub
HAVING count(*) > 1;

-- 4. Macros globais (esperado: ids 1 a 18; macro 17 = Despesas Gerais)
SELECT m.id, m.nome_macro, m.ordem_macro, g.nome_grupo
FROM macrocategoria m
JOIN grupocategoria g ON g.id = m.grupo_id
WHERE m.usuario_id IS NULL
ORDER BY m.id;

-- 5. Nome real da UNIQUE de contas (a spec 002 supõe contas_usuario_id_nome_conta_key)
SELECT conname, pg_get_constraintdef(oid) AS definicao
FROM pg_constraint
WHERE conrelid = 'contas'::regclass AND contype = 'u';

-- 6. Contas: totais, inativas e não-cartão com dias (esperado: 0 não-cartão com dias, por causa do CHECK)
SELECT count(*) AS total,
       count(*) FILTER (WHERE ativa) AS ativas,
       count(*) FILTER (WHERE NOT ativa) AS inativas,
       count(*) FILTER (WHERE tipo_conta <> 'Cartão de Crédito' AND (dia_vencimento IS NOT NULL OR dia_fechamento IS NOT NULL)) AS nao_cartao_com_dias,
       count(*) FILTER (WHERE tipo_conta = 'Cartão de Crédito' AND (dia_vencimento IS NULL OR dia_fechamento IS NULL)) AS cartao_sem_dias
FROM contas;

-- 7. Nomes e tipos das contas existentes (sem saldo nem dados pessoais)
SELECT id, nome_conta, tipo_conta, ativa
FROM contas
ORDER BY id;

-- 8. Agendamentos ativos por conta (P-002-6: bloquear exclusão de conta com agendamentos ativos)
SELECT conta_id, count(*) AS agendamentos_ativos
FROM agendamentos
WHERE ativo
GROUP BY conta_id
ORDER BY conta_id;
