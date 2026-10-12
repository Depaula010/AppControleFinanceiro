-- ============================================================================
-- 02 — CRIAR O BANCO DE HOMOLOGAÇÃO
-- Conecte no mesmo servidor (postgres-central), no banco "postgres" ou "bot_whatsapp_prod".
-- Execute APENAS esta linha. Não mexe em nada existente.
-- ============================================================================
CREATE DATABASE bot_whatsapp_homol ENCODING 'UTF8';

-- Em seguida, no HeidiSQL:
--   a) Atualize a lista de bancos (F5) e confirme que bot_whatsapp_homol apareceu.
--   b) Siga o passo 3 do README (exportar a ESTRUTURA da produção e importar na homol).
