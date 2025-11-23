#!/bin/bash
# Script para gerar certificado SSL auto-assinado
# Para uso em desenvolvimento e testes com IP direto (sem domínio)

echo "🔐 Gerando certificado SSL auto-assinado..."

# Criar diretório se não existir
mkdir -p ssl

# Gerar chave privada e certificado (válido por 365 dias)
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout ssl/selfsigned.key \
  -out ssl/selfsigned.crt \
  -subj "/C=BR/ST=State/L=City/O=Organization/OU=IT/CN=212.47.65.37" \
  -addext "subjectAltName=IP:212.47.65.37"

# Definir permissões corretas
chmod 600 ssl/selfsigned.key
chmod 644 ssl/selfsigned.crt

echo "✅ Certificados SSL gerados com sucesso!"
echo "📁 Arquivos criados:"
echo "   - ssl/selfsigned.key (chave privada)"
echo "   - ssl/selfsigned.crt (certificado público)"
echo ""
echo "⚠️  IMPORTANTE: Este é um certificado auto-assinado."
echo "    Os navegadores mostrarão aviso de segurança."
echo "    Para produção SaaS, use Let's Encrypt com domínio real."
