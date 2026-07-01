using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Workers;


// ─────────────────────────────────────────────────────────────────────────────
// AuditWorker
// ----------------------------------------------------------------------------
// Background Service that listens for AppointmentConfirmed events from
// Azure Service Bus.
//
// Business Purpose
// ----------------
// Every appointment confirmation must generate an immutable audit record.
// This supports compliance requirements (such as HIPAA) by ensuring that
// every important business action is permanently recorded.
//
// This table is INSERT ONLY.
// Audit records should never be updated or deleted.
// ─────────────────────────────────────────────────────────────────────────────
public class AuditWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuditWorker> _logger;


    public AuditWorker(
        IConfiguration configuration,
        ILogger<AuditWorker> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serviceBusConnectionString =
            _configuration["ServiceBus:ConnectionString"]
            ?? throw new InvalidOperationException("Service Bus connection string not configured.");


        var topicName =
            _configuration["ServiceBus:TopicName"]
            ?? throw new InvalidOperationException("Service Bus topic not configured.");


        var subscriptionName =
            _configuration["ServiceBus:Subscriptions:Audit"]
            ?? throw new InvalidOperationException("Audit subscription not configured.");


        var sqlConnectionString =
            _configuration.GetConnectionString("CareBridgeDB")
            ?? throw new InvalidOperationException("CareBridgeDB connection string not configured.");


        await using var serviceBusClient = new ServiceBusClient(serviceBusConnectionString);


        await using var processor = serviceBusClient.CreateProcessor(
            topicName,
            subscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 1,
                AutoCompleteMessages = false
            });


        // ---------------------------------------------------------------------
        // Process AppointmentConfirmed messages.
        // ---------------------------------------------------------------------
        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                _logger.LogInformation(
                    "[Audit] Processing MessageId={MessageId}",
                    args.Message.MessageId);


                var appointment = JsonSerializer.Deserialize<AppointmentEvent>(
       args.Message.Body.ToString(),
       new JsonSerializerOptions
       {
           PropertyNameCaseInsensitive = true
       });


                if (appointment == null)
                {
                    throw new InvalidOperationException("Unable to deserialize AppointmentEvent.");
                }


                if (appointment is null)
                {
                    throw new InvalidOperationException(
                        "Unable to deserialize AppointmentEvent.");
                }


                using var connection = new SqlConnection(sqlConnectionString);


                // INSERT ONLY.
                // Audit records are immutable and should never be modified.
                const string sql = @"
INSERT INTO AppointmentAudit
(
    AppointmentId,
    PatientId,
    ProviderId,
    Action,
    PerformedBy,
    Timestamp,
    SourceApp
)
VALUES
(
    @AppointmentId,
    @PatientId,
    @ProviderId,
    'Appointment Confirmed',
    @ConfirmedBy,
    @ConfirmedAt,
    'CareBridge.AppointmentAPI'
);";


                await connection.ExecuteAsync(sql, new
                {
                    appointment.AppointmentId,
                    appointment.PatientId,
                    appointment.ProviderId,
                    appointment.ConfirmedBy,
                    appointment.ConfirmedAt
                });


                _logger.LogInformation(
                    "[Audit] Audit record written successfully for AppointmentId={AppointmentId}",
                    appointment.AppointmentId);


                // Remove the message from the subscription.
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Audit] Failed to process MessageId={MessageId}",
                    args.Message.MessageId);


                // Return the message to Azure Service Bus for retry.
                await args.AbandonMessageAsync(args.Message);
            }
        };


        // ---------------------------------------------------------------------
        // Handle Service Bus processing errors.
        // ---------------------------------------------------------------------
        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(
                args.Exception,
                "[Audit] Service Bus Error. Entity={EntityPath}, Source={ErrorSource}",
                args.EntityPath,
                args.ErrorSource);


            return Task.CompletedTask;
        };


        _logger.LogInformation(
            "AuditWorker started. Listening on Topic='{Topic}', Subscription='{Subscription}'.",
            topicName,
            subscriptionName);


        await processor.StartProcessingAsync(stoppingToken);


        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Expected during application shutdown.
        }


        await processor.StopProcessingAsync(stoppingToken);


        _logger.LogInformation("AuditWorker stopped.");
    }
}
