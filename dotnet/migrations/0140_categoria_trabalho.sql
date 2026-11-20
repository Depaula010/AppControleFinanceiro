-- 0140: macro global "Trabalho" (custos com trabalho: Claude Code, gasolina, marmita) - desvio 286.
-- Aditiva e idempotente: só insere se não existir macro global "Trabalho"; nunca altera nem apaga linhas existentes
-- e nunca toca linhas de usuário. Fica no grupo "Despesa Essencial", então entra em tipo=Despesa sem mudar o filtro.
DO $$
DECLARE
    v_grupo_id integer;
    v_macro_id integer;
BEGIN
    SELECT id INTO v_grupo_id FROM grupocategoria WHERE nome_grupo = 'Despesa Essencial';
    IF v_grupo_id IS NULL THEN
        RETURN;
    END IF;

    IF EXISTS (SELECT 1 FROM macrocategoria WHERE nome_macro = 'Trabalho' AND usuario_id IS NULL) THEN
        RETURN;
    END IF;

    INSERT INTO macrocategoria (grupo_id, nome_macro, ordem_macro)
    VALUES (v_grupo_id, 'Trabalho', 6)
    RETURNING id INTO v_macro_id;

    INSERT INTO subcategoria (macro_id, nome_sub)
    VALUES (v_macro_id, 'Ferramentas e Assinaturas (Claude Code, etc.)'),
           (v_macro_id, 'Combustível e Deslocamento (Trabalho)'),
           (v_macro_id, 'Alimentação no Trabalho (Marmita)'),
           (v_macro_id, 'Equipamentos e Material de Trabalho'),
           (v_macro_id, 'Outros');
END
$$;
