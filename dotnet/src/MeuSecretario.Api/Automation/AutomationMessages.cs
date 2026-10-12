using System.Globalization;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Api.Automation;

/// <summary>
/// Textos exatos da spec 011 (copiados do Flask, com e sem acento como lá). Os 500 são fixos (desvio 008): o <c>str(e)</c>
/// do Flask nunca é reproduzido.
/// </summary>
public static class AutomationMessages
{
    public const string NotConfigured = "Servico nao configurado";
    public const string MissingData = "Dados faltando";
    public const string InvalidApiKey = "API key invalida";
    public const string InvalidApiKeyAccented = "API key inválida";
    public const string InvalidJson = "JSON invalido ou ausente";
    public const string DatabaseUnavailable = "Banco de dados temporariamente indisponivel";
    public const string Unauthorized = "Não autorizado";
    public const string ToggleMissingFields = "Campos 'incluir' e 'api_key' são obrigatórios";
    public const string ScheduleNotFound = "Agendamento não encontrado ou inativo";
    public const string ListMissingKey = "Parâmetro 'api_key' é obrigatório";

    public const string AutomateSaved = "Aguardando confirmacao do usuario";
    public const string ApiPending = "Transacao pendente de confirmacao no WhatsApp";
    public const string ApiSavedWithoutRedis = "Transacao salva com sucesso (Redis indisponivel)";
    public const string ApiSavedAfterRedisError = "Transacao salva (erro no Redis)";
    public const string SmsSettled = "Conta fixa quitada com sucesso!";
    public const string SmsRegistered = "Despesa registrada";

    public const string AutomateFailed = "Erro ao processar a notificação. Tente novamente.";
    public const string ApiTransactionFailed = "Erro ao registrar a transação. Tente novamente.";
    public const string SmsFailed = "Erro ao processar o pagamento. Tente novamente.";
    public const string ToggleFailed = "Erro ao atualizar a reserva. Tente novamente.";
    public const string ListFailed = "Erro ao listar a reserva. Tente novamente.";
    public const string ReprocessFailed = "Erro ao reprocessar a fatura. Tente novamente.";

    public static string MissingFields(IEnumerable<string> fields) => "Campos obrigatorios faltando: " + string.Join(", ", fields);

    public static string InvalidValue(string reason) => InputMessages.InvalidValue(reason);

    public static string InvalidPaymentType() => "tipo_pagamento invalido. Use: " + PaymentType.ValidList;

    public static string AccountNotFound(string name) => $"Conta '{name}' nao encontrada. Verifique o nome exato.";

    public static string ToggleSaved(string description, bool include) =>
        $"Agendamento '{description}' {(include ? "incluído" : "excluído")} do cálculo de reserva";

    public static string InvoiceNotFound(int id) => string.Create(CultureInfo.InvariantCulture, $"Fatura ID {id} não encontrada");
}
