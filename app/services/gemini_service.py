# app/services/gemini_service.py
import json
from app import gemini_model # Importa o "Singleton" do Gemini


def _obter_modelo_gemini_para_usuario(usuario_id: int = None):
    """
    Retorna modelo Gemini configurado com chave apropriada.
    Respeita escolha do usuário (chave própria ou sistema).

    Args:
        usuario_id: ID do usuário (opcional, usa chave do sistema se None)

    Returns:
        GenerativeModel configurado

    Raises:
        Exception: Se usuário não configurou preferência ou chave indisponível
    """
    from app.services.gerenciador_chaves_api import GerenciadorChavesApi
    import google.generativeai as genai

    # Se não tem usuario_id, usa modelo global
    if usuario_id is None:
        return gemini_model

    try:
        # Buscar chave (usuário ou sistema)
        chave, tipo_chave = GerenciadorChavesApi.obter_chave_api(usuario_id, 'gemini')

        # Se for chave do sistema, usar modelo global
        if tipo_chave == 'sistema':
            return gemini_model

        # Se for chave própria, criar modelo específico
        genai.configure(api_key=chave)
        return genai.GenerativeModel('gemini-2.0-flash')

    except Exception as e:
        print(f"[GEMINI] Erro ao obter chave para usuario {usuario_id}: {e}")
        raise  # Re-lançar exceção para usuário ver mensagem


def safe_generate_content(model, prompt):
    """
    Wrapper seguro para chamadas ao Gemini com tratamento de erros.

    Args:
        model: Modelo Gemini
        prompt: Prompt a ser enviado

    Returns:
        response object

    Raises:
        Exception: Com mensagens amigáveis para diferentes tipos de erro
    """
    try:
        return model.generate_content(prompt)
    except Exception as e:
        error_str = str(e)

        # Erro 429 - Quota Exceeded
        if '429' in error_str or 'quota' in error_str.lower() or 'ResourceExhausted' in error_str:
            print(f"[GEMINI-QUOTA] ❌ Limite de quota excedido: {e}")
            raise Exception("429 Quota exceeded - Gemini API rate limit reached")

        # Erro 403 - Permission Denied
        if '403' in error_str or 'permission' in error_str.lower():
            print(f"[GEMINI-PERMISSION] ❌ Erro de permissão: {e}")
            raise Exception("403 Permission denied - Invalid API key or permissions")

        # Outros erros
        print(f"[GEMINI-ERRO] ❌ Erro ao gerar conteúdo: {e}")
        raise


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

def extract_from_notification(texto_notificacao, usuario_id=None):
    """
    Usa o Gemini para extrair dados brutos de uma notificação (Automate).

    Args:
        texto_notificacao: Texto da notificação
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model: raise Exception("Modelo Gemini não configurado.")

    prompt = f"""
    Analise a notificação: "{texto_notificacao}"
    Retorne APENAS JSON com: "valor_decimal" (sempre positivo), "descricao_bruta", "tipo_fluxo" ("Renda" ou "Despesa").
    """;
    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    
    if not response_text:
        raise Exception("Falha na extração (Automate): Resposta vazia do Gemini.")
        
    json_response_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-1] Extração: {json_response_text}")
    return json.loads(json_response_text)

def categorize_transaction(categories_json_list, transacao_descricao, tipo_transacao, id_outros_fallback, usuario_id=None):
    """
    Usa o Gemini para escolher o ID de uma categoria com base na descrição.

    Args:
        categories_json_list: Lista de categorias
        transacao_descricao: Descrição da transação
        tipo_transacao: Tipo da transação
        id_outros_fallback: ID fallback
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model: raise Exception("Modelo Gemini não configurado.")

    prompt = f"""
    Minhas subcategorias são: {json.dumps(categories_json_list)}
    A transação teve a descrição: "{transacao_descricao}"; O tipo é: "{tipo_transacao}"
    Qual é o "id" da subcategoria que melhor corresponde? Se for genérico, use o "id" de "Outros" (que é {id_outros_fallback}).
    Responda APENAS com o número do ID.
    """;
    response = safe_generate_content(model, prompt)
    
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

def get_message_intent(texto_msg, usuario_id=None):
    """
    Usa o Gemini para classificar a intenção principal da mensagem do WhatsApp.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise a mensagem: "{texto_msg}"

    Classifique a intenção principal como:
    - "Renda"
    - "Despesa"
    - "Consulta Saldo" (quando o usuário quer saber quanto TEM nas contas)
    - "Listar Contas" (quando o usuário quer saber QUAIS contas tem cadastradas)
    - "Consulta Reserva" (cálculo de reserva de emergência, 6 meses)
    - "Vencimentos Hoje" (tenho conta que vence hoje, o que vence hoje, vencimentos de hoje, contas de hoje)
    - "Vencimentos Amanhã" (tenho conta que vence amanhã, o que vence amanhã, vencimentos de amanhã)
    - "Vencimentos Essa Semana" (contas que vencem essa semana, vencimentos da semana, o que vence nos próximos 7 dias)
    - "Consulta Período"
    - "Consulta Potes"
    - "Consulta Contas Fixas"
    - "Quitar Conta Fixa"
    - "Transferência"
    - "Pagamento Fatura" (quando o usuário PAGOU/vai PAGAR a fatura)
    - "Consulta Valor Fatura" (quando o usuário quer SABER o valor da fatura)
    - "Consultar Agenda"
    - "Horários Livres" (quando estou livre, melhor horário para, quando posso marcar)
    - "Criar Evento" (criar/agendar/marcar evento)
    - "Deletar Evento" (deletar/cancelar/remover evento)
    - "Configurar Notificações" (configurar/ativar/desativar notificações)
    - "Consulta Categoria Específica"
    - "Análise Inteligente" (analisar gastos, insights, relatório financeiro, análise financeira, padrões de consumo)
    - "Comparação Mensal" (comparar mês atual com anterior, evolução mensal)
    - "Previsão de Gastos" (quanto vou gastar, previsão, projeção, orçamento futuro, estimativa de gastos)
    - "Gráfico de Gastos" (gráfico, gráficos, visualizar gastos, mostrar gráfico, gerar gráfico)
    - "Solicitar API Key" (minha api key, qual minha chave, api key, chave de acesso, credenciais)
    - "Configurar Relatório Mensal" (configurar relatório mensal, ativar relatório, desativar relatório, alterar hora relatório, mudar horário relatório)
    - "Configurar Localização" (configurar localização, minha cidade, mudar cidade, definir localização, onde estou)
    - "Configurar Endereço" (configurar endereço casa/trabalho, adicionar endereço, cadastrar endereço favorito)
    - "Listar Endereços" (quais endereços tenho, meus endereços, endereços cadastrados, ver endereços)
    - "Deletar Endereço" (remover endereço, apagar endereço, deletar endereço casa/trabalho)
    - "Menu de Ajuda" (o que você pode fazer, quais funcionalidades, comandos disponíveis, o que você faz, ajuda secretário, funcionalidades do bot)

    Responda APENAS com JSON: {{"intent": "..."}}

    Exemplos:
    - "criar evento academia amanhã" → {{"intent": "Criar Evento"}}
    - "deletar reunião de hoje" → {{"intent": "Deletar Evento"}}
    - "quero receber minha agenda às 8h" → {{"intent": "Configurar Notificações"}}
    - "tenho compromisso agora à tarde?" → {{"intent": "Consultar Agenda"}}
    - "quando posso marcar dentista esta semana?" → {{"intent": "Horários Livres"}}
    - "quando estou livre amanhã?" → {{"intent": "Horários Livres"}}
    - "melhor horário para reunião hoje" → {{"intent": "Horários Livres"}}
    - "analisar meus gastos" → {{"intent": "Análise Inteligente"}}
    - "quero um relatório financeiro" → {{"intent": "Análise Inteligente"}}
    - "comparar este mês com o anterior" → {{"intent": "Comparação Mensal"}}
    - "quanto vou gastar este mês" → {{"intent": "Previsão de Gastos"}}
    - "qual a projeção de gastos" → {{"intent": "Previsão de Gastos"}}
    - "estimativa de gastos próximo mês" → {{"intent": "Previsão de Gastos"}}
    - "gráfico de gastos" → {{"intent": "Gráfico de Gastos"}}
    - "mostrar gráfico" → {{"intent": "Gráfico de Gastos"}}
    - "visualizar meus gastos" → {{"intent": "Gráfico de Gastos"}}
    - "qual minha api key" → {{"intent": "Solicitar API Key"}}
    - "me dá minha chave de acesso" → {{"intent": "Solicitar API Key"}}
    - "tenho conta que vence hoje?" → {{"intent": "Vencimentos Hoje"}}
    - "o que vence amanhã?" → {{"intent": "Vencimentos Amanhã"}}
    - "contas que vencem essa semana" → {{"intent": "Vencimentos Essa Semana"}}
    - "configurar relatório mensal" → {{"intent": "Configurar Relatório Mensal"}}
    - "quero receber relatório todo dia 1" → {{"intent": "Configurar Relatório Mensal"}}
    - "ativar relatório mensal às 10h" → {{"intent": "Configurar Relatório Mensal"}}
    - "configurar localização São Paulo, SP" → {{"intent": "Configurar Localização"}}
    - "minha cidade é Campinas" → {{"intent": "Configurar Localização"}}
    - "mudar minha localização" → {{"intent": "Configurar Localização"}}
    - "configurar endereço de casa" → {{"intent": "Configurar Endereço"}}
    - "adicionar endereço do trabalho" → {{"intent": "Configurar Endereço"}}
    - "cadastrar meu endereço" → {{"intent": "Configurar Endereço"}}
    - "quais endereços eu tenho?" → {{"intent": "Listar Endereços"}}
    - "meus endereços cadastrados" → {{"intent": "Listar Endereços"}}
    - "ver meus endereços" → {{"intent": "Listar Endereços"}}
    - "remover endereço de casa" → {{"intent": "Deletar Endereço"}}
    - "apagar endereço do trabalho" → {{"intent": "Deletar Endereço"}}
    - "deletar endereço" → {{"intent": "Deletar Endereço"}}
    - "paguei 500 da fatura do nubank" → {{"intent": "Pagamento Fatura"}}
    - "qual o valor da minha fatura?" → {{"intent": "Consulta Valor Fatura"}}
    - "quanto está a fatura do cartão?" → {{"intent": "Consulta Valor Fatura"}}
    - "quanto eu tenho na minha conta?" → {{"intent": "Consulta Saldo"}}
    - "qual meu saldo?" → {{"intent": "Consulta Saldo"}}
    - "quanto tenho no banco?" → {{"intent": "Consulta Saldo"}}
    - "quais contas eu tenho?" → {{"intent": "Listar Contas"}}
    - "minhas contas cadastradas" → {{"intent": "Listar Contas"}}
    - "mostre minhas contas" → {{"intent": "Listar Contas"}}
    - "meu secretário o que você pode fazer?" → {{"intent": "Menu de Ajuda"}}
    - "quais funcionalidades você tem?" → {{"intent": "Menu de Ajuda"}}
    - "comandos disponíveis" → {{"intent": "Menu de Ajuda"}}
    - "o que você faz?" → {{"intent": "Menu de Ajuda"}}
    - "ajuda secretário" → {{"intent": "Menu de Ajuda"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)

    if not response_text:
        raise Exception("Falha na classificação da intenção: Resposta vazia do Gemini.")

    json_text = response_text.strip().replace("```json", "").replace("```", "")
    intent_data = json.loads(json_text)
    print(f"[GEMINI-INTENT] Intenção: {intent_data.get('intent')}")
    return intent_data.get('intent')

def extract_transaction_details(texto_msg, intent, usuario_id=None):
    """
    Extrai Valor e Descrição para Renda/Despesa manual.

    Args:
        texto_msg: Mensagem do usuário
        intent: Intenção detectada
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model: raise Exception("Modelo Gemini não configurado.")
    
    prompt = f"""Analise a mensagem: "{texto_msg}"
    O tipo é: "{intent}".
    Extraia "valor_decimal" (sempre positivo) e "descricao_bruta".
    Responda APENAS com JSON.
    Ex: {{"valor_decimal": 50.00, "descricao_bruta": "Padaria"}}""";
    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Extração (R/D): {json_extract_text}")
    return json.loads(json_extract_text)

def extract_transfer_details(texto_msg, contas_json_list, usuario_id=None):
    """
    Extrai Valor, Origem e Destino para Transferência.

    Args:
        texto_msg: Mensagem do usuário
        contas_json_list: Lista de contas
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model: raise Exception("Modelo Gemini não configurado.")

    prompt = f"""Analise a mensagem de transferência: "{texto_msg}"
    Minhas contas são: {json.dumps(contas_json_list)}
    Extraia "valor_decimal", "conta_origem", e "conta_destino". Responda APENAS com JSON.""";
    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Extração (Transf): {json_extract_text}")
    return json.loads(json_extract_text)

def extract_fatura_payment_details(texto_msg, contas_json_list, usuario_id=None):
    """
    Extrai Valor, Origem e Cartão para Pagamento de Fatura.

    Args:
        texto_msg: Mensagem do usuário
        contas_json_list: Lista de contas
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model: raise Exception("Modelo Gemini não configurado.")

    prompt = f"""Analise a mensagem de pagamento de fatura: "{texto_msg}"
    Minhas contas são: {json.dumps(contas_json_list)}
    Extraia "valor_decimal", "conta_origem", e "conta_cartao". Responda APENAS com JSON.""";
    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Extração (Pagto Fatura): {json_extract_text}")
    return json.loads(json_extract_text)

def extract_category_query(texto_msg, usuario_id=None):
    """
    Extrai o nome da categoria que o usuário deseja consultar.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model: raise Exception("Modelo Gemini não configurado.")

    prompt = f"""Analise a pergunta: "{texto_msg}"
    Extraia o nome da categoria ou subcategoria que o usuário quer consultar.
    Responda APENAS com JSON: {{"nome_categoria": "..."}}
    Ex1: "quanto gastei com supermercado" -> {{"nome_categoria": "Supermercado / Mercearia"}}
    Ex2: "qual foi meu gasto com lazer?" -> {{"nome_categoria": "Lazer e Entretenimento"}}""";
    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_extract_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EXTRACT] Categoria para consulta: {json_extract_text}")
    return json.loads(json_extract_text)

def extract_period_query(texto_msg, usuario_id=None):
    '''
    Extrai o tipo de período da mensagem do usuário.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "period_type": "ontem" | "hoje" | "final_de_semana" | etc.,
            "categoria": "supermercado" (opcional)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
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

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-PERIOD] Período extraído: {json_text}")
    return json.loads(json_text)

def extract_chart_type(texto_msg, usuario_id=None):
    '''
    Extrai o tipo de gráfico e período desejado pelo usuário.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "tipo_grafico": "pizza" | "barras" | "linha",
            "periodo_dias": int (opcional, para gráfico de pizza),
            "num_meses": int (opcional, para gráficos de barras e linha)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem: "{texto_msg}"

    Identifique o tipo de gráfico desejado e período:

    Tipos de gráfico:
    - "pizza" → Gastos por categoria (padrão: últimos 30 dias)
    - "barras" → Evolução mensal de despesas vs rendas (padrão: 6 meses)
    - "linha" → Saldo ao longo do tempo (padrão: 6 meses)

    Se não especificar o tipo, use "pizza" como padrão.

    Responda APENAS com JSON:
    - Para gráfico de pizza: {{"tipo_grafico": "pizza", "periodo_dias": 30}}
    - Para gráfico de barras: {{"tipo_grafico": "barras", "num_meses": 6}}
    - Para gráfico de linha: {{"tipo_grafico": "linha", "num_meses": 6}}

    Exemplos:
    - "gráfico de gastos" → {{"tipo_grafico": "pizza", "periodo_dias": 30}}
    - "gráfico de pizza" → {{"tipo_grafico": "pizza", "periodo_dias": 30}}
    - "mostrar gráfico de gastos por categoria" → {{"tipo_grafico": "pizza", "periodo_dias": 30}}
    - "gráfico de evolução mensal" → {{"tipo_grafico": "barras", "num_meses": 6}}
    - "gráfico de barras dos últimos 3 meses" → {{"tipo_grafico": "barras", "num_meses": 3}}
    - "gráfico de saldo" → {{"tipo_grafico": "linha", "num_meses": 6}}
    - "evolução do saldo" → {{"tipo_grafico": "linha", "num_meses": 6}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-CHART] Tipo de gráfico extraído: {json_text}")
    return json.loads(json_text)

def extract_bill_payment(texto_msg, usuario_id=None):
    '''
    Extrai dados de um pagamento de conta fixa.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "descricao": "conta de água",
            "valor": 150.50
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
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

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-BILL] Pagamento extraído: {json_text}")
    return json.loads(json_text)

def extract_payment_items(texto_msg, usuario_id=None):
    '''
    Extrai itens de pagamento de uma mensagem (sem classificar intent).

    Esta função é NEUTRA - não tenta adivinhar se é conta fixa ou despesa.
    Apenas extrai O QUE foi pago e por QUANTO.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "itens": ["baba", "leite", "internet"],
            "valor_total": 300.00 ou null,
            "trigger_word": "paguei" ou "gastei" (para contexto)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem: "{texto_msg}"

    Extraia APENAS os itens mencionados que o usuário pagou/gastou.

    IMPORTANTE:
    - "itens": lista de TUDO que foi pago (ex: ["baba", "leite", "padaria"])
    - "valor_total": valor total mencionado (ou null se não mencionar)
    - "trigger_word": verbo usado ("paguei", "gastei", "quitei", "comprei", etc)
    - Normalizar nomes: "conta de água" → "água", "conta de luz" → "luz"
    - Conectores "e", "e o", "e a", "," → são múltiplos itens

    NÃO tente classificar se é conta fixa ou despesa. Apenas extraia os dados.

    Responda APENAS com JSON.

    Exemplos:
    - "paguei a baba" →
      {{"itens": ["baba"], "valor_total": null, "trigger_word": "paguei"}}

    - "paguei a baba e o leite" →
      {{"itens": ["baba", "leite"], "valor_total": null, "trigger_word": "paguei"}}

    - "paguei 300 da internet e da água" →
      {{"itens": ["internet", "água"], "valor_total": 300.00, "trigger_word": "paguei"}}

    - "gastei 50 na padaria" →
      {{"itens": ["padaria"], "valor_total": 50.00, "trigger_word": "gastei"}}

    - "comprei comida de 80" →
      {{"itens": ["comida"], "valor_total": 80.00, "trigger_word": "comprei"}}

    - "paguei 150 da água" →
      {{"itens": ["água"], "valor_total": 150.00, "trigger_word": "paguei"}}

    - "quitei internet, telefone e seguro" →
      {{"itens": ["internet", "telefone", "seguro"], "valor_total": null, "trigger_word": "quitei"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-PAYMENT-ITEMS] Extração: {json_text}")
    return json.loads(json_text)

def extract_calendar_query(texto_msg, usuario_id=None):
    '''
    Extrai o período da consulta de agenda.
    CORREÇÃO: Melhor detecção de "este mês", "esse mês", etc.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {"period_type": "hoje" | "amanha" | "final_de_semana" | etc.}
    '''
    print(f"[GEMINI-CALENDAR] Extraindo período da mensagem: {texto_msg}")

    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
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

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-CALENDAR] Período: {json_text}")
    return json.loads(json_text)

def extract_event_creation_details(texto_msg, usuario_id=None):
    '''
    Extrai dados para criar um evento.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "titulo": "Academia",
            "data": "2025-11-20" ou "hoje" ou "amanha",
            "hora_inicio": "07:00" ou null,
            "hora_fim": "08:30" ou null,
            "descricao": "..." ou null,
            "localizacao": "..." ou null
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise a mensagem de criação de evento: "{texto_msg}"
    
    Extraia os seguintes dados:
    - titulo: Título/nome do evento (obrigatório)
    - data: Data no formato YYYY-MM-DD, ou "hoje", ou "amanha"
    - hora_inicio: Horário de início no formato HH:MM (ou null para dia inteiro)
    - hora_fim: Horário de fim no formato HH:MM (ou null)
    - descricao: Descrição adicional (ou null)
    - localizacao: Local do evento (ou null)
    
    Responda APENAS com JSON válido.
    
    Exemplos:
    - "Criar evento Academia amanhã às 7h" → 
      {{"titulo": "Academia", "data": "amanha", "hora_inicio": "07:00", "hora_fim": null, "descricao": null, "localizacao": null}}
    
    - "Agendar reunião com João dia 25/11 às 14h na sala 3" →
      {{"titulo": "Reunião com João", "data": "2025-11-25", "hora_inicio": "14:00", "hora_fim": null, "descricao": null, "localizacao": "Sala 3"}}
    
    - "Marcar aniversário da Maria dia 15 de dezembro" →
      {{"titulo": "Aniversário da Maria", "data": "2025-12-15", "hora_inicio": null, "hora_fim": null, "descricao": null, "localizacao": null}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EVENT] Criação extraída: {json_text}")
    return json.loads(json_text)


def extract_event_deletion_query(texto_msg, usuario_id=None):
    '''
    Extrai termo de busca para deletar evento.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {"titulo_busca": "academia", "quando": "hoje" ou "amanha" ou null}
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem de exclusão de evento: "{texto_msg}"

    Extraia:
    - titulo_busca: Título/palavra-chave para buscar o evento
    - quando: "hoje", "amanha", ou null (qualquer data)

    Responda APENAS com JSON.

    Exemplos:
    - "Deletar academia de hoje" → {{"titulo_busca": "academia", "quando": "hoje"}}
    - "Cancelar reunião de amanhã" → {{"titulo_busca": "reunião", "quando": "amanha"}}
    - "Remover aniversário da Maria" → {{"titulo_busca": "aniversário maria", "quando": null}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-EVENT] Exclusão extraída: {json_text}")
    return json.loads(json_text)


def extract_time_filter_query(texto_msg, usuario_id=None):
    '''
    Extrai filtro de horário da consulta.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {"time_filter": "tarde" | "manha" | "noite" | "agora" | null}
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a pergunta sobre agenda: "{texto_msg}"

    Identifique se há filtro de horário:
    - "manhã" / "de manhã" → {{"time_filter": "manha"}}
    - "tarde" / "à tarde" / "da tarde" → {{"time_filter": "tarde"}}
    - "noite" / "à noite" / "da noite" → {{"time_filter": "noite"}}
    - "agora" / "próximas horas" / "daqui a pouco" → {{"time_filter": "agora"}}
    - Sem filtro específico → {{"time_filter": null}}

    Responda APENAS com JSON.

    Exemplos:
    - "tenho compromisso agora à tarde?" → {{"time_filter": "tarde"}}
    - "eventos de manhã" → {{"time_filter": "manha"}}
    - "o que tenho agora?" → {{"time_filter": "agora"}}
    - "meus compromissos hoje" → {{"time_filter": null}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-TIME] Filtro extraído: {json_text}")
    return json.loads(json_text)


def extract_notification_config(texto_msg, usuario_id=None):
    '''
    Extrai configuração de notificações.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "tipo": "agenda_diaria" | "contas_vencer",
            "acao": "ativar" | "desativar" | "configurar",
            "hora": "08:00" ou null,
            "dias_antes": 1 ou null (só para contas)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")
    
    prompt = f'''Analise a mensagem sobre configuração de notificações: "{texto_msg}"
    
    Extraia:
    - tipo: "agenda_diaria" (agenda do dia) ou "contas_vencer" (contas a pagar)
    - acao: "ativar", "desativar", ou "configurar" (mudar horário/dias)
    - hora: Horário no formato HH:MM (ou null)
    - dias_antes: Número de dias antes (1, 2, 3...) (ou null, só para contas)
    
    Responda APENAS com JSON.
    
    Exemplos:
    - "Quero receber minha agenda diária às 8h" →
      {{"tipo": "agenda_diaria", "acao": "configurar", "hora": "08:00", "dias_antes": null}}
    
    - "Ativar notificação de contas" →
      {{"tipo": "contas_vencer", "acao": "ativar", "hora": null, "dias_antes": null}}
    
    - "Desligar lembrete de agenda" →
      {{"tipo": "agenda_diaria", "acao": "desativar", "hora": null, "dias_antes": null}}
    
    - "Avisar 2 dias antes das contas vencerem, às 9h" →
      {{"tipo": "contas_vencer", "acao": "configurar", "hora": "09:00", "dias_antes": 2}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-NOTIF] Config extraída: {json_text}")
    return json.loads(json_text)


def extract_free_time_query(texto_msg, usuario_id=None):
    '''
    Extrai período e duração desejada para buscar horários livres.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "period_type": "hoje" | "amanha" | "esta_semana" | "proxima_semana",
            "duracao_minutos": 60 (default) ou valor especificado,
            "contexto": "dentista" ou null (opcional, para IA sugerir melhor horário)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a pergunta sobre horários livres: "{texto_msg}"

    Extraia:
    - period_type: "hoje", "amanha", "esta_semana", "proxima_semana"
    - duracao_minutos: Duração estimada da atividade em minutos (default: 60)
    - contexto: O que o usuário quer marcar (dentista, reunião, etc.) ou null

    Responda APENAS com JSON.

    Exemplos:
    - "quando estou livre amanhã?" →
      {{"period_type": "amanha", "duracao_minutos": 60, "contexto": null}}

    - "quando posso marcar dentista esta semana?" →
      {{"period_type": "esta_semana", "duracao_minutos": 60, "contexto": "dentista"}}

    - "melhor horário para reunião de 2 horas hoje" →
      {{"period_type": "hoje", "duracao_minutos": 120, "contexto": "reunião"}}

    - "horários livres na próxima semana" →
      {{"period_type": "proxima_semana", "duracao_minutos": 60, "contexto": null}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-FREE-TIME] Query extraída: {json_text}")
    return json.loads(json_text)


def extract_fatura_query(texto_msg, contas_json_list, usuario_id=None):
    '''
    Extrai informações sobre qual fatura o usuário quer consultar.

    Args:
        texto_msg: Mensagem do usuário
        contas_json_list: Lista de contas
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "conta_cartao": "Nubank" ou null (se não especificou, retorna todas)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a pergunta sobre consulta de fatura: "{texto_msg}"

    Minhas contas são: {json.dumps(contas_json_list)}

    Extraia:
    - conta_cartao: Nome do cartão/conta que o usuário quer consultar (ou null se não especificou)

    Responda APENAS com JSON.

    Exemplos:
    - "qual o valor da minha fatura?" → {{"conta_cartao": null}}
    - "quanto está a fatura do Nubank?" → {{"conta_cartao": "Nubank"}}
    - "valor da fatura do Inter" → {{"conta_cartao": "Inter"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-FATURA-QUERY] Query extraída: {json_text}")
    return json.loads(json_text)


def extract_saldo_query(texto_msg, contas_json_list, usuario_id=None):
    '''
    Extrai informações sobre qual(is) conta(s) o usuário quer consultar o saldo.

    Args:
        texto_msg: Mensagem do usuário
        contas_json_list: Lista de contas
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "nome_conta": "Nubank" ou null (se não especificou, retorna todas)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a pergunta sobre consulta de saldo: "{texto_msg}"

    Minhas contas são: {json.dumps(contas_json_list)}

    Extraia:
    - nome_conta: Nome da conta que o usuário quer consultar (ou null se não especificou)

    Responda APENAS com JSON.

    Exemplos:
    - "quanto eu tenho na minha conta?" → {{"nome_conta": null}}
    - "qual meu saldo?" → {{"nome_conta": null}}
    - "quanto tenho no Nubank?" → {{"nome_conta": "Nubank"}}
    - "saldo da carteira" → {{"nome_conta": "Carteira"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-SALDO-QUERY] Query extraída: {json_text}")
    return json.loads(json_text)


def extract_parcelamento_info(texto_msg, usuario_id=None):
    '''
    Detecta se a compra é parcelada e extrai informações de parcelamento.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        {
            "parcelado": true/false,
            "num_parcelas": 3 ou null,
            "descricao_limpa": "suplemento alimentar" (sem info de parcelamento)
        }
    '''
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem de compra: "{texto_msg}"

    Identifique se é uma compra parcelada e extraia:
    - parcelado: true se menciona parcelamento, false caso contrário
    - num_parcelas: número de parcelas (ex: 3, 6, 12) ou null se não parcelado
    - descricao_limpa: descrição da compra SEM informações de parcelamento

    Palavras-chave de parcelamento:
    - "parcelado", "parcelada", "dividido", "dividida"
    - "3x", "6x", "12x", "em 3 vezes", "em 6 vezes"
    - "3 vezes", "três vezes", "seis vezes"

    Responda APENAS com JSON.

    Exemplos:
    - "comprei notebook de 3000 parcelado em 12x" →
      {{"parcelado": true, "num_parcelas": 12, "descricao_limpa": "notebook"}}

    - "suplemento alimentar no crédito dividido de três vezes" →
      {{"parcelado": true, "num_parcelas": 3, "descricao_limpa": "suplemento alimentar"}}

    - "comprei uma cadeira de 800" →
      {{"parcelado": false, "num_parcelas": null, "descricao_limpa": "cadeira"}}

    - "celular 2000 reais em 10x sem juros" →
      {{"parcelado": true, "num_parcelas": 10, "descricao_limpa": "celular"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-PARCELAMENTO] Info extraída: {json_text}")
    return json.loads(json_text)

def extract_monthly_report_config(texto_msg, usuario_id=None):
    """
    Extrai configurações de relatório mensal da mensagem do usuário.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        dict: {
            "acao": "ativar" | "desativar" | "configurar",
            "momento_envio": "INICIO_MES" | "FIM_MES" | null,
            "hora_envio": "HH:MM" | null
        }
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem sobre configuração de relatório mensal: "{texto_msg}"

    Extraia as seguintes informações:

    1. "acao":
       - "ativar" (se o usuário quer ATIVAR/LIGAR o relatório)
       - "desativar" (se o usuário quer DESATIVAR/DESLIGAR o relatório)
       - "configurar" (se o usuário quer CONFIGURAR/ALTERAR as preferências)
       - "consultar" (se o usuário quer CONSULTAR/VER as configurações atuais)

    2. "momento_envio":
       - "INICIO_MES" (se menciona: início do mês, dia 1, começo do mês, primeiro dia)
       - "FIM_MES" (se menciona: fim do mês, final do mês, último dia do mês)
       - null (se não especificou)

    3. "hora_envio":
       - Formato "HH:MM" em 24h (ex: "08:00", "14:30", "22:00")
       - null (se não especificou)

    Responda APENAS com JSON: {{"acao": "...", "momento_envio": "..." ou null, "hora_envio": "..." ou null}}

    Exemplos:
    - "quero receber relatório todo dia 1 às 8h" →
      {{"acao": "configurar", "momento_envio": "INICIO_MES", "hora_envio": "08:00"}}

    - "ativar relatório mensal no fim do mês às 10h" →
      {{"acao": "ativar", "momento_envio": "FIM_MES", "hora_envio": "10:00"}}

    - "desativar relatório mensal" →
      {{"acao": "desativar", "momento_envio": null, "hora_envio": null}}

    - "mudar hora do relatório para 14:00" →
      {{"acao": "configurar", "momento_envio": null, "hora_envio": "14:00"}}

    - "quero receber no último dia do mês" →
      {{"acao": "configurar", "momento_envio": "FIM_MES", "hora_envio": null}}

    - "como está configurado meu relatório?" →
      {{"acao": "consultar", "momento_envio": null, "hora_envio": null}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-RELATORIO-CONFIG] Configuração extraída: {json_text}")
    return json.loads(json_text)

def generate_daily_briefing(briefing_data, usuario_id=None):
    """
    Gera resumo matinal humanizado da agenda usando Gemini AI.

    Args:
        briefing_data: dict com:
            - eventos: lista de eventos do dia
            - clima_principal: dict com clima da cidade do usuário
            - climas_adicionais: lista de climas de outras cidades (eventos)
            - gaps: lista de intervalos livres entre eventos
            - total_eventos: int
            - eventos_remotos: int
            - eventos_presenciais: int
            - alertas_financeiros: dict com contas e faturas próximas ao vencimento
        usuario_id: ID do usuário (opcional)

    Returns:
        str: Mensagem humanizada formatada para WhatsApp
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    # Formatar eventos para o prompt
    eventos_texto = []
    for idx, evento in enumerate(briefing_data['eventos'], 1):
        titulo = evento.get('summary', 'Sem título')

        # Horário
        if evento.get('all_day'):
            horario = "Dia inteiro"
        else:
            start = evento.get('start', '')
            end = evento.get('end', '')

            try:
                from datetime import datetime
                start_dt = datetime.fromisoformat(start.replace('Z', '+00:00'))
                horario = start_dt.strftime('%H:%M')

                if end:
                    end_dt = datetime.fromisoformat(end.replace('Z', '+00:00'))
                    duracao_min = int((end_dt - start_dt).total_seconds() / 60)
                    horario += f" ({duracao_min} min)"
            except:
                horario = "Horário não especificado"

        # Local
        local = evento.get('location', '')
        tipo_str = ""

        # Detectar se é remoto
        titulo_lower = titulo.lower()
        desc_lower = (evento.get('description') or '').lower()
        local_lower = local.lower()

        if any(kw in f"{titulo_lower} {desc_lower} {local_lower}" for kw in ['meet', 'zoom', 'teams', 'online', 'remoto']):
            tipo_str = " [remoto]"
        elif local:
            tipo_str = f" em {local}"

        eventos_texto.append(f"{idx}. {horario} - {titulo}{tipo_str}")

    eventos_str = "\n".join(eventos_texto)

    # Formatar clima
    clima_texto = ""
    if briefing_data.get('clima_principal'):
        clima = briefing_data['clima_principal']
        clima_texto = f"Clima: {clima['descricao_completa']}"

        # Adicionar chance de chuva se alta
        chance_chuva = clima.get('chance_chuva') or 0
        if chance_chuva >= 30:
            clima_texto += f" - Chance de chuva: {chance_chuva}%"

    # Climas adicionais (outras cidades)
    climas_extras = ""
    if briefing_data.get('climas_adicionais'):
        climas_list = []
        for loc in briefing_data['climas_adicionais']:
            cidade = loc['cidade']
            clima = loc['clima']
            climas_list.append(f"{cidade}: {clima['descricao_completa']}")

        if climas_list:
            climas_extras = f"\nClimas em outras cidades (você tem eventos lá):\n" + "\n".join(climas_list)

    # Intervalos livres
    gaps_texto = ""
    if briefing_data.get('gaps'):
        gaps_list = []
        for gap in briefing_data['gaps']:
            duracao_h = gap['duracao_minutos'] // 60
            duracao_m = gap['duracao_minutos'] % 60

            if duracao_h > 0:
                duracao_str = f"{duracao_h}h" + (f"{duracao_m}min" if duracao_m > 0 else "")
            else:
                duracao_str = f"{duracao_m} min"

            gaps_list.append(f"{gap['inicio']}-{gap['fim']} ({duracao_str})")

        if gaps_list:
            gaps_texto = f"\n\nHorários livres entre eventos:\n" + "\n".join(f"• {g}" for g in gaps_list)

    # Alertas Financeiros
    alertas_texto = ""
    alertas_financeiros = briefing_data.get('alertas_financeiros', {})

    contas_hoje = alertas_financeiros.get('contas_hoje', [])
    contas_amanha = alertas_financeiros.get('contas_amanha', [])
    faturas_hoje = alertas_financeiros.get('faturas_hoje', [])
    faturas_amanha = alertas_financeiros.get('faturas_amanha', [])

    tem_alertas = any([contas_hoje, contas_amanha, faturas_hoje, faturas_amanha])

    if tem_alertas:
        alertas_partes = []

        # Contas que vencem hoje
        if contas_hoje:
            alertas_partes.append("VENCE HOJE:")
            for conta in contas_hoje:
                valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                alertas_partes.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        # Faturas que vencem hoje
        if faturas_hoje:
            if not contas_hoje:
                alertas_partes.append("VENCE HOJE:")
            for fatura in faturas_hoje:
                valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
                alertas_partes.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

        # Contas que vencem amanhã
        if contas_amanha:
            if contas_hoje or faturas_hoje:
                alertas_partes.append("")
            alertas_partes.append("VENCE AMANHÃ:")
            for conta in contas_amanha:
                valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                alertas_partes.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        # Faturas que vencem amanhã
        if faturas_amanha:
            if not contas_amanha:
                if contas_hoje or faturas_hoje:
                    alertas_partes.append("")
                alertas_partes.append("VENCE AMANHÃ:")
            for fatura in faturas_amanha:
                valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
                alertas_partes.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

        alertas_texto = "\n\nALERTAS FINANCEIROS:\n" + "\n".join(alertas_partes)

    # Informações da data
    data_alvo = briefing_data.get('data', datetime.now().date())
    dias_semana = ['segunda-feira', 'terça-feira', 'quarta-feira', 'quinta-feira',
                   'sexta-feira', 'sábado', 'domingo']
    dia_semana = dias_semana[data_alvo.weekday()]
    data_formatada = data_alvo.strftime('%d/%m/%Y')

    # Montar prompt para Gemini
    prompt = f"""Você é um assistente pessoal humanizado. Gere um resumo matinal da agenda do usuário.

DATA: {data_formatada} ({dia_semana})

EVENTOS DO DIA:
{eventos_str}

{clima_texto}
{climas_extras}
{gaps_texto}
{alertas_texto}

INSTRUÇÕES:
1. Comece com uma saudação amigável (ex: "☀️ Bom dia!", "🌅 Olá!")
2. IMPORTANTE: Use o dia da semana CORRETO informado acima ({dia_semana})
3. Resuma os compromissos de forma clara e objetiva
4. Use emojis apropriados (mas sem exagerar)
5. Destaque informações importantes:
   - Se há eventos remotos vs presenciais
   - Horários livres úteis para trabalho focado ou pausa
   - Clima (especialmente se for chover ou temperatura extrema)
   - ALERTAS FINANCEIROS: contas e faturas que vencem hoje ou amanhã (use ⚠️ ou 💰)
   - Dicas úteis (ex: "saia cedo", "leve guarda-chuva", "tempo livre para almoço")
6. Seja conciso mas informativo (máximo 20 linhas)
7. Mantenha tom profissional mas amigável
8. NÃO invente informações que não estão nos dados fornecidos
9. Se houver alertas financeiros, destaque-os claramente no resumo

FORMATAÇÃO (IMPORTANTE):
- Use *texto* para negrito (WhatsApp)
- Para listas, use "•" (bullet) ou "-", NUNCA use "*" como marcador
- Organize seções com títulos em negrito
- EVITE usar dois asteriscos seguidos (** ou * *)

Gere APENAS o texto da mensagem, sem introduções ou formatação extra.
"""

    try:
        response = safe_generate_content(model, prompt)
        response_text = get_gemini_text_response(response)

        # Limpar qualquer formatação extra
        mensagem = response_text.strip()

        # Corrigir formatação problemática do Gemini
        import re
        # Padrão principal: Remover asteriscos de marcadores de lista
        # Converte "*   *texto" -> "   • texto" (bullet simples)
        mensagem = re.sub(r'^\*\s+\*', '   • ', mensagem, flags=re.MULTILINE)
        # Também trata variações como "* *texto" ou "*  *texto"
        mensagem = re.sub(r'^\s*\*\s*\*', '   • ', mensagem, flags=re.MULTILINE)

        # Remover tags HTML indesejadas que o Gemini pode adicionar
        mensagem = re.sub(r'</?blockquote>', '', mensagem, flags=re.IGNORECASE)
        mensagem = re.sub(r'</?[a-z]+>', '', mensagem, flags=re.IGNORECASE)  # Remove outras tags HTML
        mensagem = mensagem.strip()

        print(f"[GEMINI-BRIEFING] Resumo gerado com sucesso ({len(mensagem)} chars)")
        return mensagem

    except Exception as e:
        print(f"[GEMINI-BRIEFING] Erro ao gerar resumo: {e}")

        # Fallback: gerar mensagem básica sem IA
        msg = "☀️ Bom dia! Sua agenda de hoje:\n\n"
        msg += eventos_str

        if clima_texto:
            msg += f"\n\n{clima_texto}"

        if gaps_texto:
            msg += gaps_texto

        return msg

def extract_location_config(texto_msg, usuario_id=None):
    """
    Extrai configuração de localização (cidade e estado) da mensagem do usuário.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        dict: {
            "cidade": "São Paulo",
            "estado": "SP"
        }
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem sobre configuração de localização: "{texto_msg}"

    Extraia a cidade e o estado (sigla de 2 letras) que o usuário deseja configurar.

    Responda APENAS com JSON: {{"cidade": "...", "estado": "..."}}

    Exemplos:
    - "configurar localização São Paulo, SP" → {{"cidade": "São Paulo", "estado": "SP"}}
    - "minha cidade é Campinas SP" → {{"cidade": "Campinas", "estado": "SP"}}
    - "localização: Rio de Janeiro, RJ" → {{"cidade": "Rio de Janeiro", "estado": "RJ"}}
    - "moro em Belo Horizonte MG" → {{"cidade": "Belo Horizonte", "estado": "MG"}}
    - "estou em Curitiba" → {{"cidade": "Curitiba", "estado": "PR"}} (inferir estado conhecido)

    Se não conseguir extrair o estado, retorne null para estado.
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-LOCATION] Localização extraída: {json_text}")
    return json.loads(json_text)

def extract_address_config(texto_msg, usuario_id=None):
    """
    Extrai configuração de endereço favorito.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        dict: {
            "label": "casa" | "trabalho" | "outro",
            "endereco_completo": "Rua Exemplo, 123, Bairro, Cidade-SP, CEP"
        }

    Exemplos:
    - "configurar endereço casa: Av Paulista 1000, SP"
      → {"label": "casa", "endereco_completo": "Av Paulista 1000, SP"}
    - "meu trabalho fica na Rua X, 456, Centro, Campinas"
      → {"label": "trabalho", "endereco_completo": "Rua X, 456, Centro, Campinas"}
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem sobre configuração de endereço: "{texto_msg}"

    Extraia:
    - label: "casa", "trabalho" ou "outro" (qual tipo de endereço)
    - endereco_completo: O endereço completo fornecido

    Responda APENAS com JSON: {{"label": "...", "endereco_completo": "..."}}

    Regras:
    - Se mencionar "casa", "minha casa", "residência" → label: "casa"
    - Se mencionar "trabalho", "escritório", "empresa" → label: "trabalho"
    - Se não especificar ou mencionar "outro" → label: "outro"
    - Extrair todo o endereço após o label (ignorar palavras como "fica", "é", "em")

    Exemplos:
    - "configurar endereço casa: Av Paulista 1000, Consolação, São Paulo-SP" →
      {{"label": "casa", "endereco_completo": "Av Paulista 1000, Consolação, São Paulo-SP"}}

    - "meu trabalho fica na Rua X, 456, Centro, Campinas-SP" →
      {{"label": "trabalho", "endereco_completo": "Rua X, 456, Centro, Campinas-SP"}}

    - "adicionar endereço: Av Brasil 200, Rio de Janeiro-RJ" →
      {{"label": "outro", "endereco_completo": "Av Brasil 200, Rio de Janeiro-RJ"}}

    - "cadastrar casa Rua das Flores 10, Belo Horizonte-MG" →
      {{"label": "casa", "endereco_completo": "Rua das Flores 10, Belo Horizonte-MG"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-ADDRESS] Endereço extraído: {json_text}")
    return json.loads(json_text)

def validate_event_location_completeness(location_text, usuario_id=None):
    """
    Verifica se endereço do evento está completo o suficiente.

    Args:
        location_text: Texto da localização do evento
        usuario_id: ID do usuário (opcional)

    Returns:
        dict: {
            "is_complete": True/False,
            "missing_info": ["cidade", "estado"] ou [],
            "suggested_question": "Qual a cidade do evento?" ou None
        }

    Exemplos:
    - "Academia" → incomplete, missing tudo
    - "Shopping Iguatemi, Campinas" → complete
    - "Rua X" → incomplete, missing cidade
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    if not location_text or location_text.strip() == "":
        return {
            "is_complete": False,
            "missing_info": ["endereco", "cidade", "estado"],
            "suggested_question": "Qual o endereço completo do evento?"
        }

    prompt = f'''Analise o endereço de evento: "{location_text}"

    Determine se o endereço está completo o suficiente para ser geocodificado (localizado no mapa).

    Um endereço está COMPLETO se tiver:
    - Nome de estabelecimento reconhecível + cidade (ex: "Shopping Iguatemi, Campinas")
    - OU Rua/Avenida + número + cidade (ex: "Av Paulista 1000, São Paulo")
    - OU Ponto de referência famoso + cidade (ex: "Maracanã, Rio de Janeiro")

    Um endereço está INCOMPLETO se:
    - Só tem nome genérico sem cidade (ex: "Academia", "Dentista")
    - Só tem rua sem número nem cidade (ex: "Rua das Flores")
    - Só tem bairro sem cidade (ex: "Centro")

    Responda APENAS com JSON:
    {{
        "is_complete": true/false,
        "missing_info": ["cidade", "estado"] ou [],
        "suggested_question": "Qual a cidade do evento?" ou null
    }}

    Exemplos:
    - "Academia" →
      {{"is_complete": false, "missing_info": ["endereco", "cidade", "estado"],
        "suggested_question": "Qual o endereço completo da academia? (Rua, bairro e cidade)"}}

    - "Shopping Iguatemi, Campinas-SP" →
      {{"is_complete": true, "missing_info": [], "suggested_question": null}}

    - "Rua X" →
      {{"is_complete": false, "missing_info": ["numero", "cidade", "estado"],
        "suggested_question": "Qual o número e a cidade dessa rua?"}}

    - "Centro Médico, Campinas" →
      {{"is_complete": true, "missing_info": [], "suggested_question": null}}

    - "Dentista Dr. Silva" →
      {{"is_complete": false, "missing_info": ["endereco", "cidade"],
        "suggested_question": "Qual o endereço completo? (Rua e cidade)"}}
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-LOCATION-CHECK] Validação: {json_text}")
    return json.loads(json_text)

def extract_address_label_from_deletion(texto_msg, usuario_id=None):
    """
    Extrai o label do endereço que o usuário quer deletar.

    Args:
        texto_msg: Mensagem do usuário
        usuario_id: ID do usuário (opcional)

    Returns:
        dict: {
            "label": "casa" | "trabalho" | "outro"
        }
    """
    model = _obter_modelo_gemini_para_usuario(usuario_id)
    if not model:
        raise Exception("Modelo Gemini não configurado.")

    prompt = f'''Analise a mensagem sobre deletar endereço: "{texto_msg}"

    Extraia qual tipo de endereço o usuário quer deletar:
    - "casa" (se mencionar casa, residência, minha casa)
    - "trabalho" (se mencionar trabalho, escritório, empresa)
    - "outro" (se mencionar outro ou não especificar)

    Responda APENAS com JSON: {{"label": "casa" | "trabalho" | "outro"}}

    Exemplos:
    - "deletar endereço de casa" → {{"label": "casa"}}
    - "remover endereço do trabalho" → {{"label": "trabalho"}}
    - "apagar endereço outro" → {{"label": "outro"}}
    - "deletar endereço" → {{"label": "outro"}} (padrão se não especificar)
    '''

    response = safe_generate_content(model, prompt)
    response_text = get_gemini_text_response(response)
    json_text = response_text.strip().replace("```json", "").replace("```", "")
    print(f"[GEMINI-DELETE-ADDRESS] Label extraído: {json_text}")
    return json.loads(json_text)