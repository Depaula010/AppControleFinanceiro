# Desvio 191 — Configurar Notificações (Gemini, contas_vencer) chama método que não existe

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
Complementa o desvio 152. No ramo `contas_vencer`, `update_contas_vencer_config` chama `NotificationConfigService.update_alertas_financeiros_ativos`, que **não existe** (só `update_alertas_financeiros_config`): `AttributeError` ⇒ 500 `Erro ao processar.` **sem gravar nada** (o 152 descreve uma gravação que só acontece em `agenda_diaria`). Confirmado pelo harness (`gemini_contas_ativar_quebra_sem_gravar`).

## Comportamento proposto (.NET)
Chamar `update_alertas_financeiros_config(ativo=...)` e responder o status com as chaves que existem (`alertas_financeiros_ativos`). `dias_antes` e `hora` seguem ignorados (os alertas rodam com o resumo matinal).

## Impacto nos clientes
O usuário passa a conseguir ligar e desligar os alertas financeiros por WhatsApp.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar junto com o 152.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
