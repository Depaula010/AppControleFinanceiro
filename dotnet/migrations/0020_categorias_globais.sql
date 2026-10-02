-- 0020: categorias globais (grupos, macros, subcategorias). Substitui o populate-global-categories do Flask,
-- que apagava tudo antes de inserir. Idempotente: grupos só se faltarem; macros e subcategorias globais
-- só se NÃO existir nenhuma macro nem subcategoria global (em produção não insere nada).
-- Nunca apaga nem altera linhas existentes e nunca toca linhas de usuário (usuario_id não nulo).
DO $$
DECLARE
    precisa_seed boolean;
BEGIN
    INSERT INTO grupocategoria (nome_grupo)
    SELECT v.nome
    FROM (VALUES (1, 'Renda'), (2, 'Despesa Essencial'), (3, 'Meta Financeira'),
                 (4, 'Despesa Discricionária'), (5, 'Geral')) AS v(n, nome)
    WHERE NOT EXISTS (SELECT 1 FROM grupocategoria g WHERE g.nome_grupo = v.nome)
    ORDER BY v.n;

    SELECT NOT EXISTS (SELECT 1 FROM macrocategoria WHERE usuario_id IS NULL)
       AND NOT EXISTS (SELECT 1 FROM subcategoria WHERE usuario_id IS NULL)
      INTO precisa_seed;

    IF precisa_seed THEN
        INSERT INTO macrocategoria (grupo_id, nome_macro, ordem_macro)
        SELECT g.id, v.macro, v.ordem
        FROM (VALUES
            (1, 'Renda', 'Renda Principal', 1),
            (2, 'Renda', 'Renda Extra e Passiva', 2),
            (3, 'Renda', 'Investimentos e Reembolsos', 3),
            (4, 'Renda', 'Outras Entradas', 4),
            (5, 'Despesa Essencial', 'Moradia e Contas', 1),
            (6, 'Despesa Essencial', 'Alimentação Essencial', 2),
            (7, 'Despesa Essencial', 'Saúde e Bem-Estar Básico', 3),
            (8, 'Despesa Essencial', 'Educação Essencial', 4),
            (9, 'Despesa Essencial', 'Transporte Essencial', 5),
            (10, 'Meta Financeira', 'Construção de Riqueza', 1),
            (11, 'Meta Financeira', 'Gerenciamento de Dívidas', 2),
            (12, 'Meta Financeira', 'Educação de Alto Custo', 3),
            (13, 'Despesa Discricionária', 'Lazer e Entretenimento', 1),
            (14, 'Despesa Discricionária', 'Assinaturas e Serviços', 2),
            (15, 'Despesa Discricionária', 'Cuidados Pessoais', 3),
            (16, 'Despesa Discricionária', 'Compras Pessoais', 4),
            (17, 'Geral', 'Despesas Gerais', NULL),
            (18, 'Geral', 'Receitas Gerais', NULL)
        ) AS v(n, grupo, macro, ordem)
        JOIN grupocategoria g ON g.nome_grupo = v.grupo
        ORDER BY v.n;

        INSERT INTO subcategoria (macro_id, nome_sub)
        SELECT m.id, v.sub
        FROM (VALUES
            (1, 'Renda Principal', 'Salário Fixo / Pró-labore'),
            (2, 'Renda Principal', 'Remuneração Variável / Comissões'),
            (3, 'Renda Extra e Passiva', 'Aluguéis Recebidos'),
            (4, 'Renda Extra e Passiva', 'Freelances / Projetos Avulsos'),
            (5, 'Investimentos e Reembolsos', 'Dividendos / Juros (Rendimentos)'),
            (6, 'Investimentos e Reembolsos', 'Restituições / Reembolsos'),
            (7, 'Outras Entradas', 'Presentes em Dinheiro / Doações'),
            (8, 'Outras Entradas', 'Venda de Bens (Pontual)'),
            (9, 'Moradia e Contas', 'Aluguel / Financiamento'),
            (10, 'Moradia e Contas', 'Condomínio / IPTU / Taxas'),
            (11, 'Moradia e Contas', 'Eletricidade / Água / Gás'),
            (12, 'Moradia e Contas', 'Internet / Telefone Fixo'),
            (13, 'Moradia e Contas', 'Manutenção da Casa'),
            (14, 'Alimentação Essencial', 'Supermercado / Mercearia'),
            (15, 'Alimentação Essencial', 'Feira / Hortifrúti'),
            (16, 'Alimentação Essencial', 'Suplementos e Itens Básicos'),
            (17, 'Saúde e Bem-Estar Básico', 'Plano de Saúde / Convênio'),
            (18, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo'),
            (19, 'Saúde e Bem-Estar Básico', 'Consultas e Exames (não cobertos)'),
            (20, 'Educação Essencial', 'Mensalidades Escolares / Faculdade'),
            (21, 'Educação Essencial', 'Material Didático e Livros'),
            (22, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)'),
            (23, 'Transporte Essencial', 'Transporte Público (Bilhetes/Passe)'),
            (24, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)'),
            (25, 'Transporte Essencial', 'IPVA / Seguro do Veículo'),
            (26, 'Construção de Riqueza', 'Aporte à Reserva de Emergência'),
            (27, 'Construção de Riqueza', 'Investimentos para Aposentadoria'),
            (28, 'Construção de Riqueza', 'Investimentos de Curto Prazo'),
            (29, 'Gerenciamento de Dívidas', 'Quitação de Empréstimos (Principal)'),
            (30, 'Gerenciamento de Dívidas', 'Juros e Multas (Dívidas)'),
            (31, 'Educação de Alto Custo', 'Pós-graduação, Intercâmbio'),
            (32, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery'),
            (33, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais'),
            (34, 'Lazer e Entretenimento', 'Viagens e Hospedagem'),
            (35, 'Assinaturas e Serviços', 'Streaming (Netflix, Spotify, etc.)'),
            (36, 'Assinaturas e Serviços', 'Softwares / Apps Pagos'),
            (37, 'Assinaturas e Serviços', 'Academia / Clube'),
            (38, 'Cuidados Pessoais', 'Salão de Beleza / Barbearia'),
            (39, 'Cuidados Pessoais', 'Cosméticos e Higiene (não básica)'),
            (40, 'Compras Pessoais', 'Roupas, Calçados e Acessórios'),
            (41, 'Compras Pessoais', 'Eletrônicos e Gadgets'),
            (42, 'Compras Pessoais', 'Presentes (Aniversários, Datas)')
        ) AS v(n, macro, sub)
        JOIN macrocategoria m ON m.nome_macro = v.macro AND m.usuario_id IS NULL
        ORDER BY v.n;

        INSERT INTO subcategoria (macro_id, nome_sub)
        SELECT m.id, 'Outros'
        FROM macrocategoria m
        WHERE m.usuario_id IS NULL
        ORDER BY m.id;
    END IF;
END
$$;
