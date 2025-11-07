# run.py
from app import create_app

"""
Este é o ponto de entrada principal.
O Gunicorn (servidor de produção) vai apontar para este arquivo.
Ele simplesmente chama a "Fábrica" para construir e retornar a aplicação.
"""
app = create_app()

if __name__ == "__main__":
    # Isso só é usado para rodar localmente (ex: python run.py)
    app.run(debug=True)