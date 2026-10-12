-- Feature 007 (Configurações), desvio 250: antecedência do alerta de vencimento em coluna própria.
-- O Flask gravava daysBeforeDue em alerta_potes_threshold (limiar de % do pote). O valor antigo NÃO é copiado
-- (em produção ele é 1, lixo do bug); linhas existentes recebem o padrão 3. Aditiva e idempotente.
ALTER TABLE notificationconfigs ADD COLUMN IF NOT EXISTS dias_antes_vencimento integer NOT NULL DEFAULT 3;
