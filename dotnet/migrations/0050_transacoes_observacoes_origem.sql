-- Feature 004 (Transações), desvio 027: colunas que o código do Flask já grava e o banco nunca teve.
-- Só aditiva e idempotente. Sem CHECK em origem (o código usa também 'api_endpoint'); linhas antigas ficam NULL.
ALTER TABLE transacoes ADD COLUMN IF NOT EXISTS observacoes text NULL;
ALTER TABLE transacoes ADD COLUMN IF NOT EXISTS origem varchar(20) NULL;
