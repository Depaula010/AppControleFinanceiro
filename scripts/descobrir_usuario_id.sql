-- ============================================================================
-- SCRIPT: DESCOBRIR SEU USUARIO_ID
-- ============================================================================
--
-- Execute este script para encontrar o ID do seu usuário no sistema.
-- Você precisará desse ID para cadastrar suas chaves de API.
--
-- ============================================================================

-- ----------------------------------------------------------------------------
-- Opção 1: Buscar por email
-- ----------------------------------------------------------------------------

-- Substitua 'seu-email@exemplo.com' pelo seu email real
SELECT
    id AS usuario_id,
    nome,
    email,
    whatsapp_numero,
    criado_em
FROM Usuarios
WHERE email = 'seu-email@exemplo.com';

-- Exemplo:
-- SELECT * FROM Usuarios WHERE email = 'rafael@email.com';


-- ----------------------------------------------------------------------------
-- Opção 2: Buscar por WhatsApp
-- ----------------------------------------------------------------------------

-- Substitua '5531940012345' pelo seu número de WhatsApp
SELECT
    id AS usuario_id,
    nome,
    email,
    whatsapp_numero,
    criado_em
FROM Usuarios
WHERE whatsapp_numero = '5531940012345';

-- Exemplo:
-- SELECT * FROM Usuarios WHERE whatsapp_numero = '5531940012345';


-- ----------------------------------------------------------------------------
-- Opção 3: Listar TODOS os usuários
-- ----------------------------------------------------------------------------

-- Se você é administrador ou único usuário, liste todos:
SELECT
    id AS usuario_id,
    nome,
    email,
    whatsapp_numero,
    criado_em,
    CASE
        WHEN id = 1 THEN '← Provavelmente é você'
        ELSE ''
    END as observacao
FROM Usuarios
ORDER BY id;


-- ----------------------------------------------------------------------------
-- Opção 4: Buscar por nome (parcial)
-- ----------------------------------------------------------------------------

-- Busca por parte do nome (case insensitive)
SELECT
    id AS usuario_id,
    nome,
    email,
    whatsapp_numero
FROM Usuarios
WHERE LOWER(nome) LIKE LOWER('%Rafael%')
ORDER BY id;


-- ============================================================================
-- INFORMAÇÕES ADICIONAIS DO USUÁRIO
-- ============================================================================

-- Ver informações completas do usuário ID específico
SELECT
    u.id AS usuario_id,
    u.nome,
    u.email,
    u.whatsapp_numero,
    u.cidade,
    u.estado,
    u.criado_em,
    -- Verificar se tem chaves cadastradas
    (SELECT COUNT(*) FROM ChavesApiUsuario WHERE usuario_id = u.id) AS total_chaves,
    -- Verificar se tem preferências
    (SELECT COUNT(*) FROM PreferenciasChaveApi WHERE usuario_id = u.id) AS total_preferencias,
    -- Verificar plano atual
    (SELECT p.nome FROM AssinaturasUsuario a
     JOIN Planos p ON a.plano_id = p.id
     WHERE a.usuario_id = u.id AND a.ativo = TRUE
     LIMIT 1) AS plano_atual
FROM Usuarios u
WHERE u.id = 1;  -- Substitua 1 pelo ID encontrado


-- Ver se o usuário JÁ tem chaves cadastradas
SELECT
    u.id AS usuario_id,
    u.nome,
    COUNT(DISTINCT c.provedor) AS chaves_cadastradas,
    COUNT(DISTINCT p.provedor) AS preferencias_configuradas,
    STRING_AGG(DISTINCT c.provedor, ', ') AS provedores_com_chave,
    STRING_AGG(DISTINCT p.provedor, ', ') AS provedores_com_preferencia
FROM Usuarios u
LEFT JOIN ChavesApiUsuario c ON c.usuario_id = u.id AND c.ativo = TRUE
LEFT JOIN PreferenciasChaveApi p ON p.usuario_id = u.id
WHERE u.id = 1  -- Substitua 1 pelo ID encontrado
GROUP BY u.id, u.nome;


-- ============================================================================
-- VERIFICAR ESTRUTURA DAS TABELAS (caso esteja com dúvidas)
-- ============================================================================

-- Ver colunas da tabela Usuarios
SELECT
    column_name,
    data_type,
    is_nullable
FROM information_schema.columns
WHERE table_name = 'usuarios'
ORDER BY ordinal_position;


-- Ver quantos usuários existem no sistema
SELECT
    COUNT(*) AS total_usuarios,
    COUNT(CASE WHEN email IS NOT NULL THEN 1 END) AS usuarios_com_email,
    COUNT(CASE WHEN whatsapp_numero IS NOT NULL THEN 1 END) AS usuarios_com_whatsapp
FROM Usuarios;


-- ============================================================================
-- DICAS
-- ============================================================================

/*
💡 DICAS PARA ENCONTRAR SEU USUÁRIO:

1. Se você usa WhatsApp para acessar o sistema:
   → Use a Opção 2 (buscar por WhatsApp)

2. Se você se cadastrou com email:
   → Use a Opção 1 (buscar por email)

3. Se você é o único usuário do sistema:
   → Seu ID provavelmente é 1
   → Use a Opção 3 para confirmar

4. Se nada funcionar:
   → Liste todos os usuários (Opção 3)
   → Identifique qual é você pelo nome ou email

⚠️ IMPORTANTE: Anote o 'usuario_id' encontrado!
   Você precisará dele para cadastrar as chaves.

📝 Exemplo de resultado:

 usuario_id | nome          | email              | whatsapp_numero
------------+---------------+--------------------+------------------
          1 | Rafael Silva  | rafael@email.com   | 5531940012345

Neste caso, seu usuario_id é: 1

🎯 Próximo passo:
   Use o usuario_id encontrado no script:
   python scripts/inserir_minhas_chaves.py
*/


-- ============================================================================
-- FIM DO SCRIPT
-- ============================================================================
