# app/services/gemini_service.py
import json
from app import gemini_model # Importa o "Singleton" do Gemini

def get_gemini_text_response(response):
    """ 
    Helper interno para extrair 'response.text' com segurança.
    Se falhar (bloqueio), levanta uma exceção. 
    (Movido do app.py)
    """
    try:
        return response.text
    except ValueError as e:
        print(f"[GEMINI-ERRO] Resposta do Gemini bloqueada ou inválida. {e}")
        print(f"[GEMINI-ERRO] Feedback: {response.prompt_feedback}")
        raise Exception(f"Falha na API: Resposta do Gemini bloqueada. {response.prompt_feedback}")
    except Exception as e:
        print(f"[GEMINI-ERRO] Erro inesperado ao extrair texto: {e}")
        raise e

def extract_from_notification(texto_notificacao):
    """
    Usa o Gemini para extrair dados brutos de uma notificação (Automate).
    """
    if not gemini_model: raise Exception("Modelo Gemini não configurado.")
    
    prompt = f"""
    Analise a notificação: "{texto_notificacao}"
    Retorne APENAS JSON com: "valor_decimal" (sempre positivo), "descricao_bruta", "tipo_fluxo" ("Renda" ou "Despesa").
    """; 
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    
    if not response_text:
        raise Exception("Falha na extração (Automate): Resposta vazia do Gemini.")
        
    json_response_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-1] Extração: {json_response_text}")
    return json.loads(json_response_text)

def categorize_transaction(categories_json_list, transacao_descricao, tipo_transacao, id_outros_fallback):
    """
    Usa o Gemini para escolher o ID de uma categoria com base na descrição.
    """
    if not gemini_model: raise Exception("Modelo Gemini não configurado.")

    prompt = f"""
    Minhas subcategorias são: {json.dumps(categories_json_list)}
    A transação teve a descrição: "{transacao_descricao}"; O tipo é: "{tipo_transacao}"
    Qual é o "id" da subcategoria que melhor corresponde? Se for genérico, use o "id" de "Outros" (que é {id_outros_fallback}).
    Responda APENAS com o número do ID.
    """; 
    response = gemini_model.generate_content(prompt)
    
    try:
        response_text = get_gemini_text_response(response)
    except Exception as e:
        print(f"[GEMINI-CAT] ERRO: Resposta do Gemini bloqueada. Usando 'Outros'. {e}")
        return id_outros_fallback
    
    if not response_text:
        print(f"[GEMINI-CAT] ERRO: Gemini retornou uma resposta vazia. Usando 'Outros'.")
        return id_outros_fallback
    
    try:
        id_categoria_str = response_text.strip().replace("`", "")
        id_categoria_final = int(id_categoria_str)
        # Validação extra: O ID existe na lista?
        if id_categoria_final not in [cat['id'] for cat in categories_json_list]:
            print(f"[GEMINI-CAT] AVISO: Gemini retornou um ID ({id_categoria_final}) que não existe. Usando 'Outros'.")
            id_categoria_final = id_outros_fallback
        return id_categoria_final
    except ValueError:
        print(f"[GEMINI-CAT] AVISO: Gemini não retornou um número ({response_text}). Usando 'Outros'.")
        return id_outros_fallback

def get_message_intent(texto_msg):
    """
    Usa o Gemini para classificar a intenção principal da mensagem do WhatsApp.
    """
    if not gemini_model: 
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise a mensagem: "{texto_msg}"
    
    Classifique a intenção principal como:
    - "Renda"
    - "Despesa"
    - "Consulta Reserva"
    - "Consulta Período" ("perguntas como "quanto gastei ontem?", "gastos do fds")
    - "Consulta Potes"
    - "Consulta Contas Fixas" ("minhas contas fixas", "contas pendentes")
    - "Quitar Conta Fixa ("paguei a conta de água")
    - "Transferência"
    - "Pagamento Fatura"
    - "Consultar Agenda"
    - "Consulta Categoria Específica"
    
    Responda APENAS com JSON: {{"intent": "..."}}
    
    Exemplos:
    - "quanto gastei ontem?" → {{"intent": "Consulta Período"}}
    - "gastos do final de semana" → {{"intent": "Consulta Período"}}
    - "minhas contas fixas" → {{"intent": "Consulta Contas Fixas"}}
    - "tenho compromisso hoje?" → {{"intent": "Consulta Agenda"}}
    - "paguei a água no valor de 150" → {{"intent": "Quitar Conta Fixa"}}
    '''
    
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    
    if not response_text:
        raise Exception("Falha na classificação da intenção: Resposta vazia do Gemini.")
        
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    intent_data = json.loads(json_text)
    print(f"[GEMINI-INTENT] Intenção: {intent_data.get('intent')}")
    return intent_data.get('intent')

def extract_transaction_details(texto_msg, intent):
    """
    Extrai Valor e Descrição para Renda/Despesa manual.
    """
    if not gemini_model: raise Exception("Modelo Gemini não configurado.")
    
    prompt = f"""Analise a mensagem: "{texto_msg}"
    O tipo é: "{intent}".
    Extraia "valor_decimal" (sempre positivo) e "descricao_bruta".
    Responda APENAS com JSON.
    Ex: {{"valor_decimal": 50.00, "descricao_bruta": "Padaria"}}"""; 
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Extração (R/D): {json_extract_text}")
    return json.loads(json_extract_text)

def extract_transfer_details(texto_msg, contas_json_list):
    """
    Extrai Valor, Origem e Destino para Transferência.
    """
    if not gemini_model: raise Exception("Modelo Gemini não configurado.")
    
    prompt = f"""Analise a mensagem de transferência: "{texto_msg}"
    Minhas contas são: {json.dumps(contas_json_list)}
    Extraia "valor_decimal", "conta_origem", e "conta_destino". Responda APENAS com JSON."""; 
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Extração (Transf): {json_extract_text}")
    return json.loads(json_extract_text)

def extract_fatura_payment_details(texto_msg, contas_json_list):
    """
    Extrai Valor, Origem e Cartão para Pagamento de Fatura.
    """
    if not gemini_model: raise Exception("Modelo Gemini não configurado.")
    
    prompt = f"""Analise a mensagem de pagamento de fatura: "{texto_msg}"
    Minhas contas são: {json.dumps(contas_json_list)}
    Extraia "valor_decimal", "conta_origem", e "conta_cartao". Responda APENAS com JSON."""; 
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Extração (Pagto Fatura): {json_extract_text}")
    return json.loads(json_extract_text)

def extract_category_query(texto_msg):
    """
    Extrai o nome da categoria que o usuário deseja consultar.
    """
    if not gemini_model: raise Exception("Modelo Gemini não configurado.")
    
    prompt = f"""Analise a pergunta: "{texto_msg}"
    Extraia o nome da categoria ou subcategoria que o usuário quer consultar.
    Responda APENAS com JSON: {{"nome_categoria": "..."}}
    Ex1: "quanto gastei com supermercado" -> {{"nome_categoria": "Supermercado / Mercearia"}}
    Ex2: "qual foi meu gasto com lazer?" -> {{"nome_categoria": "Lazer e Entretenimento"}}"""; 
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Categoria para consulta: {json_extract_text}")
    return json.loads(json_extract_text)

def extract_period_query(texto_msg):
    '''
    Extrai o tipo de período da mensagem do usuário.
    
    Returns:
        {
            "period_type": "ontem" | "hoje" | "final_de_semana" | etc.,
            "categoria": "supermercado" (opcional)
        }
    '''
    if not gemini_model:
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise a pergunta: "{texto_msg}"
    
    Identifique o período que o usuário quer consultar:
    - "ontem" → {{"period_type": "ontem"}}
    - "hoje" → {{"period_type": "hoje"}}
    - "final de semana" / "fds" → {{"period_type": "final_de_semana"}}
    - "esta semana" → {{"period_type": "esta_semana"}}
    - "semana passada" → {{"period_type": "semana_passada"}}
    - "últimos 7 dias" → {{"period_type": "ultimos_7_dias"}}
    - "este mês" → {{"period_type": "este_mes"}}
    - "mês passado" → {{"period_type": "mes_passado"}}
    
    Se mencionar uma categoria específica, inclua também:
    {{"period_type": "...", "categoria": "supermercado"}}
    
    Responda APENAS com JSON.
    
    Exemplos:
    - "quanto gastei ontem?" → {{"period_type": "ontem"}}
    - "gastos do final de semana" → {{"period_type": "final_de_semana"}}
    - "quanto gastei com uber esta semana?" → {{"period_type": "esta_semana", "categoria": "uber"}}
    '''
    
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-PERIOD] Período extraído: {json_text}")
    return json.loads(json_text)

def extract_bill_payment(texto_msg):
    '''
    Extrai dados de um pagamento de conta fixa.
    
    Returns:
        {
            "descricao": "conta de água",
            "valor": 150.50
        }
    '''
    if not gemini_model:
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise: "{texto_msg}"
    
    O usuário pagou uma conta. Extraia:
    - "descricao": nome da conta (ex: "conta de água", "seguro carro")
    - "valor": valor pago
    
    Responda APENAS com JSON.
    
    Exemplos:
    - "paguei 150 da água" → {{"descricao": "conta de água", "valor": 150.00}}
    - "quitei o seguro do carro, 800 reais" → {{"descricao": "seguro carro", "valor": 800.00}}
    '''
    
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-BILL] Pagamento extraído: {json_text}")
    return json.loads(json_text)

def extract_calendar_query(texto_msg):
    '''
    Extrai o período da consulta de agenda.
    CORREÇÃO: Melhor detecção de "este mês", "esse mês", etc.
    
    Returns:
        {"period_type": "hoje" | "amanha" | "final_de_semana" | etc.}
    '''
    print(f"[GEMINI-CALENDAR] Extraindo período da mensagem: {texto_msg}")
    
    if not gemini_model:
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise a pergunta sobre agenda: "{texto_msg}"
    
    Identifique o período que o usuário quer consultar:
    - "hoje" → {{"period_type": "hoje"}}
    - "amanhã" → {{"period_type": "amanha"}}
    - "final de semana" / "fds" → {{"period_type": "final_de_semana"}}
    - "esta semana" / "essa semana" → {{"period_type": "esta_semana"}}
    - "próxima semana" / "semana que vem" → {{"period_type": "proxima_semana"}}
    - "este mês" / "esse mês" / "mês atual" → {{"period_type": "este_mes"}}
    - "mês passado" → {{"period_type": "mes_passado"}}
    - "próximo mês" / "mês que vem" → {{"period_type": "proximo_mes"}}
    
    ATENÇÃO:
    - "esse mês", "este mês", "neste mês", "no mês" → SEMPRE "este_mes"
    - "essa semana", "esta semana", "nesta semana" → SEMPRE "esta_semana"
    
    Responda APENAS com JSON.
    
    Exemplos CRÍTICOS:
    - "quais meus compromissos esse mês?" → {{"period_type": "este_mes"}}
    - "eventos deste mês" → {{"period_type": "este_mes"}}
    - "agenda do mês" → {{"period_type": "este_mes"}}
    - "compromissos essa semana" → {{"period_type": "esta_semana"}}
    '''
    
    response = gemini_model.generate_content(prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-CALENDAR] Período: {json_text}")
    return json.loads(json_text)