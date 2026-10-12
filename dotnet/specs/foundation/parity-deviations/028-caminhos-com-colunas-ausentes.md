# Desvio 028 — Outros caminhos que usam colunas ou tabelas inexistentes

- **Estado:** aprovado
- **Features afetadas:** 007, 008, 009, 013
- **Severidade:** correção

## Comportamento atual (Flask)
Confirmado em produção que não existem: `notificationconfigs.agenda_diaria_ativa/agenda_diaria_hora` (removidas pela rota de cleanup; ainda usadas por `notification_processor_service.py:53` ⇒ `/admin/trigger-agenda-notifications` e `test-notification tipo=agenda` quebram), `usuarios.whatsapp_numero` (`gerenciador_notificacoes.py:52`, classe sem chamadores), `planos.limite_mensal_*` (`gerenciador_notificacoes.py:487`), `consentimentousuario.versao_termos/ip_endereco/user_agent` (o banco tem `versao_consentimento` e `ip_consentimento`; `servico_consentimento_lgpd.py:71`), `logacessochaveapi.data_acesso` (o banco tem `timestamp_acesso`). A tabela `consentimentousuario` tem 0 linhas e `logacessochaveapi` 0: o fluxo LGPD e o log de acesso provavelmente nunca funcionaram.

## Comportamento proposto (.NET)
Cada caminho é mapeado nas colunas reais (`versao_consentimento`, `ip_consentimento`, `timestamp_acesso`). O que usa colunas removidas de propósito (`agenda_diaria_*`, `gerenciador_notificacoes`) não é migrado: as rotas respondem 200 com envelope B e mensagem de que a função foi substituída pelo resumo matinal.

## Impacto nos clientes
LGPD e log de acesso passam a funcionar. `/admin/trigger-agenda-notifications` deixa de falhar.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar. Confirmar se alguém usa as rotas de agenda diária.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).

## Implementação
009c (2026-10-07): `POST /admin/trigger-agenda-notifications` responde 200 envelope B com o aviso de substituição e contadores zerados, sem I/O. `test-notification tipo=agenda` não usa `agenda_diaria_*` no código lido e segue funcionando (Q-009-9 pendente de teste em homol). As demais partes do desvio (LGPD, log de acesso) são de outras features.
