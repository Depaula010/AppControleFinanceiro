-- ============================================================================
-- Importação das faturas do cartão Inter (CSV do app do banco): vencimento 20/10/2026 e 20/11/2026.
-- Gerado em 2026-11-20 a partir de fatura-inter-2026-10 (1).csv e fatura-inter-2026-11.csv (195 lançamentos).
--
-- Regras desta carga (decididas com o Rafael):
--   * "PAGTO DEBITO AUTOMATICO" (pagamento da fatura anterior) fica de fora: já existe como Pagamento Fatura.
--   * Estorno SHOPEE EImportsBrasil (-23,99 em 19/09) abatido da compra de 81,01 (vira 57,02, com observação).
--   * Parcelas ("Parcela n/N") entram como "DESCRIÇÃO (n/N)", na data da parcela (data da compra + n-1 meses,
--     limitada ao ciclo da fatura) e ligadas à fatura do CSV; o desvio 271 pede esse formato.
--   * Descrição = estabelecimento do Inter sem a cidade/país e sem espaços repetidos (como o "ZP ROGGI 62899" já em produção).
--   * A fatura de cada lançamento é a do ARQUIVO (não a regra de ciclo do sistema: o Inter põe o dia 13 na fatura seguinte).
--   * Duplicidade: lançamento da mesma conta, mesmo valor, descrição parecida (8 primeiros caracteres) e mesma fatura
--     ou mesma data, já existente, NÃO é inserido (respeita repetições legítimas, ex.: 3 x ZIG R$ 8,00 no mesmo dia).
--
-- Só SQL comum (sem DO $$): roda no HeidiSQL (F9 executa tudo) e no psql. Termina em ROLLBACK de propósito:
-- rode, confira as abas de resultado e, se estiver certo, troque o ROLLBACK final por COMMIT e rode de novo.
-- Faça um pg_dump antes do COMMIT. Idempotente: depois de commitado, rodar de novo não insere nada (tudo vira "já existe").
-- ============================================================================
BEGIN;

-- 1) Cartão Inter: tem de haver exatamente um cartão de crédito com "inter" no nome.
CREATE TEMP TABLE _conta AS
SELECT id AS conta_id, usuario_id, nome_conta, dia_fechamento, dia_vencimento
FROM contas
WHERE tipo_conta = 'Cartão de Crédito' AND nome_conta ILIKE '%inter%' AND ativa;

SELECT * FROM _conta;  -- confira: deve ser UMA linha, com fechamento 13 e vencimento 20

SELECT 1 / (CASE WHEN (SELECT count(*) FROM _conta) = 1 THEN 1 ELSE 0 END) AS conta_unica_ou_aborta;

-- 2) Lançamentos do CSV (valor positivo = despesa).
CREATE TEMP TABLE _import (
  ordem int, data date, descricao text, valor numeric(15,2), macro text, sub text, fb_macro text, fb_sub text,
  parc_n int, parc_total int, obs text, venc date, cat_inter text
);
INSERT INTO _import VALUES
  (1, '2026-10-12', 'ABC ATACADO E VAREJO L', 14.37, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (2, '2026-10-12', 'BMB 31994001072', 30.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (3, '2026-10-12', 'BMB 31994001072', 30.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (4, '2026-10-12', 'PG FOCO RADICAL', 66.79, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SERVICOS'),
  (5, '2026-10-12', 'ABC ATACADO E VAREJO L', 85.25, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (6, '2026-10-11', 'EMPORIO FRUTARIA BH', 70.06, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (7, '2026-10-11', 'IFD DELIVERY88 LTDA', 74.61, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (8, '2026-10-11', 'COKOMBI AGUA DE COCO N', 15.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (9, '2026-10-10', 'ZIG ESPACO CULTURAL PU', 38.80, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (10, '2026-10-10', 'MARCUS V DE O DOMICIAN', 67.75, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (11, '2026-10-10', 'ZIG ESPACO CULTURAL PU', 14.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (12, '2026-10-10', 'POSTO JARDIM INDUSTRIA', 100.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'TRANSPORTE'),
  (13, '2026-10-09', 'TBR AUTOMOTIVO', 25.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (14, '2026-10-09', 'MISTER PAO', 8.78, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (15, '2026-10-09', 'ABC ATACADO E VAREJO L', 79.40, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (16, '2026-10-08', 'Sympla FREDERICO BRUNO', 34.66, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SERVICOS'),
  (17, '2026-10-08', 'Sympla CONSORCIO GESTO (1/2)', 163.00, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, 1, 2, NULL, '2026-10-20', 'CULTURA'),
  (18, '2026-10-08', 'FRIGORIFICO SOFIA', 35.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (19, '2026-10-07', 'CAPPTA BAR DO COREU L', 32.90, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'BARES'),
  (20, '2026-10-06', 'ABC ATACADO E VAREJO L', 44.55, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (21, '2026-10-06', 'ANAGALLES', 55.00, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (22, '2026-10-06', 'MP MALIBU', 34.36, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (23, '2026-10-05', 'S2P 2ARSOLUTION', 149.90, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (24, '2026-10-04', 'FARMACIA POPULAR', 24.99, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (25, '2026-10-04', 'FARMACIA POPULAR', 49.98, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (26, '2026-10-04', 'BEM QUE TEM', 27.47, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (27, '2026-10-03', 'Cartao de TODOS out', 33.40, 'Saúde e Bem-Estar Básico', 'Plano de Saúde / Convênio', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SERVICOS'),
  (28, '2026-10-03', 'REDE SANTANA', 150.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'TRANSPORTE'),
  (29, '2026-10-03', 'ABC ATACADO E VAREJO L', 73.27, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (30, '2026-10-03', 'Vindi Ddpizzariasabo', 217.20, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (31, '2026-10-02', 'HORTIFRUTI CARINHO DA', 6.26, 'Alimentação Essencial', 'Feira / Hortifrúti', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (32, '2026-10-02', 'MAMPV PANIFICADORA LTD', 12.83, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (33, '2026-10-02', 'AO GOSTO', 34.90, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (34, '2026-10-02', 'Barreiro', 69.90, 'Educação Essencial', 'Material Didático e Livros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'LIVRARIAS'),
  (35, '2026-10-02', 'PAPELARIA E COPIADORA', 124.96, 'Educação Essencial', 'Material Didático e Livros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'LIVRARIAS'),
  (36, '2026-10-02', 'ELETROLUZES', 12.80, 'Moradia e Contas', 'Manutenção da Casa', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'CONSTRUCAO'),
  (37, '2026-10-02', 'LUIZ AFONSO COSTA GOME', 50.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (38, '2026-09-29', 'MERCADOLIVRE MERCADOL', 35.00, 'Compras Pessoais', 'Roupas, Calçados e Acessórios', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'VESTUARIO'),
  (39, '2026-09-29', 'IOF INTERNACIONAL', 3.85, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (40, '2026-09-28', 'RAIA4348', 26.59, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (41, '2026-09-28', 'ANTHROPIC CLAUDE SUB', 110.00, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (42, '2026-09-27', 'SHOPEE GrupoLUZZOO', 137.88, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (43, '2026-09-27', 'MERCADOLIVRE TAYCAN', 59.89, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (44, '2026-09-27', 'ABC ATACADO E VAREJO L', 46.24, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (45, '2026-09-27', 'HORTIFRUTI CARINHO DA', 8.91, 'Alimentação Essencial', 'Feira / Hortifrúti', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (46, '2026-09-26', 'STILO BIKE MIRANTE B', 20.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'ESPORTES'),
  (47, '2026-09-26', 'SANTO POSTO GAS', 150.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'TRANSPORTE'),
  (48, '2026-09-26', 'IFD BONGA BONGA DELIVE', 32.89, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (49, '2026-09-23', 'ABC CARNES', 52.60, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (50, '2026-09-23', 'ARMANDO NACIONAS E IMP', 63.00, 'Educação Essencial', 'Material Didático e Livros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'LIVRARIAS'),
  (51, '2026-09-23', 'SUPERMERCADOS BH', 27.96, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (52, '2026-09-23', 'HORTIFRUTI CARINHO DA', 10.62, 'Alimentação Essencial', 'Feira / Hortifrúti', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (53, '2026-09-23', 'RAIA4348', 51.09, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (54, '2026-09-23', 'AMAZONMKTPLC JOAOPEDRO', 42.00, 'Compras Pessoais', 'Roupas, Calçados e Acessórios', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'VESTUARIO'),
  (55, '2026-09-22', 'Google YouTubePremium', 53.90, 'Assinaturas e Serviços', 'Streaming (Netflix, Spotify, etc.)', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'ENTRETENIMENTO'),
  (56, '2026-09-22', 'CROA CENTRO DE REABI (1/6)', 674.10, 'Saúde e Bem-Estar Básico', 'Consultas e Exames (não cobertos)', NULL, NULL, 1, 6, NULL, '2026-10-20', 'COMPRAS'),
  (57, '2026-09-22', 'GAPO APOIO A PESQUISA (1/6)', 166.90, 'Saúde e Bem-Estar Básico', 'Consultas e Exames (não cobertos)', NULL, NULL, 1, 6, NULL, '2026-10-20', 'SAUDE'),
  (58, '2026-09-21', 'RAIA4348', 23.45, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (59, '2026-09-21', 'LUIZ AFONSO COSTA GOME', 90.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (60, '2026-09-21', 'ABC ATACADO E VAREJO L', 17.35, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (61, '2026-09-20', 'MP FELIPEGABRIEL', 12.00, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'TRANSPORTE'),
  (62, '2026-09-20', 'COCO Y CANA', 35.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (63, '2026-09-20', 'RAIA4348', 123.78, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (64, '2026-09-19', 'IOF INTERNACIONAL', 3.85, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (65, '2026-09-19', 'IFD 44 799 019 WILSON', 41.92, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (66, '2026-09-19', 'MAMPV PANIFICADORA LTD', 19.98, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (67, '2026-09-19', 'IOF INTERNACIONAL', 0.94, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-10-20', 'OUTROS'),
  (68, '2026-09-19', 'SHOPEE EImportsBrasil', 57.02, 'Compras Pessoais', 'Roupas, Calçados e Acessórios', NULL, NULL, NULL, NULL, 'Compra 81,01 menos estorno 23,99 (fatura Inter)', '2026-10-20', 'VESTUARIO'),
  (69, '2026-09-19', 'RAIA4348', 27.98, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (70, '2026-09-18', 'ABC ATACADO E VAREJO L', 55.01, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (71, '2026-09-18', 'RAIA4348', 13.99, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (72, '2026-09-18', 'RAIA4348', 13.99, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'DROGARIA'),
  (73, '2026-09-18', 'COCO Y CANA', 23.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (74, '2026-09-17', 'ANTHROPIC CLAUDE SUB', 110.00, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (75, '2026-09-17', 'FRIGORIFICO SOFIA', 40.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (76, '2026-09-16', 'JuQuebradinha2', 22.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'BARES'),
  (77, '2026-09-15', 'FRIGORIFICO SOFIA', 20.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (78, '2026-09-15', 'ABC ATACADO E VAREJO L', 48.99, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (79, '2026-09-14', 'Wellhub Rafael De Paul', 183.20, 'Assinaturas e Serviços', 'Academia / Clube', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SERVICOS'),
  (80, '2026-09-14', 'CONTABO PAYMENT', 26.78, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-10-20', 'COMPRAS'),
  (81, '2026-09-13', 'MP BEBIDAS', 4.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SUPERMERCADO'),
  (82, '2026-09-13', 'PG FOCO RADICAL', 110.68, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'SERVICOS'),
  (83, '2026-09-13', 'EMPORIO FRUTARIA BH', 165.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (84, '2026-09-13', 'IFD HOT DOG DO TCHOTCH', 27.97, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-10-20', 'RESTAURANTES'),
  (85, '2026-10-12', 'CENTAURO CE123 (2/2)', 124.99, 'Compras Pessoais', 'Roupas, Calçados e Acessórios', NULL, NULL, 2, 2, NULL, '2026-10-20', 'ESPORTES'),
  (86, '2026-10-12', 'SHOPEE InovarMetaisOn (3/3)', 108.22, 'Despesas Gerais', 'Outros', NULL, NULL, 3, 3, NULL, '2026-10-20', 'COMPRAS'),
  (87, '2026-10-01', 'SHOPEE InovarMetaisOn (2/3)', 108.22, 'Despesas Gerais', 'Outros', NULL, NULL, 2, 3, NULL, '2026-10-20', 'COMPRAS'),
  (88, '2026-09-28', 'ZP ROGGI 62899 (2/6)', 511.00, 'Despesas Gerais', 'Outros', NULL, NULL, 2, 6, NULL, '2026-10-20', 'OUTROS'),
  (89, '2026-09-25', 'DL AliExpress BR BOX (3/3)', 108.68, 'Despesas Gerais', 'Outros', NULL, NULL, 3, 3, NULL, '2026-10-20', 'COMPRAS'),
  (90, '2026-11-12', 'ABC ATACADO E VAREJO L', 53.09, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (91, '2026-11-12', 'COCO Y CANA', 24.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (92, '2026-11-12', 'MP CARNESDAVILLA', 17.50, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (93, '2026-11-11', 'CAPPTA BAR DO COREU L', 28.90, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'BARES'),
  (94, '2026-11-09', 'CENTRAL POPULAR', 24.90, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'DROGARIA'),
  (95, '2026-11-09', 'ABC ATACADO E VAREJO L', 104.18, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (96, '2026-11-09', 'HORTIFRUTI CARINHO DA', 17.98, 'Alimentação Essencial', 'Feira / Hortifrúti', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (97, '2026-11-09', 'SHOP VIA BARREIRO', 11.25, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (98, '2026-11-08', 'MARCUS V DE O DOMICIAN', 119.92, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (99, '2026-11-07', 'ZIG BARREIRAO 2000', 8.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (100, '2026-11-07', 'IFD HUDS BURGER LTDA', 60.12, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (101, '2026-11-07', 'ZIG BARREIRAO 2000', 12.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (102, '2026-11-07', 'ZIG BARREIRAO 2000', 8.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (103, '2026-11-07', 'ZIG BARREIRAO 2000', 8.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (104, '2026-11-07', 'ZIG BARREIRAO 2000', 4.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (105, '2026-11-06', 'MP FELIPEGABRIEL', 12.84, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (106, '2026-11-06', 'IFD RD JAPAS ALIMENTOS', 41.25, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (107, '2026-11-06', 'BARAM BAR E RESTAURANT', 21.90, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (108, '2026-11-04', 'FRIGORIFICO CARNES DA', 20.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (109, '2026-11-03', 'SUPERMERCADO EPA', 26.94, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (110, '2026-11-03', 'Cartao de TODOS nov', 33.40, 'Saúde e Bem-Estar Básico', 'Plano de Saúde / Convênio', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (111, '2026-11-03', 'ABC ATACADO E VAREJO L', 11.99, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (112, '2026-11-03', 'HORTIFRUTI CARINHO DA', 11.56, 'Alimentação Essencial', 'Feira / Hortifrúti', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (113, '2026-11-03', 'ABAST SHELL BOX', 150.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (114, '2026-11-03', 'CAPPTA BAR DO COREU L', 27.90, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'BARES'),
  (115, '2026-11-02', 'CP PARC DUO GOURMET (1/12)', 59.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, 1, 12, NULL, '2026-11-20', 'OUTROS'),
  (116, '2026-11-02', 'BAR E RESTAURANTE LUGA', 6.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (117, '2026-11-02', 'CHRISTIAN FERREIRA', 14.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (118, '2026-10-31', 'MP FELIPEGABRIEL', 39.59, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (119, '2026-10-31', 'PIZZA PRA VOCE', 64.90, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (120, '2026-10-31', 'RAIA4348', 39.58, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'DROGARIA'),
  (121, '2026-10-31', 'MAMPV PANIFICADORA LTD', 16.14, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (122, '2026-10-31', 'VENDAS DE SALGADO', 4.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (123, '2026-10-30', 'Uber UBER TRIP HELP U', 35.95, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (124, '2026-10-30', 'IFD RD JAPAS ALIMENTOS', 39.89, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (125, '2026-10-29', 'FRIGORIFICO SOFIA', 20.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (126, '2026-10-29', 'IOF INTERNACIONAL', 3.85, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (127, '2026-10-29', 'ABC ATACADO E VAREJO L', 11.37, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (128, '2026-10-28', 'ANTHROPIC CLAUDE SUB', 110.00, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (129, '2026-10-28', 'abastec abastece ai', 150.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (130, '2026-10-27', 'Sympla Patrick Antonio', 24.47, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (131, '2026-10-27', 'MERES ESCRITORIOS COMP', 59.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (132, '2026-10-27', 'ABC ATACADO E VAREJO L', 52.58, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (133, '2026-10-26', 'SACOLO BARREIRO', 17.13, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (134, '2026-10-26', 'ABC ATACADO E VAREJO L', 27.96, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (135, '2026-10-25', 'COKOMBI AGUA DE COCO N', 20.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (136, '2026-10-25', 'AO GOSTO', 31.69, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (137, '2026-10-25', 'PG FOCO RADICAL', 92.69, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (138, '2026-10-25', 'COKOMBI AGUA DE COCO N', 15.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (139, '2026-10-25', 'Uber UBER TRIP HELP U', 4.46, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (140, '2026-10-25', 'Uber UBER TRIP HELP U', 25.98, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (141, '2026-10-24', 'CACHORRO QUENTE DO LEK', 54.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (142, '2026-10-24', 'D V RODOVIARIO', 279.00, 'Lazer e Entretenimento', 'Viagens e Hospedagem', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'HOSPEDAGEM'),
  (143, '2026-10-24', 'MAMPV PANIFICADORA LTD', 29.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (144, '2026-10-24', 'POSTO RABONI LTDA', 50.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (145, '2026-10-24', 'Spotpasssolucoes', 20.00, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (146, '2026-10-24', 'Spotpasssolucoes', 23.10, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (147, '2026-10-24', 'Spotpasssolucoes', 9.90, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (148, '2026-10-24', 'Spotpasssolucoes', 23.10, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (149, '2026-10-23', 'HORTIFRUTI CARINHO DA', 12.63, 'Alimentação Essencial', 'Feira / Hortifrúti', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (150, '2026-10-22', 'Google YouTubePremium', 53.90, 'Assinaturas e Serviços', 'Streaming (Netflix, Spotify, etc.)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'ENTRETENIMENTO'),
  (151, '2026-10-22', 'SHOPEE TradeWinStore', 83.40, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (152, '2026-10-22', 'abastec abastece ai', 50.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (153, '2026-10-22', 'FRIGORIFICO SOFIA', 17.50, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (154, '2026-10-21', 'MP MALIBU', 5.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (155, '2026-10-21', 'ABC ATACADO E VAREJO L', 42.39, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (156, '2026-10-21', 'FRIGORIFICO SOFIA', 17.50, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (157, '2026-10-20', 'FRIGORIFICO SOFIA', 20.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (158, '2026-10-19', 'LUIZ AFONSO COSTA GOME', 130.00, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (159, '2026-10-19', 'ABC ATACADO E VAREJO L', 7.37, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (160, '2026-10-19', 'RAIA4348', 26.59, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'DROGARIA'),
  (161, '2026-10-19', 'MP ALIEXPRESS', 136.34, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (162, '2026-10-18', 'IOF INTERNACIONAL', 0.92, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (163, '2026-10-18', 'IOF INTERNACIONAL', 3.85, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (164, '2026-10-18', 'DANI PIZZA E LANCHES', 111.60, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (165, '2026-10-18', 'MP FELIPEGABRIEL', 10.00, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (166, '2026-10-18', 'BAR E RESTAURANTE LUGA', 10.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (167, '2026-10-18', 'PG FOCO RADICAL', 58.69, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (168, '2026-10-18', '49982581 PEDRO AUGUSTO', 22.00, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (169, '2026-10-18', 'ZP OLX RENATA TOLEDO', 185.95, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (170, '2026-10-18', 'IG AgilizeTecno', 209.90, 'Despesas Gerais', 'Outros', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (171, '2026-10-18', 'COKOMBI AGUA DE COCO N', 20.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (172, '2026-10-18', 'COKOMBI AGUA DE COCO N', 20.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'OUTROS'),
  (173, '2026-10-17', 'POSTO JARDIM INDUSTRIA', 150.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (174, '2026-10-17', 'IG TRAMPOLIM', 79.80, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'VIAGEM'),
  (175, '2026-10-17', 'RAIA2857', 50.42, 'Saúde e Bem-Estar Básico', 'Medicamentos de Uso Contínuo', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'DROGARIA'),
  (176, '2026-10-17', 'SACOLO BARREIRO', 9.67, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (177, '2026-10-17', 'RANCHO DO BOI', 136.07, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (178, '2026-10-17', 'ANTHROPIC CLAUDE SUB', 110.00, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (179, '2026-10-17', 'STB PAVAOVIABIJUTERIAS', 18.00, 'Compras Pessoais', 'Roupas, Calçados e Acessórios', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (180, '2026-10-17', 'MP FELIPEGABRIEL', 6.00, 'Transporte Essencial', 'Aplicativos de Transporte (Necessidade)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (181, '2026-10-16', 'BARREIRO V COMERCIO D', 26.00, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (182, '2026-10-16', 'MARCUS V DE O DOMICIAN', 65.06, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (183, '2026-10-16', 'ABC ATACADO E VAREJO L', 15.66, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (184, '2026-10-15', 'FRIGORIFICO SOFIA', 17.50, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (185, '2026-10-15', 'CAPPTA BAR DO COREU L', 32.90, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'BARES'),
  (186, '2026-10-14', 'BOQUINHA LANCHES BARR', 38.85, 'Lazer e Entretenimento', 'Restaurantes, Bares e Delivery', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'RESTAURANTES'),
  (187, '2026-10-14', 'CONTABO PAYMENT', 26.41, 'Trabalho', 'Ferramentas e Assinaturas (Claude Code, etc.)', 'Assinaturas e Serviços', 'Softwares / Apps Pagos', NULL, NULL, NULL, '2026-11-20', 'COMPRAS'),
  (188, '2026-10-14', 'Wellhub Rafael De Paul', 183.20, 'Assinaturas e Serviços', 'Academia / Clube', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SERVICOS'),
  (189, '2026-10-14', 'FRIGORIFICO SOFIA', 37.50, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (190, '2026-10-14', 'SORVETES CAROL', 8.81, 'Alimentação Essencial', 'Supermercado / Mercearia', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'SUPERMERCADO'),
  (191, '2026-10-13', 'abastec abastece ai', 100.00, 'Transporte Essencial', 'Combustível / Recarga (Carro/Moto)', NULL, NULL, NULL, NULL, NULL, '2026-11-20', 'TRANSPORTE'),
  (192, '2026-11-08', 'Sympla CONSORCIO GESTO (2/2)', 162.99, 'Lazer e Entretenimento', 'Passeios e Eventos Culturais', NULL, NULL, 2, 2, NULL, '2026-11-20', 'CULTURA'),
  (193, '2026-10-22', 'GAPO APOIO A PESQUISA (2/6)', 166.90, 'Saúde e Bem-Estar Básico', 'Consultas e Exames (não cobertos)', NULL, NULL, 2, 6, NULL, '2026-11-20', 'SAUDE'),
  (194, '2026-10-22', 'CROA CENTRO DE REABI (2/6)', 674.10, 'Saúde e Bem-Estar Básico', 'Consultas e Exames (não cobertos)', NULL, NULL, 2, 6, NULL, '2026-11-20', 'COMPRAS'),
  (195, '2026-10-28', 'ZP ROGGI 62899 (3/6)', 511.00, 'Despesas Gerais', 'Outros', NULL, NULL, 3, 6, NULL, '2026-11-20', 'OUTROS');

-- 3) Subcategoria de cada lançamento (global ou do usuário; macro/sub de reserva se a categoria nova não existir).
CREATE TEMP TABLE _cat AS
SELECT i.ordem,
       COALESCE(
         (SELECT s.id FROM subcategoria s JOIN macrocategoria m ON m.id = s.macro_id
           WHERE m.nome_macro = i.macro AND s.nome_sub = i.sub
             AND (s.usuario_id IS NULL OR s.usuario_id = c.usuario_id) ORDER BY s.usuario_id NULLS LAST, s.id LIMIT 1),
         (SELECT s.id FROM subcategoria s JOIN macrocategoria m ON m.id = s.macro_id
           WHERE m.nome_macro = i.fb_macro AND s.nome_sub = i.fb_sub
             AND (s.usuario_id IS NULL OR s.usuario_id = c.usuario_id) ORDER BY s.usuario_id NULLS LAST, s.id LIMIT 1)
       ) AS subcategoria_id
FROM _import i CROSS JOIN _conta c;

SELECT 1 / (CASE WHEN NOT EXISTS (SELECT 1 FROM _cat WHERE subcategoria_id IS NULL) THEN 1 ELSE 0 END) AS categorias_ok_ou_aborta;
-- (se abortou: SELECT DISTINCT i.macro, i.sub FROM _import i JOIN _cat USING (ordem) WHERE subcategoria_id IS NULL;
--  mostra a categoria que não existe no banco; crie-a ou troque no VALUES.)

-- 4) Faturas (a de cada arquivo). Se já existe a do (conta, vencimento), usa a existente e não mexe no status.
INSERT INTO faturas (conta_id, data_vencimento, data_fechamento, status)
SELECT c.conta_id, v.venc, v.fech, v.status
FROM _conta c CROSS JOIN (VALUES (DATE '2026-10-20', DATE '2026-10-13', 'Paga'),
                                 (DATE '2026-11-20', DATE '2026-11-13', 'Fechada')) AS v(venc, fech, status)
ON CONFLICT (conta_id, data_vencimento) DO NOTHING;

-- 5) Duplicidade: quantos lançamentos iguais já existem para cada lançamento do CSV.
CREATE TEMP TABLE _plano AS
SELECT i.*, f.id AS fatura_id, c.conta_id, c.usuario_id, ct.subcategoria_id,
       row_number() OVER (PARTITION BY i.venc, lower(regexp_replace(i.descricao, '\s*\(\d+/\d+\)\s*$', '')),
                                       i.valor, CASE WHEN i.parc_total IS NULL THEN i.data END
                          ORDER BY i.ordem) AS rn,
       (SELECT count(*) FROM transacoes t
         WHERE t.conta_id = c.conta_id AND t.tipo_transacao = 'Despesa' AND t.valor = -i.valor
           AND left(regexp_replace(lower(t.descricao), '[^a-z0-9]', '', 'g'), 8)
             = left(regexp_replace(lower(i.descricao), '[^a-z0-9]', '', 'g'), 8)
           AND (t.fatura_id = f.id OR t.data_transacao = i.data)) AS ja_existem
FROM _import i
CROSS JOIN _conta c
JOIN faturas f ON f.conta_id = c.conta_id AND f.data_vencimento = i.venc
JOIN _cat ct ON ct.ordem = i.ordem;

CREATE TEMP TABLE _marca AS SELECT COALESCE(max(id), 0) AS max_id FROM transacoes;

-- 6) Inserção: só o que ainda não existe.
INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, data_transacao,
                        tipo_transacao, consolidada, observacoes, origem)
SELECT usuario_id, conta_id, subcategoria_id, fatura_id, left(descricao, 255), -valor, data,
       'Despesa', TRUE, obs, 'extrato_inter'
FROM _plano
WHERE rn > ja_existem
ORDER BY ordem;

-- 7) Parcelados (desvio 271): a maior parcela de cada compra neste CSV, se ainda há parcela por vir, vira/atualiza o parcelado.
CREATE TEMP TABLE _parc AS
SELECT * FROM (
  SELECT DISTINCT ON (lower(b.base), p.parc_total)
         t.id AS transacao_id, p.usuario_id, p.conta_id, p.subcategoria_id, t.valor, p.parc_n, p.parc_total, b.base,
         extract(day FROM p.data)::int AS data_compra_dia, t.data_transacao
  FROM _plano p
  CROSS JOIN LATERAL (SELECT trim(regexp_replace(p.descricao, '\s*\(\d+/\d+\)\s*$', '')) AS base) b
  JOIN transacoes t ON t.conta_id = p.conta_id AND t.fatura_id = p.fatura_id AND t.descricao = left(p.descricao, 255)
                   AND t.tipo_transacao = 'Despesa' AND t.valor = -p.valor
  WHERE p.parc_total IS NOT NULL
  ORDER BY lower(b.base), p.parc_total, p.parc_n DESC, t.id DESC
) x
WHERE x.parc_n < x.parc_total;  -- a maior parcela da compra ainda não é a última

-- 7a) Já existe parcelado ativo da mesma compra: avança o contador (se a parcela é mais nova) e liga a transação.
UPDATE agendamentos a
SET parcelas_executadas = p.parc_n, ativo = (p.parc_n < p.parc_total), updated_at = now()
FROM _parc p
WHERE a.usuario_id = p.usuario_id AND a.conta_id = p.conta_id AND a.ativo AND a.tipo_agendamento = 'PARCELADO'
  AND a.total_parcelas = p.parc_total AND lower(trim(a.descricao)) = lower(p.base)
  AND COALESCE(a.parcelas_executadas, 0) < p.parc_n;

UPDATE transacoes t
SET agendamento_id = a.id
FROM _parc p
JOIN agendamentos a ON a.usuario_id = p.usuario_id AND a.conta_id = p.conta_id AND a.tipo_agendamento = 'PARCELADO'
                   AND a.total_parcelas = p.parc_total AND lower(trim(a.descricao)) = lower(p.base)
WHERE t.id = p.transacao_id AND t.agendamento_id IS NULL AND a.parcelas_executadas = p.parc_n;

-- 7b) Não existe: cria o parcelado (próxima parcela um mês depois) e liga a transação.
CREATE TEMP TABLE _novos_ag AS
WITH novos AS (
  INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                            periodicidade, data_inicio, dia_execucao, total_parcelas, parcelas_executadas, ativo)
  SELECT p.usuario_id, p.conta_id, p.subcategoria_id, p.base, -p.valor, 'PARCELADO', 'MENSAL',
         (p.data_transacao + INTERVAL '1 month')::date, p.data_compra_dia, p.parc_total, p.parc_n, TRUE
  FROM _parc p
  WHERE NOT EXISTS (SELECT 1 FROM agendamentos a
                    WHERE a.usuario_id = p.usuario_id AND a.conta_id = p.conta_id AND a.tipo_agendamento = 'PARCELADO'
                      AND a.total_parcelas = p.parc_total AND lower(trim(a.descricao)) = lower(p.base))
  RETURNING id, usuario_id, conta_id, descricao, total_parcelas
)
SELECT * FROM novos;

UPDATE transacoes t
SET agendamento_id = n.id
FROM _parc p
JOIN _novos_ag n ON n.usuario_id = p.usuario_id AND n.conta_id = p.conta_id AND n.descricao = p.base
                AND n.total_parcelas = p.parc_total
WHERE t.id = p.transacao_id AND t.agendamento_id IS NULL;

-- ============================ CONFERÊNCIA (antes do COMMIT) ============================
-- A) O que foi inserido e o que já existia (ja_existia = quantos iguais o banco já tinha).
SELECT p.ordem, p.venc AS fatura, p.data, p.descricao, p.valor,
       CASE WHEN p.rn > p.ja_existem THEN 'INSERIDO' ELSE 'JA EXISTIA (pulado)' END AS situacao,
       p.ja_existem, p.macro || ' / ' || p.sub AS categoria
FROM _plano p ORDER BY p.venc, p.ordem;

-- B) Possíveis duplicatas que a regra automática NÃO pegou: transação já existente, mesma conta e valor, a até 3 dias,
--    com descrição diferente (ex.: lançada pelo WhatsApp como "mercado"). Revise; se for a mesma compra, apague a nova.
SELECT p.venc AS fatura, p.data AS data_csv, p.descricao AS csv, p.valor, t.id AS transacao_existente,
       t.data_transacao, t.descricao AS existente, t.origem
FROM _plano p
JOIN transacoes t ON t.conta_id = p.conta_id AND t.tipo_transacao = 'Despesa' AND t.valor = -p.valor
                 AND abs(t.data_transacao - p.data) <= 3
                 AND (t.id <= (SELECT max_id FROM _marca) OR t.origem IS DISTINCT FROM 'extrato_inter')
WHERE p.rn > p.ja_existem
ORDER BY p.venc, p.data;

-- C) Total de cada fatura no banco (depois da carga) contra o total do CSV. Devem bater; se o banco tiver MAIS, há
--    lançamento extra antigo na fatura; se tiver MENOS, algo do CSV foi pulado como duplicado em outra fatura.
SELECT f.data_vencimento, f.status,
       (SELECT COALESCE(-sum(t.valor), 0) FROM transacoes t WHERE t.fatura_id = f.id AND t.tipo_transacao = 'Despesa') AS total_no_banco,
       CASE f.data_vencimento WHEN DATE '2026-10-20' THEN 6209.55 ELSE 6215.32 END AS total_csv_sem_pagamento
FROM faturas f JOIN _conta c ON c.conta_id = f.conta_id
WHERE f.data_vencimento IN (DATE '2026-10-20', DATE '2026-11-20');

-- D) Parcelados criados/atualizados.
SELECT a.id, a.descricao, a.valor_previsto, a.total_parcelas, a.parcelas_executadas, a.data_inicio, a.dia_execucao, a.ativo
FROM agendamentos a JOIN _parc p ON p.usuario_id = a.usuario_id AND p.conta_id = a.conta_id
 AND a.tipo_agendamento = 'PARCELADO' AND a.total_parcelas = p.parc_total AND lower(trim(a.descricao)) = lower(p.base);

-- Troque por COMMIT depois de conferir (A–D).
ROLLBACK;
