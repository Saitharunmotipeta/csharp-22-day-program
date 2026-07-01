using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Workers;


// ─────────────────────────────────────────────────────────────────────────────
// ProviderScheduleWorker
// ----------------------------------------------------------------------------
// Background Service that listens for AppointmentConfirmed events from
// Azure Service Bus.
//
// Business Purpose
// ----------------
// Maintains a lightweight ProviderSchedule table that powers the doctor's
// scheduling dashboard. Instead of querying the Appointment table every time,
// the dashboard simply reads from this pre-built schedule.
//
// This worker demonstrates the "Competing Consumers / Event-Driven" pattern,
// where one published event is consumed independently by multiple services.
// ─────────────────────────────────────────────────────────────────────────────
public class ProviderScheduleWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProviderScheduleWorker> _logger;


    public ProviderScheduleWorker(
        IConfiguration configuration,
        ILogger<ProviderScheduleWorker> logger)
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
            _configuration["ServiceBus:Subscriptions:ProviderSchedule"]
            ?? throw new InvalidOperationException("ProviderSchedule subscription not configured.");


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
                    "[ProviderSchedule] Processing MessageId={MessageId}",
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


                // UPSERT pattern.
                // If the appointment already exists, update it.
                // Otherwise create a new schedule entry.
                const string sql = @"
IF EXISTS
(
    SELECT 1
    FROM ProviderSchedule
    WHERE AppointmentId = @AppointmentId
)
BEGIN
    UPDATE ProviderSchedule
    SET
        Status = 'Confirmed',
        AddedAt = @AddedAt
    WHERE AppointmentId = @AppointmentId;
END
ELSE
BEGIN
    INSERT INTO ProviderSchedule
    (
        ProviderId,
        AppointmentId,
        PatientName,
        AppointmentDate,
        DepartmentName,
        Status,
        AddedAt
    )
    VALUES
    (
        @ProviderId,
        @AppointmentId,
        @PatientName,
        @AppointmentDate,
        @DepartmentName,
        'Confirmed',
        @AddedAt
    );
END";


                await connection.ExecuteAsync(sql, new
                {
                    appointment.ProviderId,
                    appointment.AppointmentId,
                    appointment.PatientName,
                    appointment.AppointmentDate,
                    appointment.DepartmentName,
                    AddedAt = DateTime.UtcNow
                });


                _logger.LogInformation(
                    "[ProviderSchedule] Schedule updated successfully for Provider={ProviderName}",
                    appointment.ProviderName);


                // Remove the message from the subscription.
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[ProviderSchedule] Failed to process MessageId={MessageId}",
                    args.Message.MessageId);


                // Return the message so Service Bus can retry.
                await args.AbandonMessageAsync(args.Message);
            }
        };


        // ---------------------------------------------------------------------
        // Handle Service Bus processor errors.
        // ---------------------------------------------------------------------
        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(
                args.Exception,
                "[ProviderSchedule] Service Bus Error. Entity={EntityPath}, Source={ErrorSource}",
                args.EntityPath,
                args.ErrorSource);


            return Task.CompletedTask;
        };


        _logger.LogInformation(
            "ProviderScheduleWorker started. Listening on Topic='{Topic}', Subscription='{Subscription}'.",
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


        _logger.LogInformation("ProviderScheduleWorker stopped.");
    }
}


