import sys
from sqlalchemy import text
from werkzeug.security import generate_password_hash
from app import create_app, db

def configurar_senha_usuario():
    app = create_app()
    
    with app.app_context():
        # 1. Garante que a coluna password_hash existe (Migration manual)
        print("🔍 Verificando estrutura do banco de dados...")
        try:
            with db.engine.connect() as conn:
                conn.execute(text("ALTER TABLE public.Usuarios ADD COLUMN IF NOT EXISTS password_hash VARCHAR(255)"))
                conn.commit()
            print("✅ Coluna 'password_hash' verificada/criada.")
        except Exception as e:
            print(f"⚠️ Aviso ao verificar coluna (pode já existir): {e}")

        # 2. Solicita dados
        print("\n--- Definir Senha de Administrador ---")
        whatsapp = input("Digite o número do WhatsApp do usuário (apenas números): ").strip()
        nova_senha = input("Digite a nova senha para este usuário: ").strip()

        if not whatsapp or not nova_senha:
            print("❌ Erro: WhatsApp e Senha são obrigatórios.")
            return

        # 3. Busca e Atualiza
        whatsapp_limpo = "".join(filter(str.isdigit, whatsapp))
        hashed_password = generate_password_hash(nova_senha)

        try:
            # Verifica se usuário existe
            usuario = db.session.execute(
                text("SELECT id, nome FROM Usuarios WHERE numero_whatsapp = :w"), 
                {'w': whatsapp_limpo}
            ).fetchone()

            if not usuario:
                print(f"❌ Usuário com WhatsApp {whatsapp_limpo} não encontrado no banco.")
                return

            # Atualiza a senha
            db.session.execute(
                text("UPDATE Usuarios SET password_hash = :p WHERE id = :id"),
                {'p': hashed_password, 'id': usuario.id}
            )
            db.session.commit()
            
            print(f"\n🎉 SUCESSO! Senha definida para o usuário: {usuario.nome}")
            print("Agora você pode logar no Frontend com este WhatsApp e senha.")
            
        except Exception as e:
            db.session.rollback()
            print(f"❌ Erro ao atualizar banco: {e}")

if __name__ == "__main__":
    configurar_senha_usuario()