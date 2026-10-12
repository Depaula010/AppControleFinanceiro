-- ============================================================================
-- 01 — INSPEÇÃO DA PRODUÇÃO (somente leitura)
-- Rodar conectado em bot_whatsapp_prod. Só SELECT em catálogos e contagens.
-- No HeidiSQL: abra este arquivo na aba Consulta e execute com Shift+Ctrl+F9
-- ("Executar todas as consultas"): cada SELECT gera uma aba de resultado.
-- Depois salve cada resultado: botão direito na grade > Exportar resultados da grade
-- (ou copie e cole no chat). Respondem as incertezas de data-model.md §6.
-- ============================================================================

-- 1. Versão, fuso e idioma de datas (desvio 009: TO_CHAR 'Day' depende de lc_time)
SELECT version() AS versao,
       current_setting('TimeZone') AS timezone,
       current_setting('lc_time')  AS lc_time;

-- 2. Revisão do Alembic
SELECT version_num FROM alembic_version;

-- 3. Colunas de todas as tabelas (substitui o dump antigo de março)
SELECT table_name, ordinal_position AS pos, column_name, data_type,
       character_maximum_length AS tam, numeric_precision AS prec, numeric_scale AS esc,
       is_nullable AS nulo, column_default AS padrao, is_identity AS identity
FROM information_schema.columns
WHERE table_schema = 'public'
ORDER BY table_name, ordinal_position;

-- 4. Constraints (PK, FK, UNIQUE, CHECK), incluindo o CHECK de tipo_transacao (desvio 001)
SELECT conrelid::regclass AS tabela, conname AS constraint_nome,
       CASE contype WHEN 'p' THEN 'PK' WHEN 'f' THEN 'FK' WHEN 'u' THEN 'UNIQUE'
                    WHEN 'c' THEN 'CHECK' ELSE contype::text END AS tipo,
       pg_get_constraintdef(oid) AS definicao
FROM pg_constraint
WHERE connamespace = 'public'::regnamespace
ORDER BY conrelid::regclass::text, contype, conname;

-- 5. Índices (inclui parciais)
SELECT tablename AS tabela, indexname AS indice, indexdef AS definicao
FROM pg_indexes
WHERE schemaname = 'public'
ORDER BY tablename, indexname;

-- 6. Triggers (o dump do HeidiSQL não os listou)
SELECT event_object_table AS tabela, trigger_name, action_timing AS quando,
       event_manipulation AS evento, action_statement AS acao
FROM information_schema.triggers
WHERE trigger_schema = 'public'
ORDER BY event_object_table, trigger_name;

-- 7. Funções do schema public (ex.: update_updated_at_column)
SELECT p.proname AS funcao, pg_get_functiondef(p.oid) AS definicao
FROM pg_proc p
WHERE p.pronamespace = 'public'::regnamespace;

-- 8. Linhas por tabela (só números)
SELECT relname AS tabela, n_live_tup AS linhas_aprox
FROM pg_stat_user_tables
ORDER BY relname;

-- 9. Existem transações com 'Receita'? (desvio 001)
SELECT tipo_transacao, count(*) AS qtd
FROM transacoes
GROUP BY tipo_transacao
ORDER BY tipo_transacao;

-- 10. Formato dos hashes de senha (apenas o prefixo do algoritmo, nunca o hash)
SELECT split_part(senha_hash, '$', 1) AS algoritmo, count(*) AS qtd
FROM usuarios
WHERE senha_hash IS NOT NULL
GROUP BY 1;

-- 11. Formato de api_key_automate: Fernet ou texto puro? (só contagem e tamanho)
SELECT (api_key_automate LIKE 'gAAAAA%') AS parece_fernet,
       min(length(api_key_automate)) AS menor, max(length(api_key_automate)) AS maior,
       count(*) AS qtd
FROM usuarios
WHERE api_key_automate IS NOT NULL
GROUP BY 1;

-- 12. Tokens do Google estão cifrados? (só prefixo de 6 caracteres do formato, sem o token)
SELECT (access_token LIKE 'gAAAAA%') AS access_parece_fernet,
       (refresh_token LIKE 'gAAAAA%') AS refresh_parece_fernet,
       count(*) AS qtd
FROM googlecalendartokens
GROUP BY 1, 2;

-- 13. Subcategorias globais duplicadas? (os LIMIT 1 por nome dependem de unicidade)
SELECT macro_id, nome_sub, count(*) AS qtd
FROM subcategoria
WHERE usuario_id IS NULL
GROUP BY macro_id, nome_sub
HAVING count(*) > 1;

-- 14. Colunas que o código usa e o dump de março não tinha (esperado: 1 linha por coluna existente)
SELECT table_name, column_name
FROM information_schema.columns
WHERE table_schema = 'public'
  AND (table_name, column_name) IN (
    ('transacoes','observacoes'), ('transacoes','origem'),
    ('consentimentousuario','versao_termos'), ('consentimentousuario','ip_endereco'),
    ('consentimentousuario','user_agent'), ('logacessochaveapi','data_acesso'),
    ('planos','nome'), ('assinaturasusuario','ativo'),
    ('usuarios','whatsapp_numero'),
    ('notificationconfigs','agenda_diaria_ativa'), ('notificationconfigs','agenda_diaria_hora'))
ORDER BY 1, 2;

-- 15. Existe budgetviolations?
SELECT to_regclass('public.budgetviolations') AS budgetviolations;
