# Desvio 287 — Evento que termina à meia-noite (fim <= início)

- **Estado:** implementado; aprovado pelo Rafael em 2026-11-23 (plano aprovado após "Formatura Isis, hoje de 20:00 às 00:00" falhar com "Erro ao criar evento.")
- **Features afetadas:** 010 (Google Calendar), 012 (WhatsApp)
- **Severidade:** correção

## Comportamento atual
Início e fim são montados na mesma data. "20:00 às 00:00" vira fim 00:00 do mesmo dia (antes do início), o Google recusa e o bot responde "Erro ao criar evento.".

## Comportamento proposto (.NET)
Na criação (`ForNew`) e na atualização (`ForUpdate`), fim explícito menor ou igual ao início passa para o dia seguinte (20:00–00:00 ⇒ até 00:00 do dia seguinte; 22:00–01:30 ⇒ 01:30 do dia seguinte). Fim maior que o início não muda.

## Impacto nos clientes
Nenhuma rota ou campo muda. Eventos que antes falhavam agora são criados.
