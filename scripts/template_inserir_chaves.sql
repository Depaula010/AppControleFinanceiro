-- ============================================================================
-- TEMPLATE: INSERIR CHAVES DE API NO BANCO DE DADOS
-- ============================================================================
--
-- ATENÇÃO: Este é apenas um TEMPLATE!
-- Você precisa CRIPTOGRAFAR as chaves antes de inserir.
--
-- Siga o guia em: scripts/GUIA_INSERIR_CHAVES.md
-- Use o script Python: python scripts/inserir_minhas_chaves.py
--
-- ============================================================================

-- ----------------------------------------------------------------------------
-- PASSO 1: Descobrir seu usuario_id
-- ----------------------------------------------------------------------------

-- Execute este comando para encontrar seu ID:
SELECT id, nome, email, whatsapp_numero
FROM Usuarios
ORDER BY id;

-- Anote o 'id' do seu usuário. Exemplo: 1


-- ----------------------------------------------------------------------------
-- PASSO 2: Criptografar suas chaves
-- ----------------------------------------------------------------------------

-- Execute no Python (dentro do diretório do projeto):
/*
from app.services.encryption_service import encryption_service

# Suas chaves (NÃO CRIPTOGRAFADAS)
gemini = "AIzaSyABCD1234567890XYZ"
weather = "a1b2c3d4e5f6g7h8i9j0"
openroute = "5b3ce3597851110001cf6248"

# Criptografar
print("Gemini:", encryption_service.encrypt(gemini))
print("Weather:", encryption_service.encrypt(weather))
print("OpenRoute:", encryption_service.encrypt(openroute))
*/

-- Copie as strings criptografadas (começam com "gAAAAAB...")


-- ----------------------------------------------------------------------------
-- PASSO 3: Inserir chaves criptografadas
-- ----------------------------------------------------------------------------

-- ⚠️ SUBSTITUA:
--    {USUARIO_ID} pelo seu ID (ex: 1)
--    {GEMINI_ENCRYPTED} pela string criptografada do Gemini
--    {WEATHER_ENCRYPTED} pela string criptografada do Weather
--    {OPENROUTE_ENCRYPTED} pela string criptografada do OpenRoute

BEGIN;

-- 3.1. Inserir chave do Google Gemini
INSERT INTO ChavesApiUsuario
    (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
VALUES
    ({USUARIO_ID}, 'gemini', '{GEMINI_ENCRYPTED}', TRUE, NOW(), NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    chave_api_criptografada = EXCLUDED.chave_api_criptografada,
    ativo = TRUE,
    atualizado_em = NOW();

-- 3.2. Inserir chave do WeatherAPI
INSERT INTO ChavesApiUsuario
    (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
VALUES
    ({USUARIO_ID}, 'weather', '{WEATHER_ENCRYPTED}', TRUE, NOW(), NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    chave_api_criptografada = EXCLUDED.chave_api_criptografada,
    ativo = TRUE,
    atualizado_em = NOW();

-- 3.3. Inserir chave do OpenRouteService
INSERT INTO ChavesApiUsuario
    (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
VALUES
    ({USUARIO_ID}, 'openroute', '{OPENROUTE_ENCRYPTED}', TRUE, NOW(), NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    chave_api_criptografada = EXCLUDED.chave_api_criptografada,
    ativo = TRUE,
    atualizado_em = NOW();

COMMIT;


-- ----------------------------------------------------------------------------
-- PASSO 4: Configurar preferências (usar chave própria = grátis)
-- ----------------------------------------------------------------------------

BEGIN;

-- 4.1. Configurar para usar chave própria do Gemini (GRÁTIS)
INSERT INTO PreferenciasChaveApi
    (usuario_id, provedor, usar_chave_propria, atualizado_em)
VALUES
    ({USUARIO_ID}, 'gemini', TRUE, NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    usar_chave_propria = TRUE,
    atualizado_em = NOW();

-- 4.2. Configurar para usar chave própria do Weather (GRÁTIS)
INSERT INTO PreferenciasChaveApi
    (usuario_id, provedor, usar_chave_propria, atualizado_em)
VALUES
    ({USUARIO_ID}, 'weather', TRUE, NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    usar_chave_propria = TRUE,
    atualizado_em = NOW();

-- 4.3. Configurar para usar chave própria do OpenRoute (GRÁTIS)
INSERT INTO PreferenciasChaveApi
    (usuario_id, provedor, usar_chave_propria, atualizado_em)
VALUES
    ({USUARIO_ID}, 'openroute', TRUE, NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    usar_chave_propria = TRUE,
    atualizado_em = NOW();

COMMIT;


-- ============================================================================
-- VERIFICAÇÃO: Confirmar que funcionou
-- ============================================================================

-- Ver chaves cadastradas (sem mostrar o conteúdo criptografado)
SELECT
    id,
    provedor,
    ativo,
    LENGTH(chave_api_criptografada) as tamanho_criptografado,
    ultimo_uso_em,
    criado_em,
    atualizado_em
FROM ChavesApiUsuario
WHERE usuario_id = {USUARIO_ID}
ORDER BY provedor;

-- Resultado esperado:
-- id | provedor  | ativo | tamanho_criptografado | ...
-- ---+-----------+-------+-----------------------+-----
-- 42 | gemini    | t     | ~150                  | ...
-- 43 | openroute | t     | ~150                  | ...
-- 44 | weather   | t     | ~150                  | ...


-- Ver preferências configuradas
SELECT
    id,
    provedor,
    usar_chave_propria,
    CASE
        WHEN usar_chave_propria THEN '✅ Chave própria (GRÁTIS)'
        ELSE '❌ Chave do sistema (PAGO)'
    END as configuracao,
    atualizado_em
FROM PreferenciasChaveApi
WHERE usuario_id = {USUARIO_ID}
ORDER BY provedor;

-- Resultado esperado:
-- provedor  | usar_chave_propria | configuracao
-- ----------+--------------------+---------------------------
-- gemini    | t                  | ✅ Chave própria (GRÁTIS)
-- openroute | t                  | ✅ Chave própria (GRÁTIS)
-- weather   | t                  | ✅ Chave própria (GRÁTIS)


-- ============================================================================
-- COMANDOS ÚTEIS
-- ============================================================================

-- Verificar quanto você usou no mês atual
SELECT
    provedor,
    tipo_chave,
    quantidade_chamadas,
    mes_ano
FROM RastreamentoUsoApi
WHERE usuario_id = {USUARIO_ID}
  AND mes_ano = TO_CHAR(CURRENT_DATE, 'YYYY-MM')
ORDER BY provedor;


-- Ver logs de acesso (últimos 50)
SELECT
    provedor,
    tipo_chave,
    operacao,
    sucesso,
    mensagem_erro,
    criado_em
FROM LogAcessoChaveApi
WHERE usuario_id = {USUARIO_ID}
ORDER BY criado_em DESC
LIMIT 50;


-- Trocar para usar chave do SISTEMA (pago) para um provedor
-- UPDATE PreferenciasChaveApi
-- SET usar_chave_propria = FALSE,
--     atualizado_em = NOW()
-- WHERE usuario_id = {USUARIO_ID}
--   AND provedor = 'gemini';


-- Voltar para usar chave PRÓPRIA (grátis)
-- UPDATE PreferenciasChaveApi
-- SET usar_chave_propria = TRUE,
--     atualizado_em = NOW()
-- WHERE usuario_id = {USUARIO_ID}
--   AND provedor = 'gemini';


-- ============================================================================
-- DELETAR TUDO (recomeçar do zero) - USE COM CUIDADO!
-- ============================================================================

-- ⚠️ CUIDADO: Isso apaga TODAS as suas chaves e preferências!
-- Descomente apenas se tiver certeza:

-- BEGIN;
-- DELETE FROM LogAcessoChaveApi WHERE usuario_id = {USUARIO_ID};
-- DELETE FROM RastreamentoUsoApi WHERE usuario_id = {USUARIO_ID};
-- DELETE FROM PreferenciasChaveApi WHERE usuario_id = {USUARIO_ID};
-- DELETE FROM ChavesApiUsuario WHERE usuario_id = {USUARIO_ID};
-- COMMIT;


-- ============================================================================
-- FIM DO SCRIPT
-- ============================================================================
--
-- ✅ Próximos passos:
--
-- 1. Verifique se as queries de verificação retornaram dados corretos
-- 2. Teste enviando mensagem no WhatsApp
-- 3. Verifique os logs do servidor:
--    [GEMINI] ✅ Usando chave de gemini propria para usuário {USUARIO_ID}
--    [WEATHER] ✅ Usando chave de weather propria para usuário {USUARIO_ID}
-- 4. Monitore o uso nos dashboards dos provedores
--
-- 🎉 Pronto! Você está usando suas próprias chaves (grátis)!
--
-- ============================================================================
