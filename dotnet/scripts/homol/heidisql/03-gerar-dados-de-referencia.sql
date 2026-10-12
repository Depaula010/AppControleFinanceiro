-- ============================================================================
-- 03 — GERAR OS DADOS DE REFERÊNCIA (categorias globais e planos)
-- Rodar conectado em bot_whatsapp_prod (somente SELECT).
-- Cada SELECT devolve UMA célula com um comando INSERT pronto.
-- Copie o conteúdo da célula (clique na célula > Ctrl+C; se aparecer cortado,
-- clique com o botão direito > "Copiar valor" ou abra no editor de texto da célula)
-- e execute NA ORDEM abaixo, conectado em bot_whatsapp_homol.
-- Só cópias de categorias globais (usuario_id IS NULL) e planos; nenhum dado de usuário.
-- ============================================================================

-- 1º
SELECT format('INSERT INTO grupocategoria SELECT * FROM json_populate_recordset(null::grupocategoria, %L);',
              json_agg(t)) AS comando
FROM grupocategoria t;

-- 2º
SELECT format('INSERT INTO macrocategoria SELECT * FROM json_populate_recordset(null::macrocategoria, %L);',
              json_agg(t)) AS comando
FROM macrocategoria t WHERE t.usuario_id IS NULL;

-- 3º
SELECT format('INSERT INTO subcategoria SELECT * FROM json_populate_recordset(null::subcategoria, %L);',
              json_agg(t)) AS comando
FROM subcategoria t WHERE t.usuario_id IS NULL;

-- 4º
SELECT format('INSERT INTO planos SELECT * FROM json_populate_recordset(null::planos, %L);',
              json_agg(t)) AS comando
FROM planos t;

-- ============================================================================
-- Depois de colar os 4 INSERTs na homol, rode ESTE bloco na homol para ajustar as sequences:
-- ============================================================================
-- SELECT setval(pg_get_serial_sequence('grupocategoria','id'),  COALESCE((SELECT MAX(id) FROM grupocategoria),1));
-- SELECT setval(pg_get_serial_sequence('macrocategoria','id'),  COALESCE((SELECT MAX(id) FROM macrocategoria),1));
-- SELECT setval(pg_get_serial_sequence('subcategoria','id'),    COALESCE((SELECT MAX(id) FROM subcategoria),1));
-- SELECT setval(pg_get_serial_sequence('planos','id'),          COALESCE((SELECT MAX(id) FROM planos),1));
-- (descomente as linhas; se alguma tabela for IDENTITY e retornar NULL, ignore o erro dessa linha)
