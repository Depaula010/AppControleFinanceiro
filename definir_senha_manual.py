import sys
from sqlalchemy import text
from werkzeug.security import generate_password_hash
import app as app_module # Importa o módulo, não o objeto direto

def configurar_senha_usuario():
    # Cria a app para inicializar as configs e o db_engine
    flask_app = app_module.create_app()

    with flask_app.app_context():
        # Acessa o db_engine do módulo após a inicialização
        engine = app_module.db_engine

        if not engine:
            print("❌ Erro: Banco de dados não inicializado.")
            return

        print("🔍 Conectando ao banco de dados...")
        try:
            with engine.connect() as conn:
                # 1. Garante coluna senha
                try:
                    conn.execute(text("ALTER TABLE Usuarios ADD COLUMN IF NOT EXISTS senha_hash VARCHAR(255)"))
                    conn.commit()
                except Exception:
                    conn.rollback()

                # 2. Inputs
                whatsapp = input("WhatsApp (apenas números): ").strip()
                nova_senha = input("Nova Senha: ").strip()

                if not whatsapp or not nova_senha:
                    return

                # 3. Atualiza
                wpp_limpo = "".join(filter(str.isdigit, whatsapp))
                senha_hash = generate_password_hash(nova_senha)

                res = conn.execute(
                    text("UPDATE Usuarios SET senha_hash = :p WHERE numero_whatsapp = :w"),
                    {'p': senha_hash, 'w': wpp_limpo}
                )
                conn.commit()

                if res.rowcount > 0:
                    print("🎉 Senha definida com sucesso!")
                else:
                    print("❌ Usuário não encontrado.")

        except Exception as e:
            print(f"❌ Erro: {e}")

if __name__ == "__main__":
    configurar_senha_usuario()