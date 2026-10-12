# Desvio 155 — Evento pendente "mais recente" depende da ordem do KEYS

- **Estado:** implementado (feature 012, 2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`EventConfirmationService.get_latest_pending_event` (`event_confirmation_service.py:68-90`) pega a última chave de `KEYS pending_event:{numero}:*`; o Redis não garante ordem. Com dois eventos pendentes o `sim` pode confirmar o errado.

## Comportamento proposto (.NET)
Gravar `criado_em` na pendência e escolher o mais novo (ou manter um ponteiro `last_pending_event:{numero}` como o de transações).

## Impacto nos clientes
O `sim` confirma o último evento criado.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar (ponteiro `last_pending_event`, mesmo padrão de `last_pending`).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
