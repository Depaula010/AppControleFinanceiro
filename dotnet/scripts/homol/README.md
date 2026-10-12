# Scripts de homologação

## Opção A — pelo HeidiSQL (mais simples)

Arquivos em `heidisql/`. Você precisa da sessão do HeidiSQL já conectada ao `postgres-central` (a mesma que gerou o `schema.sql` antigo).

1. **Inspeção** — conectado em `bot_whatsapp_prod`, abra `01-inspecao-producao.sql` e execute tudo (Shift+Ctrl+F9). São só `SELECT`. Salve os resultados (botão direito na grade → *Exportar resultados da grade* → CSV) ou me mande o que ele mostrar, principalmente as consultas 1, 4, 6, 8, 9, 10, 11, 12 e 14. Nenhuma consulta devolve hash, chave ou token, só formato e contagem.
2. **Criar o banco** — rode `02-criar-banco-homol.sql` (uma linha: `CREATE DATABASE bot_whatsapp_homol`). Atualize a lista de bancos com F5.
3. **Criar a estrutura** — conectado em `bot_whatsapp_homol`, abra `dotnet/specs/foundation/schema-prod.sql` no HeidiSQL e execute tudo (F9). O arquivo já é PostgreSQL válido (24 tabelas, 33 FKs, 33 índices, função e 9 triggers de `updated_at`), sem dados. **Não use** o *Exportar banco de dados como SQL* do HeidiSQL para isso: o export dele mistura sintaxe MySQL (`KEY (...)`, `DELIMITER`, `RETURNS UNKNOWN`) e falha no Postgres com erros como `no language specified`.
4. **Dados de referência** — conectado em `bot_whatsapp_prod`, execute os 4 `SELECT` de `03-gerar-dados-de-referencia.sql`, copie a célula de cada resultado e execute na `bot_whatsapp_homol`, **na ordem**. Depois rode o bloco de `setval` comentado no fim do arquivo.
5. **Conferir** — `04-conferir-homol.sql` na `bot_whatsapp_homol`: as tabelas devem ser as mesmas da produção, `usuarios` e `transacoes` com 0 linhas e as 3 subcategorias exigidas presentes.

Se o HeidiSQL só exibir parte do texto da célula no passo 4, use *Exportar banco de dados como SQL* só com as tabelas `grupocategoria`, `macrocategoria`, `subcategoria` e `planos` com **Dados: Inserir**, importe na homol e rode depois `DELETE FROM macrocategoria WHERE usuario_id IS NOT NULL;` (as subcategorias de usuário caem em cascata).

## Opção B — pelo servidor (shell)

Rodam **no servidor Contabo** (o host `postgres-central` só resolve dentro da rede Docker `rede-global`). Nada aqui toca em dados de produção: `dump-schema.sh` só lê estrutura e catálogos; `create-homol.sh` só lê categorias e planos da produção e escreve exclusivamente em `bot_whatsapp_homol`.

## Passo a passo

```bash
ssh root@212.47.65.37
cd /opt/meu-secretario
git pull                                  # traz dotnet/scripts/homol (após o commit)
cd dotnet/scripts/homol
chmod +x *.sh

./dump-schema.sh                          # gera ./out/schema-prod.sql, db-info.txt, row-counts.txt, tipo-transacao.txt
./create-homol.sh                         # cria bot_whatsapp_homol com a estrutura + categorias globais + planos
```

Depois, no seu computador, traga os arquivos de `out/` (scp) e coloque em `dotnet/specs/foundation/`:

```bash
scp root@212.47.65.37:/opt/meu-secretario/dotnet/scripts/homol/out/{schema-prod.sql,db-info.txt,row-counts.txt,tipo-transacao.txt} dotnet/specs/foundation/
```

Antes de commitar `schema-prod.sql`, confira que contém só estrutura: `grep -c "COPY\|INSERT" schema-prod.sql` deve ser 0.

## O que cada saída responde

| Arquivo | Pergunta que responde (ver `data-model.md` §6) |
|---|---|
| `schema-prod.sql` | colunas atuais, CHECKs, índices parciais, identity vs serial, triggers |
| `db-info.txt` | versão do Postgres, `TimeZone`, `lc_time` (desvio 009), triggers, CHECKs, versão do Alembic |
| `row-counts.txt` | quais tabelas realmente têm dados (potes, assinaturas, calendar alerts) |
| `tipo-transacao.txt` | existem linhas com `Receita`? (desvio 001) |

## Variáveis opcionais

`ENV_FILE` (padrão `/opt/meu-secretario/.env`), `NETWORK` (`rede-global`), `PG_IMAGE` (`postgres:15-alpine`), `HOMOL_DB` (deve terminar em `_homol`), `SCHEMA_FILE`.

## Segurança

- O script lê `DATABASE_URL` do `.env` do servidor; nenhuma senha está neste repositório.
- `create-homol.sh` recusa continuar se o nome do banco de homol não terminar em `_homol` ou se for igual ao de produção, e não recria um banco que já exista.
- A connection string da produção foi exposta numa conversa de planejamento: rotacione a senha do Postgres na virada.
- `dotnet/scripts/homol/out/` está no `.gitignore`.
