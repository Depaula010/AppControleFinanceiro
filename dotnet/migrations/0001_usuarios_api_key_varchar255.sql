-- 0001 (desvio 017): alarga usuarios.api_key_automate de varchar(100) para varchar(255).
-- Um token Fernet de uma chave de 43 caracteres tem 140 e não cabia na coluna.
-- Só alarga (sem perda de dados) e é idempotente: repetir não muda nada.
ALTER TABLE usuarios ALTER COLUMN api_key_automate TYPE varchar(255);
