-- ============================================================================
-- 07 — COPIAR OS DADOS DO USUÁRIO DA PRODUÇÃO PARA O HOMOL
--
-- Por quê: sem as contas, faturas, agendamentos e transações o homol não permite comparar o .NET com o Flask.
--
-- Como (3 etapas):
--   A) Conectado em bot_whatsapp_prod, rode CADA SELECT da seção "GERAR" (só leitura). Cada um devolve UMA célula
--      com um comando INSERT. Copie a célula inteira (botão direito na célula > Copiar) e execute na
--      bot_whatsapp_homol, NA ORDEM (1, 2, 3, ...).
--   B) Na bot_whatsapp_homol, rode a seção "AJUSTAR SEQUÊNCIAS" no fim.
--   C) Na bot_whatsapp_homol, confira com a seção "CONFERIR".
--
-- NÃO são copiados (de propósito): baileys_auth (sessão do seu WhatsApp), googlecalendartokens, chavesapiusuario,
-- logacessochaveapi, rastreamentousoapi, alembic_version. Nada aqui escreve na produção.
--
-- ATENÇÃO: a tabela usuarios leva o hash da senha, o telefone e a api_key_automate. O homol é seu, mas o .NET em
-- homol DEVE ficar com BOT_WHATSAPP_URL vazia (não envia WhatsApp) ou apontada só para o seu número de teste.
--
-- Se o HeidiSQL cortar o texto da célula (tabela grande, como transacoes), use a variação por faixa de id no fim.
-- ============================================================================

-- ---------------------------------------------------------------- GERAR (rodar na PRODUÇÃO) -----------------------

-- 1. usuarios
SELECT 'INSERT INTO usuarios OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::usuarios, '
       || quote_literal(coalesce(json_agg(u), '[]'::json)::text) || '::json);' AS sql_1_usuarios
FROM usuarios u;

-- 2. categorias criadas pelo usuário (as globais já vieram no passo 4 do README)
SELECT 'INSERT INTO macrocategoria OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::macrocategoria, '
       || quote_literal(coalesce(json_agg(m), '[]'::json)::text) || '::json);' AS sql_2_macrocategoria_do_usuario
FROM macrocategoria m
WHERE m.usuario_id IS NOT NULL;

-- 3. subcategorias dessas categorias
SELECT 'INSERT INTO subcategoria OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::subcategoria, '
       || quote_literal(coalesce(json_agg(s), '[]'::json)::text) || '::json);' AS sql_3_subcategoria_do_usuario
FROM subcategoria s
WHERE s.macrocategoria_id IN (SELECT id FROM macrocategoria WHERE usuario_id IS NOT NULL);

-- 4. contas
SELECT 'INSERT INTO contas OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::contas, '
       || quote_literal(coalesce(json_agg(c), '[]'::json)::text) || '::json);' AS sql_4_contas
FROM contas c;

-- 5. faturas
SELECT 'INSERT INTO faturas OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::faturas, '
       || quote_literal(coalesce(json_agg(f), '[]'::json)::text) || '::json);' AS sql_5_faturas
FROM faturas f;

-- 6. agendamentos
SELECT 'INSERT INTO agendamentos OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::agendamentos, '
       || quote_literal(coalesce(json_agg(a), '[]'::json)::text) || '::json);' AS sql_6_agendamentos
FROM agendamentos a;

-- 7. transacoes (a FK de transferencia_par_id é conferida no fim do comando, então um INSERT só basta)
SELECT 'INSERT INTO transacoes OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::transacoes, '
       || quote_literal(coalesce(json_agg(t), '[]'::json)::text) || '::json);' AS sql_7_transacoes
FROM transacoes t;

-- 8. potes e subcategorias dos potes
SELECT 'INSERT INTO potesdegastos OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::potesdegastos, '
       || quote_literal(coalesce(json_agg(p), '[]'::json)::text) || '::json);' AS sql_8_potesdegastos
FROM potesdegastos p;

SELECT 'INSERT INTO potesubcategorias OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::potesubcategorias, '
       || quote_literal(coalesce(json_agg(p), '[]'::json)::text) || '::json);' AS sql_9_potesubcategorias
FROM potesubcategorias p;

-- 9. configurações e demais tabelas do usuário
SELECT 'INSERT INTO notificationconfigs OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::notificationconfigs, '
       || quote_literal(coalesce(json_agg(n), '[]'::json)::text) || '::json);' AS sql_10_notificationconfigs
FROM notificationconfigs n;

SELECT 'INSERT INTO monthlyreportconfigs OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::monthlyreportconfigs, '
       || quote_literal(coalesce(json_agg(m), '[]'::json)::text) || '::json);' AS sql_11_monthlyreportconfigs
FROM monthlyreportconfigs m;

SELECT 'INSERT INTO calendaralertconfigs OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::calendaralertconfigs, '
       || quote_literal(coalesce(json_agg(c), '[]'::json)::text) || '::json);' AS sql_12_calendaralertconfigs
FROM calendaralertconfigs c;

SELECT 'INSERT INTO enderecosfavoritos OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::enderecosfavoritos, '
       || quote_literal(coalesce(json_agg(e), '[]'::json)::text) || '::json);' AS sql_13_enderecosfavoritos
FROM enderecosfavoritos e;

SELECT 'INSERT INTO consentimentousuario OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::consentimentousuario, '
       || quote_literal(coalesce(json_agg(c), '[]'::json)::text) || '::json);' AS sql_14_consentimentousuario
FROM consentimentousuario c;

SELECT 'INSERT INTO assinaturasusuario OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::assinaturasusuario, '
       || quote_literal(coalesce(json_agg(a), '[]'::json)::text) || '::json);' AS sql_15_assinaturasusuario
FROM assinaturasusuario a;

SELECT 'INSERT INTO preferenciaschaveapi OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::preferenciaschaveapi, '
       || quote_literal(coalesce(json_agg(p), '[]'::json)::text) || '::json);' AS sql_16_preferenciaschaveapi
FROM preferenciaschaveapi p;

-- ------------------------------------------- AJUSTAR SEQUÊNCIAS (rodar no HOMOL, depois dos INSERTs) ---------------

SELECT setval(pg_get_serial_sequence('usuarios', 'id'), greatest((SELECT coalesce(max(id), 1) FROM usuarios), 1));
SELECT setval(pg_get_serial_sequence('macrocategoria', 'id'), greatest((SELECT coalesce(max(id), 1) FROM macrocategoria), 1));
SELECT setval(pg_get_serial_sequence('subcategoria', 'id'), greatest((SELECT coalesce(max(id), 1) FROM subcategoria), 1));
SELECT setval(pg_get_serial_sequence('contas', 'id'), greatest((SELECT coalesce(max(id), 1) FROM contas), 1));
SELECT setval(pg_get_serial_sequence('faturas', 'id'), greatest((SELECT coalesce(max(id), 1) FROM faturas), 1));
SELECT setval(pg_get_serial_sequence('agendamentos', 'id'), greatest((SELECT coalesce(max(id), 1) FROM agendamentos), 1));
SELECT setval(pg_get_serial_sequence('transacoes', 'id'), greatest((SELECT coalesce(max(id), 1) FROM transacoes), 1));

-- ------------------------------------------------------------ CONFERIR (rodar no HOMOL) ---------------------------
-- Esperado (produção em 2026-10-09): 1 usuário, 4 contas, 12 faturas, 38 agendamentos, 330 transações.
SELECT 'usuarios' AS tabela, count(*) AS linhas FROM usuarios
UNION ALL SELECT 'contas', count(*) FROM contas
UNION ALL SELECT 'faturas', count(*) FROM faturas
UNION ALL SELECT 'agendamentos', count(*) FROM agendamentos
UNION ALL SELECT 'transacoes', count(*) FROM transacoes
UNION ALL SELECT 'baileys_auth (deve ser 0)', count(*) FROM baileys_auth
UNION ALL SELECT 'googlecalendartokens (deve ser 0)', count(*) FROM googlecalendartokens;

-- ------------------------------------- VARIAÇÃO POR FAIXA (se a célula de transacoes vier cortada) -----------------
-- Rode na PRODUÇÃO uma vez por faixa e execute cada resultado no HOMOL. Ajuste os limites de id.
-- SELECT 'INSERT INTO transacoes OVERRIDING SYSTEM VALUE SELECT * FROM json_populate_recordset(NULL::transacoes, '
--        || quote_literal(coalesce(json_agg(t), '[]'::json)::text) || '::json);'
-- FROM transacoes t WHERE t.id BETWEEN 1 AND 150;
-- (como transferencia_par_id referencia a própria tabela, mantenha o par de uma transferência na mesma faixa)
