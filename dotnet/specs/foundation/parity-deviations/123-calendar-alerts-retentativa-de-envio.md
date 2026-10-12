# Desvio 123 — Alertas de calendário: falha de envio perde o aviso sem nova tentativa

- **Estado:** implementado (feature 014, 2026-10-12)
- **Features afetadas:** 014 (e 013, que ganha uma linha de log no ciclo)
- **Severidade:** correção

## Comportamento atual (Flask e .NET até o 121)
O evento só é visto por ciclos cuja janela `(agora + N − 60 s, agora + N + 30 s]` contém o início dele; para um compromisso em ponto isso é **um único ciclo**. Se o envio falha (bot fora do ar, rede), o claim é solto, mas o ciclo seguinte já não enxerga o evento e o aviso se perde. Com `minutos_antes = 1` (o padrão) não existe ciclo seguinte antes do início. Em 2026-10-12 o alerta das 19h se perdeu assim (`BOT_WHATSAPP_URL` errada, todo envio com `HttpRequestException`). O log do ciclo também não diz quantos eventos entraram na janela nem quantos envios falharam.

## Comportamento proposto (.NET)
1. **Tolerância de ciclos:** o limite inferior da janela recua 3 min: `(agora + N − 60 s − 180 s, agora + N + 30 s]`, sempre com `início > agora`. O evento passa a ser visto por até 4 ciclos seguidos (antes do início); o claim de 2 h (RN-014-13) impede repetição depois de um envio bem-sucedido, e a falha solta o claim para o ciclo seguinte tentar de novo.
2. **Nova tentativa no mesmo ciclo:** envio que devolve falso é repetido **uma vez** após 5 s, antes de soltar o claim (cobre `minutos_antes = 1`).
3. **Texto fiel ao tempo restante:** na retentativa, `Faltam {N} min` usa os minutos que de fato restam (`round((início − agora) / 1 min)`, mínimo 1, no máximo N). No ciclo normal o valor é o próprio N.
4. **Log do ciclo:** quando há eventos na janela, uma linha Info por usuário: eventos na janela, enviados, falhas (só números, nada de título, local ou telefone).

## Impacto nos clientes
Alertas que hoje se perdem por falha transitória passam a chegar, no máximo 3 min depois do horário normal. Evento criado depois do horário normal do alerta, mas até 3 min dentro da tolerância, também gera aviso (antes não gerava). Com o Redis fora (claim fail-open) um evento pode ser avisado mais de uma vez.

## Alternativas
Manter a janela e só retentar no mesmo ciclo (não cobre ciclo perdido). Ou retentativa dentro do `BotClient` (altera a regra "falha de rede nunca repete" de todos os envios, inclusive os não idempotentes).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-12 ("quero"), depois da perda do alerta das 19h.
