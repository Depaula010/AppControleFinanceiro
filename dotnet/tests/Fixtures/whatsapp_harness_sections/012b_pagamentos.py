# -*- coding: utf-8 -*-
"""Cenarios da 012b (pagamentos: paguei/quitei/liquidei/saldei/zerei). Infraestrutura em 012a_transacoes.py."""

BILLS = [
    {"id": 5, "descricao": "Internet", "valor_previsto": 100.0, "dia_execucao": 10, "tipo_agendamento": "FIXO", "conta_id": 1, "categoria": "Telecom"},
    {"id": 6, "descricao": "Água", "valor_previsto": 80.5, "dia_execucao": 12, "tipo_agendamento": "LEMBRETE_VARIAVEL", "conta_id": 1, "categoria": "Moradia"},
    {"id": 7, "descricao": "Seguro", "valor_previsto": None, "dia_execucao": 20, "tipo_agendamento": "LEMBRETE_VARIAVEL", "conta_id": 3, "categoria": "Seguros"},
    {"id": 8, "descricao": "Telefone", "valor_previsto": 0, "dia_execucao": 20, "tipo_agendamento": "FIXO", "conta_id": 3, "categoria": "Telecom"},
]
PAY_RET = {"FixedBillsService.settle_fixed_bill": {"__seq__": [901, 902, 903]}, "finance_service.create_transaction": {"__seq__": [801, 802, 803]}}


def pay_returns(**extra):
    r = tx_returns()
    r.update(PAY_RET)
    r.update(extra)
    return r


def pay(name, texto, items, total=None, trigger="paguei", extra_llm=None, bills=BILLS, **kw):
    llm = {"extract_payment_items": js(itens=items, valor_total=total, trigger_word=trigger), "categorize_transaction": "11"}
    llm.update(extra_llm or {})
    kw.setdefault("returns", pay_returns())
    fin(name, "012b", texto, accounts=ACC, bills=bills, llm=llm, **kw)


pay("pagto_conta_fixa_valor_informado", "paguei 130 da internet", ["internet"], 130.0)
pay("pagto_conta_fixa_sem_valor_usa_previsto", "paguei a internet", ["internet"])
pay("pagto_conta_fixa_valor_igual_sem_diferenca", "paguei 100 da internet", ["internet"], 100)
pay("pagto_conta_fixa_diferenca_menor", "paguei 70 da internet", ["internet"], 70)
pay("pagto_variavel_com_diferenca", "paguei 150 da água", ["água"], 150)
pay("pagto_variavel_sem_diferenca", "paguei 81 da água", ["água"], 81)
pay("pagto_variavel_sem_valor_previsto_pede_valor", "paguei o seguro", ["seguro"], trigger="paguei")
pay("pagto_previsto_zero_pede_valor", "quitei o telefone", ["telefone"], trigger="quitei")
pay("pagto_varias_contas", "paguei a internet e a água", ["internet", "água"])
pay("pagto_varias_contas_ignora_total", "paguei 300 da internet e da água", ["internet", "água"], 300.0)
pay("pagto_varias_com_uma_sem_valor", "paguei a internet e o seguro", ["internet", "seguro"])
pay("pagto_varias_todas_sem_valor", "paguei o seguro e o telefone", ["seguro", "telefone"])
pay("pagto_despesa_avulsa", "paguei 25 de pizza", ["pizza"], 25.0)
pay("pagto_despesa_avulsa_conta_mencionada", "paguei 25 de pizza no cartão inter", ["pizza"], 25.0,
    returns=pay_returns())
pay("pagto_despesa_avulsa_conta_padrao", "paguei 25 de pizza", ["pizza"], 25.0, default_accounts={"renda": None, "despesa": 3})
pay("pagto_despesa_avulsa_sem_valor", "gastei com pizza, zerei a conta", ["pizza"])
pay("pagto_varias_despesas_com_total", "saldei pizza e sorvete 50", ["pizza", "sorvete"], 50.0, trigger="saldei")
pay("pagto_despesa_avulsa_sem_fallback", "paguei 25 de pizza", ["pizza"], 25.0,
    returns=pay_returns(**{"finance_service.get_fallback_category_id": None}))
fin("pagto_despesa_avulsa_sem_contas", "012b", "paguei 25 de pizza", accounts=[], bills=BILLS,
    llm={"extract_payment_items": js(itens=["pizza"], valor_total=25.0, trigger_word="paguei"), "categorize_transaction": "11"}, returns=pay_returns())
pay("pagto_combinada_conta_e_despesa", "paguei a internet e 40 de pizza", ["internet", "pizza"], 40.0)
pay("pagto_conta_e_despesa_com_total_unico_item_conta", "paguei 90 da internet", ["internet"], 90)
pay("pagto_erro_em_um_item_segue", "paguei a internet e a água", ["internet", "água"],
    returns=pay_returns(**{"FixedBillsService.settle_fixed_bill": {"__seq__": [{"__raise__": "falhou"}, 902]}}))
pay("pagto_erro_no_unico_item", "paguei a internet", ["internet"],
    returns=pay_returns(**{"FixedBillsService.settle_fixed_bill": {"__raise__": "falhou"}}))
pay("pagto_erro_na_despesa_avulsa", "paguei 25 de pizza", ["pizza"], 25.0,
    returns=pay_returns(**{"finance_service.create_transaction": {"__raise__": "falhou"}}))
pay("pagto_lista_vazia", "paguei", [])
pay("pagto_itens_ausentes", "paguei", None)
pay("pagto_trigger_ausente_usa_paguei", "liquidei o seguro", ["seguro"], extra_llm={"extract_payment_items": '{"itens": ["seguro"], "valor_total": null}'})
pay("pagto_artigos_removidos", "paguei a conta de internet", ["a conta de internet"])
pay("pagto_sem_contas_fixas", "paguei 30 de lanche", ["lanche"], 30.0, bills=[])
pay("pagto_palavra_no_meio", "ontem eu paguei a internet", ["internet"])
pay("pagto_zerei", "zerei a internet", ["internet"], trigger="zerei")
pay("pagto_json_invalido", "paguei algo", [], extra_llm={"extract_payment_items": "nao json"})
pay("pagto_cota_do_gemini", "paguei algo", [], extra_llm={"extract_payment_items": {"__raise__": "429 Quota exceeded"}})
fin("pagto_tem_prioridade_sobre_confirmacao", "012b", "paguei ok", accounts=ACC, bills=BILLS,
    redis={KEY: PENDING, LAST: "ab12cd34"}, llm={"extract_payment_items": js(itens=["internet"], valor_total=None, trigger_word="paguei")},
    returns=pay_returns())
