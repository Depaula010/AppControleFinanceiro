-- ============================================================================
-- 06 — CONFERÊNCIA DA ONDA 2 (somente leitura)
-- Rodar conectado em bot_whatsapp_prod. Só SELECT. No HeidiSQL: Shift+Ctrl+F9
-- (cada SELECT gera uma aba de resultado). Respondem perguntas das specs 004, 005,
-- 007 e 009 (ver dotnet/specs/foundation/decisoes-onda-2.md §0).
-- Nenhuma consulta devolve senha, hash, chave ou token.
-- ============================================================================

-- 1. (Q-004-2) Cartões de crédito: existe algum com vencimento >= fechamento (V >= F)?
SELECT id, nome_conta, dia_fechamento, dia_vencimento,
       (dia_vencimento >= dia_fechamento) AS vencimento_maior_ou_igual_ao_fechamento
FROM contas
WHERE tipo_conta = 'Cartão de Crédito'
ORDER BY id;

-- 2. (Q-004-2) Faturas existentes: mostra o ciclo real de cada cartão
SELECT conta_id, data_fechamento, data_vencimento, status
FROM faturas
ORDER BY conta_id, data_vencimento;

-- 3. (Q-005-3) Meses de reserva do usuário
SELECT id, meses_reserva_emergencia FROM usuarios ORDER BY id;

-- 4. (Q-005-3) Agendamentos PARCELADO marcados para a reserva de emergência (o desvio 014 só muda a meta neste caso)
SELECT tipo_agendamento, periodicidade, incluir_na_reserva, count(*) AS qtd
FROM agendamentos
WHERE ativo
GROUP BY tipo_agendamento, periodicidade, incluir_na_reserva
ORDER BY tipo_agendamento, periodicidade;

-- 5. (Q-005-2) Agendamentos PARCELADO: parcelas executadas x total
SELECT id, conta_id, total_parcelas, parcelas_executadas, ativo
FROM agendamentos
WHERE tipo_agendamento = 'PARCELADO'
ORDER BY id;

-- 6. (Q-009-3) Sessões do bot em baileys_auth (só contagem por session_id; o valor não é lido)
SELECT session_id, count(*) AS linhas
FROM baileys_auth
GROUP BY session_id;

-- 7. (Q-007-2) Usuários inativos
SELECT count(*) FILTER (WHERE ativo) AS ativos, count(*) FILTER (WHERE NOT ativo) AS inativos FROM usuarios;

-- 8. (P-007-4) Endereços favoritos: quantos têm coordenadas gravadas (o site as apaga ao editar)
SELECT label, (latitude IS NOT NULL AND longitude IS NOT NULL) AS tem_coordenadas, count(*) AS qtd
FROM enderecosfavoritos
GROUP BY label, 2
ORDER BY label;

-- 9. (P-007-3) E-mails: quantos usuários têm e-mail preenchido (sem mostrar o valor)
SELECT count(*) FILTER (WHERE email IS NOT NULL AND email <> '') AS com_email, count(*) AS total FROM usuarios;

-- 10. (P-007-1) Valores de alerta_potes_threshold e das horas configuradas
SELECT alerta_potes_threshold, resumo_matinal_hora, checkin_noturno_hora, count(*) AS qtd
FROM notificationconfigs
GROUP BY 1, 2, 3;

-- 11. (P-004-1) Transações de cartão: quantas despesas estão ligadas a fatura e quantas ficaram sem fatura
SELECT (c.tipo_conta = 'Cartão de Crédito') AS conta_e_cartao,
       count(*) FILTER (WHERE t.fatura_id IS NOT NULL) AS com_fatura,
       count(*) FILTER (WHERE t.fatura_id IS NULL) AS sem_fatura
FROM transacoes t
JOIN contas c ON c.id = t.conta_id
WHERE t.tipo_transacao = 'Despesa'
GROUP BY 1;

-- 12. (P-004-3) Origem das transações: as colunas observacoes/origem ainda não existem; confere a data da mais recente
SELECT min(data_transacao) AS primeira, max(data_transacao) AS ultima, count(*) AS total FROM transacoes;
