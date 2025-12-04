#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Script para inserir suas chaves de API no banco de dados.
Este script criptografa as chaves e gera os comandos SQL necessários.

Uso:
    python scripts/inserir_minhas_chaves.py

Autor: Meu Secretário - Sistema SaaS
Data: 04/12/2025
"""

import os
import sys
import re
from pathlib import Path
from datetime import datetime

# Adicionar o diretório raiz ao path
root_dir = Path(__file__).parent.parent
sys.path.insert(0, str(root_dir))

# ============================================================================
# CONFIGURAÇÃO - PREENCHA COM SUAS INFORMAÇÕES
# ============================================================================

# 1. Seu ID de usuário no sistema (descubra executando: scripts/descobrir_usuario_id.sql)
USUARIO_ID = 1  # ALTERE AQUI!

# 2. Suas chaves de API (deixe None se não tiver)
GEMINI_KEY = "AIzaSyBDwNho-RI7lr6xFZmVTWk1kayFsbtv8bM"  # Exemplo: "AIzaSyABCD1234567890XYZ"
WEATHER_KEY = "962aa441abd24e82bb4191942252211"  # Exemplo: "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6"
OPENROUTE_KEY = "5b3ce3597851110001cf6248e80a7618a4384a46a7d9b194a6f8bf23"  # Exemplo: "5b3ce3597851110001cf6248a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8"

# ============================================================================
# VALIDAÇÕES E FUNÇÕES AUXILIARES
# ============================================================================

def validar_chave_gemini(chave):
    """Valida formato da chave do Google Gemini."""
    if not chave:
        return False, "Chave vazia"

    # Gemini começa com "AIzaSy" e tem ~39 caracteres
    if not chave.startswith("AIzaSy"):
        return False, "Chave Gemini deve começar com 'AIzaSy'"

    if len(chave) < 35 or len(chave) > 45:
        return False, f"Chave Gemini tem tamanho incorreto ({len(chave)} caracteres, esperado ~39)"

    # Apenas caracteres alfanuméricos, _ e -
    if not re.match(r'^[A-Za-z0-9_-]+$', chave):
        return False, "Chave Gemini contém caracteres inválidos"

    return True, "OK"

def validar_chave_weather(chave):
    """Valida formato da chave do WeatherAPI."""
    if not chave:
        return False, "Chave vazia"

    # WeatherAPI tem 32 caracteres hexadecimais
    if len(chave) != 32:
        return False, f"Chave WeatherAPI deve ter 32 caracteres (encontrados {len(chave)})"

    if not re.match(r'^[a-f0-9]+$', chave, re.IGNORECASE):
        return False, "Chave WeatherAPI deve conter apenas caracteres hexadecimais (0-9, a-f)"

    return True, "OK"

def validar_chave_openroute(chave):
    """Valida formato da chave do OpenRouteService."""
    if not chave:
        return False, "Chave vazia"

    # OpenRouteService tem 58 caracteres alfanuméricos
    if len(chave) != 58:
        return False, f"Chave OpenRouteService deve ter 58 caracteres (encontrados {len(chave)})"

    # Geralmente começa com "5b3ce359"
    if not chave.startswith("5b3ce359"):
        return False, "Chave OpenRouteService geralmente começa com '5b3ce359'"

    if not re.match(r'^[a-f0-9]+$', chave, re.IGNORECASE):
        return False, "Chave OpenRouteService deve conter apenas caracteres hexadecimais"

    return True, "OK"

def verificar_encryption_key():
    """Verifica se a ENCRYPTION_KEY está configurada."""
    try:
        from app.services.encryption_service import encryption_service
        # Tenta criptografar uma string de teste
        teste = encryption_service.encrypt("teste")
        return True, "OK"
    except Exception as e:
        return False, str(e)

# ============================================================================
# SCRIPT PRINCIPAL
# ============================================================================

def main():
    print("=" * 80)
    print("🔐 GERADOR DE SCRIPT SQL PARA CHAVES DE API")
    print("=" * 80)
    print()
    print("Este script irá:")
    print("  1. Validar suas chaves de API")
    print("  2. Criptografá-las com segurança")
    print("  3. Gerar SQL para inserir no banco de dados")
    print()
    print("=" * 80)
    print()

    # Verificar ENCRYPTION_KEY
    print("🔍 Verificando configuração do sistema...")
    enc_ok, enc_msg = verificar_encryption_key()
    if not enc_ok:
        print(f"   ❌ ENCRYPTION_KEY não configurada ou inválida!")
        print(f"   Erro: {enc_msg}")
        print()
        print("   Solução: Configure a variável ENCRYPTION_KEY no arquivo .env")
        print("   Exemplo: ENCRYPTION_KEY=sua-chave-fernet-aqui")
        print()
        return
    print("   ✅ ENCRYPTION_KEY configurada")
    print()

    # Validação do usuario_id
    if USUARIO_ID <= 0:
        print("❌ ERRO: USUARIO_ID inválido!")
        print("   Você precisa definir USUARIO_ID no início do script.")
        print()
        print("   Para descobrir seu ID, execute:")
        print("   psql -f scripts/descobrir_usuario_id.sql")
        print()
        return

    if USUARIO_ID == 1:
        print("⚠️  ATENÇÃO: Você está usando USUARIO_ID = 1 (padrão)")
        print("   Certifique-se de que este é o ID correto!")
        print()
        print("   💡 Dica: Execute o script SQL para descobrir seu ID:")
        print("   psql -f scripts/descobrir_usuario_id.sql")
        print()
        resposta = input("   Continuar mesmo assim? (s/N): ")
        if resposta.lower() != 's':
            print("\n❌ Operação cancelada pelo usuário.")
            return

    print(f"📋 Usuário ID: {USUARIO_ID}")
    print()

    # Validar e coletar chaves
    chaves_para_inserir = {}

    print("🔍 Validando chaves de API...")
    print()

    # Validar Gemini
    if GEMINI_KEY and GEMINI_KEY.strip():
        valido, msg = validar_chave_gemini(GEMINI_KEY.strip())
        if valido:
            chaves_para_inserir['gemini'] = GEMINI_KEY.strip()
            print(f"   ✅ Google Gemini: Válida ({len(GEMINI_KEY)} caracteres)")
        else:
            print(f"   ❌ Google Gemini: {msg}")
            print(f"      Chave fornecida: {GEMINI_KEY[:20]}...")
    else:
        print("   ⚠️  Google Gemini: Não fornecida (pulando)")

    # Validar Weather
    if WEATHER_KEY and WEATHER_KEY.strip():
        valido, msg = validar_chave_weather(WEATHER_KEY.strip())
        if valido:
            chaves_para_inserir['weather'] = WEATHER_KEY.strip()
            print(f"   ✅ WeatherAPI: Válida (32 caracteres)")
        else:
            print(f"   ❌ WeatherAPI: {msg}")
            print(f"      Chave fornecida: {WEATHER_KEY[:20]}...")
    else:
        print("   ⚠️  WeatherAPI: Não fornecida (pulando)")

    # Validar OpenRoute
    if OPENROUTE_KEY and OPENROUTE_KEY.strip():
        valido, msg = validar_chave_openroute(OPENROUTE_KEY.strip())
        if valido:
            chaves_para_inserir['openroute'] = OPENROUTE_KEY.strip()
            print(f"   ✅ OpenRouteService: Válida (58 caracteres)")
        else:
            print(f"   ❌ OpenRouteService: {msg}")
            print(f"      Chave fornecida: {OPENROUTE_KEY[:20]}...")
    else:
        print("   ⚠️  OpenRouteService: Não fornecida (pulando)")

    print()

    # Verificar se tem pelo menos uma chave
    if not chaves_para_inserir:
        print("❌ ERRO: Nenhuma chave válida foi fornecida!")
        print()
        print("   Você precisa editar o script e adicionar pelo menos uma chave:")
        print("   • GEMINI_KEY = 'AIzaSy...'")
        print("   • WEATHER_KEY = '...'")
        print("   • OPENROUTE_KEY = '...'")
        print()
        print("   Consulte os manuais em docs/ para gerar suas chaves.")
        return

    print(f"📊 Total de chaves válidas: {len(chaves_para_inserir)}")
    print()

    # Criptografar chaves
    print("🔐 Criptografando chaves...")
    from app.services.encryption_service import encryption_service

    chaves_criptografadas = {}
    try:
        for provedor, chave in chaves_para_inserir.items():
            encrypted = encryption_service.encrypt(chave)
            chaves_criptografadas[provedor] = encrypted
            print(f"   ✅ {provedor}: Criptografada ({len(encrypted)} bytes)")
        print()
    except Exception as e:
        print(f"   ❌ Erro ao criptografar: {e}")
        print()
        import traceback
        traceback.print_exc()
        return

    print("=" * 80)
    print("📝 SCRIPT SQL GERADO")
    print("=" * 80)
    print()

    # Gerar SQL dinamicamente
    sql_parts = []
    sql_parts.append(f"""-- ============================================================================
-- INSERIR CHAVES DE API DO USUÁRIO {USUARIO_ID}
-- Gerado automaticamente em: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}
-- ============================================================================

BEGIN;
""")

    # Gerar INSERTs apenas para chaves fornecidas
    contador = 1
    nomes_provedores = {
        'gemini': 'Google Gemini',
        'weather': 'WeatherAPI',
        'openroute': 'OpenRouteService'
    }

    for provedor, chave_encrypted in chaves_criptografadas.items():
        nome = nomes_provedores.get(provedor, provedor)
        sql_parts.append(f"""
-- {contador}. Inserir chave do {nome}
INSERT INTO ChavesApiUsuario
    (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
VALUES
    ({USUARIO_ID}, '{provedor}', '{chave_encrypted}', TRUE, NOW(), NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    chave_api_criptografada = EXCLUDED.chave_api_criptografada,
    ativo = TRUE,
    atualizado_em = NOW();
""")
        contador += 1

    sql_parts.append("""
COMMIT;

-- ============================================================================
-- CONFIGURAR PREFERÊNCIAS (USAR CHAVES PRÓPRIAS = GRÁTIS)
-- ============================================================================

BEGIN;
""")

    # Gerar preferências apenas para chaves fornecidas
    for provedor in chaves_criptografadas.keys():
        nome = nomes_provedores.get(provedor, provedor)
        sql_parts.append(f"""
-- {contador}. Configurar preferência do {nome} (usar chave própria = grátis)
INSERT INTO PreferenciasChaveApi
    (usuario_id, provedor, usar_chave_propria, atualizado_em)
VALUES
    ({USUARIO_ID}, '{provedor}', TRUE, NOW())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET
    usar_chave_propria = TRUE,
    atualizado_em = NOW();
""")
        contador += 1

    sql_parts.append(f"""
COMMIT;

-- ============================================================================
-- VERIFICAR INSERÇÕES
-- ============================================================================

-- Listar chaves cadastradas (sem mostrar conteúdo criptografado)
SELECT
    id,
    provedor,
    ativo,
    LENGTH(chave_api_criptografada) as tamanho_criptografado,
    ultimo_uso_em,
    criado_em,
    atualizado_em
FROM ChavesApiUsuario
WHERE usuario_id = {USUARIO_ID}
ORDER BY provedor;

-- Resultado esperado:
-- Você deve ver {len(chaves_criptografadas)} linha(s) com os provedores: {', '.join(chaves_criptografadas.keys())}


-- Listar preferências configuradas
SELECT
    id,
    provedor,
    usar_chave_propria,
    CASE
        WHEN usar_chave_propria THEN '✅ Chave própria (GRÁTIS)'
        ELSE '❌ Chave do sistema (PAGO)'
    END as configuracao,
    atualizado_em
FROM PreferenciasChaveApi
WHERE usuario_id = {USUARIO_ID}
ORDER BY provedor;

-- Resultado esperado:
-- Você deve ver {len(chaves_criptografadas)} linha(s) com "✅ Chave própria (GRÁTIS)"


-- ============================================================================
-- COMANDOS ÚTEIS (DESCOMENTE PARA USAR)
-- ============================================================================

-- Ver uso mensal
-- SELECT
--     provedor,
--     tipo_chave,
--     quantidade_chamadas,
--     mes_ano
-- FROM RastreamentoUsoApi
-- WHERE usuario_id = {USUARIO_ID}
--   AND mes_ano = TO_CHAR(CURRENT_DATE, 'YYYY-MM')
-- ORDER BY provedor;


-- Ver logs de acesso (últimos 50)
-- SELECT
--     provedor,
--     tipo_chave,
--     sucesso,
--     criado_em
-- FROM LogAcessoChaveApi
-- WHERE usuario_id = {USUARIO_ID}
-- ORDER BY criado_em DESC
-- LIMIT 50;


-- Para DELETAR todas as chaves (caso precise recomeçar)
-- ⚠️ CUIDADO: Isso apaga tudo!
-- BEGIN;
-- DELETE FROM LogAcessoChaveApi WHERE usuario_id = {USUARIO_ID};
-- DELETE FROM RastreamentoUsoApi WHERE usuario_id = {USUARIO_ID};
-- DELETE FROM PreferenciasChaveApi WHERE usuario_id = {USUARIO_ID};
-- DELETE FROM ChavesApiUsuario WHERE usuario_id = {USUARIO_ID};
-- COMMIT;


-- ============================================================================
-- FIM DO SCRIPT
-- ============================================================================
-- 🎉 Pronto! Execute este SQL no seu banco de dados PostgreSQL.
-- ============================================================================
""")

    sql = '\n'.join(sql_parts)

    print(sql)

    # Salvar em arquivo
    output_file = root_dir / "scripts" / "output_sql_chaves.sql"
    try:
        os.makedirs(output_file.parent, exist_ok=True)
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write(sql)

        print()
        print("=" * 80)
        print(f"✅ SCRIPT SQL SALVO COM SUCESSO!")
        print("=" * 80)
        print()
        print(f"📁 Arquivo: {output_file}")
        print(f"📊 Tamanho: {len(sql)} bytes")
        print(f"🔑 Chaves inseridas: {', '.join(chaves_criptografadas.keys())}")
        print()

    except Exception as e:
        print()
        print("=" * 80)
        print(f"⚠️  Não foi possível salvar o arquivo: {e}")
        print("=" * 80)
        print()
        print("💡 SOLUÇÃO: Copie o SQL acima manualmente e salve em um arquivo .sql")
        print()

    # Instruções finais
    print("=" * 80)
    print("📋 PRÓXIMOS PASSOS")
    print("=" * 80)
    print()
    print("1️⃣  Conecte ao seu banco de dados PostgreSQL")
    print()

    db_url = os.getenv('DATABASE_URL', 'não configurado')
    if db_url != 'não configurado':
        # Tentar extrair informações básicas da URL
        if '@' in db_url:
            # Formato: postgresql://user:pass@host:port/database
            try:
                parts = db_url.split('@')[1].split('/')[0]
                print(f"    Host: {parts}")
            except:
                print(f"    URL: {db_url}")
    else:
        print("    ⚠️  DATABASE_URL não configurada no .env")

    print()
    print("2️⃣  Execute o script SQL de uma das formas:")
    print()
    print("    Opção A - psql (linha de comando):")
    print(f"    $ psql -f scripts/output_sql_chaves.sql")
    print()
    print("    Opção B - DBeaver/pgAdmin:")
    print("    • Abra uma SQL Console")
    print(f"    • Abra o arquivo: scripts/output_sql_chaves.sql")
    print("    • Execute (F5 ou botão Run)")
    print()
    print("    Opção C - Copiar e colar:")
    print("    • Copie o SQL exibido acima")
    print("    • Cole no seu cliente PostgreSQL")
    print("    • Execute")
    print()
    print("3️⃣  Verifique se funcionou:")
    print()
    print("    Os SELECTs ao final do script mostrarão:")
    print(f"    • {len(chaves_criptografadas)} chave(s) cadastrada(s)")
    print(f"    • {len(chaves_criptografadas)} preferência(s) configurada(s)")
    print("    • Todas configuradas como \"Chave própria (GRÁTIS)\"")
    print()
    print("4️⃣  Teste no WhatsApp:")
    print()
    print("    Envie: Bom dia")
    print()
    print("    Verifique os logs do servidor:")
    for provedor in chaves_criptografadas.keys():
        print(f"    [{provedor.upper()}] ✅ Usando chave de {provedor} propria para usuário {USUARIO_ID}")
    print()
    print("=" * 80)
    print("🎉 TUDO PRONTO!")
    print("=" * 80)
    print()
    print("Você agora está usando suas próprias chaves de API (GRÁTIS)!")
    print()
    print("📚 Consulte os manuais em docs/ para mais informações:")
    for provedor in chaves_criptografadas.keys():
        manual_names = {
            'gemini': 'MANUAL_GEMINI.md',
            'weather': 'MANUAL_WEATHER.md',
            'openroute': 'MANUAL_OPENROUTE.md'
        }
        if provedor in manual_names:
            print(f"   • docs/{manual_names[provedor]}")
    print()
    print("💬 Precisa de ajuda?")
    print("   • WhatsApp: (31) 9400-1072")
    print("   • Email: suporte@meusecretario.com")
    print()

if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n")
        print("=" * 80)
        print("❌ OPERAÇÃO CANCELADA PELO USUÁRIO")
        print("=" * 80)
        print()
    except Exception as e:
        print("\n")
        print("=" * 80)
        print("❌ ERRO INESPERADO")
        print("=" * 80)
        print()
        print(f"Erro: {e}")
        print()
        print("Stack trace:")
        import traceback
        traceback.print_exc()
        print()
        print("💡 Dica: Verifique se:")
        print("   • O arquivo .env existe e tem ENCRYPTION_KEY configurada")
        print("   • Você está executando do diretório raiz do projeto")
        print("   • Todas as dependências estão instaladas (pip install -r requirements.txt)")
        print()
        sys.exit(1)
