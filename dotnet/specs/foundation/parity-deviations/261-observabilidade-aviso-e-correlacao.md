# Desvio 261 — Aviso de falha por WhatsApp e ID de correlação nos logs

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 012, 013
- **Severidade:** operação

## Comportamento atual (Flask)
Falha de job (check-in noturno, motor de agendamentos...) só aparece no log (`base_job.py`): só se descobre olhando o WhatsApp ou o servidor. As linhas de log de uma mensagem do webhook não têm nada que as ligue.

## Comportamento proposto (.NET)
1. **Aviso operacional:** cada `Failure` do log de jobs também vira uma mensagem no WhatsApp do dono, em segundo plano (fila em memória, nunca bloqueia nem lança), no máximo **um por origem a cada 30 minutos** (os demais são contados: "+N falha(s) desde o último aviso"). Número em `OPS_ALERT_WHATSAPP` (só dígitos, com DDI); **vazio = desligado**. O texto não leva dados pessoais (as mensagens dos jobs já têm só ids e o tipo da exceção).
2. **ID de correlação:** cada requisição do webhook recebe um id de 8 caracteres hexadecimais que prefixa todas as linhas de log dela (`[3f9a1c02] ...`) e volta no cabeçalho `X-Correlation-Id`. Fora do webhook o formato das linhas não muda.

## Impacto nos clientes
Nenhuma rota muda (só um cabeçalho de resposta novo). Precisa do segredo `OPS_ALERT_WHATSAPP` no GitHub (o deploy já o repassa; sem ele o aviso fica desligado). O formato dos logs dos jobs é o mesmo.

## Alternativas
Só log (rejeitado: foi assim que falhas ficaram dias sem serem vistas). Alerta por e-mail ou ferramenta externa (mais peças para um sistema de um usuário).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 23/10/2026 ("Observabilidade … aviso para o seu próprio número … ID de correlação por mensagem nos logs"). Implementado nesta branch.
