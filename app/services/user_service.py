# app/services/user_service.py
"""
Novo serviço para gerenciar usuários e cadastro
"""
import secrets
from sqlalchemy import text
from app import db_engine

class UserRegistrationState:
    """Gerencia o estado do cadastro de novos usuários"""
    # Dicionário em memória para estados temporários (em produção, use Redis)
    pending_registrations = {}

def check_user_exists(numero_whatsapp):
    """Verifica se um usuário já existe"""
    if not db_engine:
        raise Exception("Banco não configurado")
    
    sql = text("SELECT id, nome FROM Usuarios WHERE numero_whatsapp = :num")
    with db_engine.connect() as conn:
        result = conn.execute(sql, {"num": numero_whatsapp}).fetchone()
        return result if result else None

def start_registration(numero_whatsapp):
    """Inicia o processo de cadastro"""
    UserRegistrationState.pending_registrations[numero_whatsapp] = {
        'step': 'awaiting_name',
        'data': {}
    }

def get_registration_state(numero_whatsapp):
    """Retorna o estado atual do cadastro"""
    return UserRegistrationState.pending_registrations.get(numero_whatsapp)

def process_registration_step(numero_whatsapp, user_message):
    """
    Processa cada etapa do cadastro.
    Retorna: (mensagem_resposta, cadastro_completo)
    """
    state = get_registration_state(numero_whatsapp)
    
    if not state:
        return None, False
    
    current_step = state['step']
    
    # Etapa 1: Nome
    if current_step == 'awaiting_name':
        state['data']['nome'] = user_message.strip()
        state['step'] = 'awaiting_dia_vencimento'
        
        return (
            f"Prazer em te conhecer, {user_message}! 😊\n\n"
            f"Agora me diga: *qual é o dia de vencimento do seu cartão de crédito principal?*\n"
            f"(Digite apenas o número, ex: 10, 15, 20, etc.)"
        ), False
    
    # Etapa 2: Dia de Vencimento
    elif current_step == 'awaiting_dia_vencimento':
        try:
            dia_venc = int(user_message.strip())
            if dia_venc < 1 or dia_venc > 31:
                return "Por favor, digite um dia válido entre 1 e 31.", False
            
            state['data']['dia_vencimento'] = dia_venc
            state['step'] = 'awaiting_dia_fechamento'
            
            return (
                f"Ótimo! Vencimento no dia {dia_venc}. ✅\n\n"
                f"Agora me diga: *qual é o dia de fechamento da fatura?*\n"
                f"(Digite apenas o número, ex: 5, 13, 25, etc.)"
            ), False
        except ValueError:
            return "Ops! Digite apenas o número do dia (ex: 10, 15, 20).", False
    
    # Etapa 3: Dia de Fechamento
    elif current_step == 'awaiting_dia_fechamento':
        try:
            dia_fech = int(user_message.strip())
            if dia_fech < 1 or dia_fech > 31:
                return "Por favor, digite um dia válido entre 1 e 31.", False
            
            state['data']['dia_fechamento'] = dia_fech
            
            # Finalizar cadastro
            user_id = complete_registration(
                numero_whatsapp,
                state['data']['nome'],
                state['data']['dia_vencimento'],
                state['data']['dia_fechamento']
            )
            
            # Limpar estado
            del UserRegistrationState.pending_registrations[numero_whatsapp]
            
            return (
                f"🎉 *Cadastro Concluído com Sucesso!* 🎉\n\n"
                f"Bem-vindo ao seu Assistente Financeiro Pessoal!\n\n"
                f"📊 *O que eu posso fazer por você:*\n"
                f"• Registrar gastos e receitas automaticamente\n"
                f"• Categorizar suas despesas com IA\n"
                f"• Acompanhar seus 'potes' de gastos\n"
                f"• Calcular sua reserva de emergência\n"
                f"• Enviar lembretes de contas\n\n"
                f"💡 *Exemplos de comandos:*\n"
                f"• \"Gastei 50 no mercado\"\n"
                f"• \"Recebi 1000 de freela\"\n"
                f"• \"Como estão meus potes?\"\n"
                f"• \"Qual minha reserva?\"\n\n"
                f"Vamos começar! Como posso te ajudar hoje? 😊"
            ), True
            
        except ValueError:
            return "Ops! Digite apenas o número do dia (ex: 5, 13, 25).", False
    
    return "Erro no processo de cadastro. Tente novamente.", False

def complete_registration(numero_whatsapp, nome, dia_venc, dia_fech):
    """Salva o novo usuário no banco com API key criptografada"""
    if not db_engine:
        raise Exception("Banco não configurado")

    from app.services.encryption_service import encryption_service

    # Gerar nova API key
    nova_api_key = secrets.token_urlsafe(32)  # Chave mais forte

    # Criptografar antes de salvar
    try:
        api_key_encrypted = encryption_service.encrypt(nova_api_key)
    except Exception as e:
        print(f"[USER-SERVICE] ⚠️  Erro ao criptografar API key, salvando em plain text: {e}")
        api_key_encrypted = nova_api_key  # Fallback

    sql_user = text("""
        INSERT INTO Usuarios (nome, numero_whatsapp, api_key_automate)
        VALUES (:nome, :num_wpp, :api_key)
        RETURNING id;
    """)
    
    sql_contas = text("""
        INSERT INTO Contas (usuario_id, nome_conta, tipo_conta, dia_vencimento, dia_fechamento) VALUES 
            (:uid, 'Banco Inter', 'Conta Corrente', NULL, NULL),         
            (:uid, 'Cartão Inter', 'Cartão de Crédito', :dia_venc, :dia_fech),
            (:uid, 'Nubank', 'Conta Corrente', NULL, NULL),             
            (:uid, 'Carteira', 'Dinheiro', NULL, NULL);
    """)
    
    try:
        with db_engine.connect() as conn:
            conn.begin()
            
            # Inserir usuário
            result = conn.execute(sql_user, {
                "nome": nome,
                "num_wpp": numero_whatsapp,
                "api_key": api_key_encrypted  # Salvar versão criptografada
            })
            user_id = result.scalar_one()
            
            # Criar contas padrão
            conn.execute(sql_contas, {
                "uid": user_id,
                "dia_venc": dia_venc,
                "dia_fech": dia_fech
            })
            
            conn.commit()
            
            print(f"[USER-SERVICE] Novo usuário cadastrado: {nome} (ID: {user_id})")
            return user_id
            
    except Exception as e:
        print(f"[USER-SERVICE] Erro ao cadastrar usuário: {e}")
        try:
            with db_engine.connect() as conn:
                conn.rollback()
        except:
            pass
        raise e

def cancel_registration(numero_whatsapp):
    """Cancela o cadastro em andamento"""
    if numero_whatsapp in UserRegistrationState.pending_registrations:
        del UserRegistrationState.pending_registrations[numero_whatsapp]
        return True
    return False