# Meu Secretário

![Logo do Meu Secretário](https://i.imgur.com/8pA0S6C.png)

## Sua Vida Financeira e Agenda, Simplificadas por IA

---

### Visão Geral (Para Usuários)

**Qual problema resolvemos?**

Você já se esqueceu de pagar uma conta ou perdeu o controle dos seus gastos mensais? O "Meu Secretário" nasceu para resolver exatamente isso. Nossa missão é simplificar sua vida financeira, automatizando o registro de despesas e garantindo que você nunca mais perca um compromisso importante.

**Como funciona?**

A mágica acontece de forma simples e direta, diretamente no seu WhatsApp:

1.  **Registro Rápido:** Basta enviar uma mensagem para o nosso bot no WhatsApp para registrar um novo gasto.
2.  **Lembretes Inteligentes:** O sistema te avisa proativamente sobre contas a pagar e eventos importantes do seu calendário.
3.  **Automação Pessoal:** Para os usuários mais avançados, oferecemos endpoints de API que permitem criar automações personalizadas com ferramentas como o "Atalhos" da Apple e outras soluções para Android.

**Funcionalidades Atuais**

*   ✅ **Registro de Transações via WhatsApp:** Adicione novas despesas de forma rápida e conversacional.
*   ✅ **Chatbot Inteligente:** Uma interface amigável no WhatsApp (construída com Baileys) para interagir com o sistema.
*   ✅ **Endpoints para Automação:** Crie seus próprios fluxos de trabalho para registrar pagamentos e outras ações financeiras.

**Funcionalidades Futuras**

Estamos sempre trabalhando para tornar o "Meu Secretário" ainda mais poderoso. Aqui está o que vem por aí:

*   ⏳ **Frontend Dedicado:** Uma interface web completa para visualizar e gerenciar suas finanças.
*   ⏳ **Relatórios Detalhados:** Gere relatórios em PDF e PNG para analisar seus gastos, metas e evolução financeira.
*   ⏳ **Metas e Alertas de Gastos:** Crie metas de gastos personalizadas ("potes") e receba alertas quando estiver se aproximando dos seus limites.

---

### Documentação Técnica (Para Desenvolvedores)

**Stack Tecnológica**

*   **Backend:** Python com Flask
*   **Banco de Dados:** SQLAlchemy (compatível com PostgreSQL)
*   **IA & NLP:** Google Gemini
*   **Cache & Mensageria:** Redis
*   **Servidor:** Gunicorn

**Pré-requisitos**

*   Python 3.8+
*   Pip (gerenciador de pacotes)
*   Uma instância de PostgreSQL
*   Uma instância de Redis
*   Uma chave de API para o Google Gemini

**Como Rodar Localmente**

1.  **Clone o Repositório:**
    ```bash
    git clone https://github.com/SEU_USUARIO/SEU_REPOSITORIO.git
    cd SEU_REPOSITORIO
    ```

2.  **Crie e Ative um Ambiente Virtual:**
    ```bash
    python -m venv venv
    source venv/bin/activate  # No Windows, use `venv\Scripts\activate`
    ```

3.  **Instale as Dependências:**
    ```bash
    pip install -r requirements.txt
    ```

4.  **Configure as Variáveis de Ambiente:**
    Crie um arquivo `.env` na raiz do projeto e adicione as seguintes variáveis:
    ```
    GEMINI_API_KEY="SUA_CHAVE_DO_GEMINI"
    DATABASE_URL="postgresql://USUARIO:SENHA@HOST:PORTA/NOME_DO_BANCO"
    REDIS_URL="redis://HOST:PORTA"
    ```

5.  **Execute a Aplicação:**
    ```bash
    gunicorn app:create_app()
    ```
    Para desenvolvimento, você pode usar:
    ```bash
    python run.py
    ```

**Estrutura do Projeto**

O projeto segue uma estrutura modular para facilitar a manutenção e o desenvolvimento:

```
├── app/                  # Contém o núcleo da aplicação
│   ├── routes/           # Blueprints do Flask (Controllers)
│   ├── services/         # Lógica de negócio e integrações
│   ├── __init__.py       # Fábrica da aplicação Flask (criação do app)
│   └── config.py         # Configurações e chaves de API
├── requirements.txt      # Dependências do Python
└── run.py                # Ponto de entrada para a aplicação
```
