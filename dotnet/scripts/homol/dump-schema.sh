#!/usr/bin/env bash
# Extrai SOMENTE a estrutura (sem dados) do banco de produção.
# Rodar no servidor Contabo, dentro de /opt/meu-secretario (onde existe o .env).
# Somente leitura: pg_dump --schema-only e consultas em catálogo. Não altera nada.
set -euo pipefail

cd "$(dirname "$0")"
ENV_FILE="${ENV_FILE:-/opt/meu-secretario/.env}"
NETWORK="${NETWORK:-rede-global}"
IMAGE="${PG_IMAGE:-postgres:15-alpine}"   # produção é PostgreSQL 15.x
OUT_DIR="${OUT_DIR:-./out}"

[ -f "$ENV_FILE" ] || { echo "Arquivo $ENV_FILE não encontrado"; exit 1; }
DATABASE_URL="$(grep -E '^DATABASE_URL=' "$ENV_FILE" | head -1 | cut -d= -f2-)"
[ -n "$DATABASE_URL" ] || { echo "DATABASE_URL vazio em $ENV_FILE"; exit 1; }
# pg_dump aceita URI postgresql://; normaliza postgres://
DATABASE_URL="${DATABASE_URL/#postgres:\/\//postgresql://}"

mkdir -p "$OUT_DIR"
run_pg() { docker run --rm --network "$NETWORK" -e PGURI="$DATABASE_URL" "$IMAGE" "$@"; }

echo "==> pg_dump --schema-only (tabelas, constraints, índices, sequences, funções, triggers)"
run_pg sh -c 'pg_dump "$PGURI" --schema-only --no-owner --no-privileges --no-comments' \
  > "$OUT_DIR/schema-prod.sql"

echo "==> Metadados que o dump do HeidiSQL não mostrou"
run_pg sh -c 'psql "$PGURI" -X -A -F"|" -t -c "
SELECT '\''version'\'', version();
SELECT '\''timezone'\'', current_setting('\''TimeZone'\'');
SELECT '\''lc_time'\'', current_setting('\''lc_time'\'');
SELECT '\''alembic'\'', version_num FROM alembic_version;
"' > "$OUT_DIR/db-info.txt"

run_pg sh -c 'psql "$PGURI" -X -A -F"|" -t -c "
SELECT '\''trigger'\'', event_object_table, trigger_name, action_timing, event_manipulation
  FROM information_schema.triggers WHERE trigger_schema='\''public'\'' ORDER BY 2,3;
SELECT '\''identity'\'', table_name, column_name, is_identity, column_default
  FROM information_schema.columns WHERE table_schema='\''public'\'' AND (is_identity='\''YES'\'' OR column_default LIKE '\''nextval%'\'') ORDER BY 2,3;
SELECT '\''check'\'', conrelid::regclass, conname, pg_get_constraintdef(oid)
  FROM pg_constraint WHERE contype='\''c'\'' AND connamespace='\''public'\''::regnamespace ORDER BY 2,3;
"' >> "$OUT_DIR/db-info.txt"

echo "==> Contagem de linhas por tabela (apenas números, sem dados)"
run_pg sh -c 'psql "$PGURI" -X -A -F"|" -t -c "
SELECT relname, n_live_tup FROM pg_stat_user_tables ORDER BY relname;
"' > "$OUT_DIR/row-counts.txt"

echo "==> Valores distintos de tipo_transacao (confirma o desvio 001: Receita x Renda)"
run_pg sh -c 'psql "$PGURI" -X -A -F"|" -t -c "
SELECT tipo_transacao, count(*) FROM transacoes GROUP BY 1 ORDER BY 1;
"' > "$OUT_DIR/tipo-transacao.txt"

echo
echo "Pronto. Arquivos em $OUT_DIR:"
ls -l "$OUT_DIR"
echo
echo "Próximo passo: copie schema-prod.sql, db-info.txt e row-counts.txt para"
echo "  dotnet/specs/foundation/  (NÃO commite dados; estes arquivos contêm só estrutura e contagens)."
echo "Confira antes de commitar: grep -iE 'password|senha_hash|api_key' $OUT_DIR/schema-prod.sql  (deve ter só nomes de coluna)."
