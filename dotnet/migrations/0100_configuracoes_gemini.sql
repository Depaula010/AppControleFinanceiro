-- Desvio 263: modelo e versão da API do Gemini passam a viver no banco (antes: GEMINI_MODEL e uma constante no código).
-- Troca por SQL, sem deploy: UPDATE configuracoes SET valor = 'gemini-2.5-flash' WHERE chave = 'gemini.modelo';
-- O app relê a cada 60 s. Linha ausente ou valor inválido => cai no padrão (GEMINI_MODEL / v1beta).
-- Aditiva e idempotente: não sobrescreve valor que alguém já tenha ajustado.
CREATE TABLE IF NOT EXISTS configuracoes (
    chave         VARCHAR(100) PRIMARY KEY,
    valor         VARCHAR(255) NOT NULL,
    descricao     TEXT NULL,
    atualizado_em TIMESTAMP NOT NULL DEFAULT NOW()
);

INSERT INTO configuracoes (chave, valor, descricao) VALUES
    ('gemini.modelo', 'gemini-2.5-flash-lite', 'Modelo do Gemini usado nas intenções do WhatsApp (ex.: gemini-2.5-flash-lite).'),
    ('gemini.versao_api', 'v1beta', 'Versão da API REST do Gemini no caminho da URL (ex.: v1beta, v1).')
ON CONFLICT (chave) DO NOTHING;
