-- Feature 016: chave da agenda do Coach por usuário. Só o hash (SHA-256 em hex) fica no banco; a chave aparece uma vez, ao ser gerada.
-- Uma chave ativa por pessoa (índice parcial). Aditiva e idempotente.
CREATE TABLE IF NOT EXISTS coach_agenda_chaves (
    id            SERIAL PRIMARY KEY,
    usuario_id    INTEGER NOT NULL REFERENCES usuarios (id) ON DELETE CASCADE,
    chave_hash    VARCHAR(64) NOT NULL UNIQUE,
    final         VARCHAR(4) NOT NULL,
    criada_em     TIMESTAMP NOT NULL DEFAULT NOW(),
    ultimo_uso_em TIMESTAMP NULL,
    revogada_em   TIMESTAMP NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_coach_agenda_chaves_ativa ON coach_agenda_chaves (usuario_id) WHERE revogada_em IS NULL;
