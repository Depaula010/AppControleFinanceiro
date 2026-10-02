#!/usr/bin/env bash
# Cria o banco de homologação (bot_whatsapp_homol) com a ESTRUTURA de produção
# e apenas as categorias globais e planos (dados de referência, sem dados de usuário).
# Rodar no servidor Contabo. NUNCA escreve na base de produção: lê dela (somente SELECT/pg_dump)
# e escreve apenas na base de homol, recusando-se a continuar se o nome não terminar em _homol.
set -euo pipefail

cd "$(dirname "$0")"
ENV_FILE="${ENV_FILE:-/opt/meu-secretario/.env}"
NETWORK="${NETWORK:-rede-global}"
IMAGE="${PG_IMAGE:-postgres:15-alpine}"
HOMOL_DB="${HOMOL_DB:-bot_whatsapp_homol}"
SCHEMA_FILE="${SCHEMA_FILE:-./out/schema-prod.sql}"

case "$HOMOL_DB" in *_homol) ;; *) echo "HOMOL_DB deve terminar em _homol (recebido: $HOMOL_DB)"; exit 1;; esac
[ -f "$SCHEMA_FILE" ] || { echo "Rode antes ./dump-schema.sh ($SCHEMA_FILE não existe)"; exit 1; }

PROD_URL="$(grep -E '^DATABASE_URL=' "$ENV_FILE" | head -1 | cut -d= -f2-)"
PROD_URL="${PROD_URL/#postgres:\/\//postgresql://}"
PROD_DB="${PROD_URL##*/}"; PROD_DB="${PROD_DB%%\?*}"
[ "$PROD_DB" != "$HOMOL_DB" ] || { echo "Banco de homol igual ao de produção. Abortando."; exit 1; }

BASE_URL="${PROD_URL%/*}"                 # postgresql://user:pass@host:5432
ADMIN_URL="$BASE_URL/postgres"
HOMOL_URL="$BASE_URL/$HOMOL_DB"

pg() { docker run --rm -i --network "$NETWORK" "$IMAGE" "$@"; }

echo "==> Verificando se $HOMOL_DB já existe"
EXISTS="$(pg psql "$ADMIN_URL" -X -A -t -c "SELECT 1 FROM pg_database WHERE datname='$HOMOL_DB'")"
if [ "$EXISTS" = "1" ]; then
  echo "O banco $HOMOL_DB já existe. Para recriar, apague manualmente (DROP DATABASE $HOMOL_DB) e rode de novo."
  exit 1
fi

echo "==> Criando $HOMOL_DB"
pg psql "$ADMIN_URL" -X -v ON_ERROR_STOP=1 -c "CREATE DATABASE $HOMOL_DB"

echo "==> Aplicando a estrutura de produção"
pg psql "$HOMOL_URL" -X -v ON_ERROR_STOP=1 < "$SCHEMA_FILE"

echo "==> Copiando dados de referência (categorias globais e planos) da produção"
# Somente leitura na produção. Ordem respeita as FKs.
copy_ref() { # tabela, filtro
  docker run --rm -i --network "$NETWORK" -e PROD="$PROD_URL" -e HOMOL="$HOMOL_URL" "$IMAGE" sh -c \
    "psql \"\$PROD\" -X -c \"\\copy (SELECT * FROM $1 $2) TO STDOUT\" | psql \"\$HOMOL\" -X -v ON_ERROR_STOP=1 -c \"\\copy $1 FROM STDIN\""
}
copy_ref grupocategoria ""
copy_ref macrocategoria "WHERE usuario_id IS NULL"
copy_ref subcategoria  "WHERE usuario_id IS NULL"
copy_ref planos ""

echo "==> Acertando sequences após a cópia"
pg psql "$HOMOL_URL" -X -A -t -c "
SELECT format('SELECT setval(%L, COALESCE((SELECT MAX(%I) FROM %I),1))', pg_get_serial_sequence(quote_ident(t.relname), a.attname), a.attname, t.relname)
FROM pg_class t JOIN pg_attribute a ON a.attrelid=t.oid
WHERE t.relkind='r' AND t.relnamespace='public'::regnamespace AND a.attnum>0
  AND pg_get_serial_sequence(quote_ident(t.relname), a.attname) IS NOT NULL" \
 | pg psql "$HOMOL_URL" -X -A -t > /dev/null

echo "==> Conferência"
pg psql "$HOMOL_URL" -X -c "\dt"
pg psql "$HOMOL_URL" -X -c "SELECT 'grupocategoria' t, count(*) FROM grupocategoria UNION ALL SELECT 'macrocategoria', count(*) FROM macrocategoria UNION ALL SELECT 'subcategoria', count(*) FROM subcategoria UNION ALL SELECT 'planos', count(*) FROM planos UNION ALL SELECT 'usuarios', count(*) FROM usuarios"

echo
echo "Homol pronto. Connection string (não commitar):"
echo "  DATABASE_URL=${HOMOL_URL//:*@/:****@}"
echo "Crie um usuário de teste com POST /auth/register no ambiente de homol."
