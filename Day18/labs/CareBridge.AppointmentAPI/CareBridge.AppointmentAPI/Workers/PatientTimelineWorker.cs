using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Workers;


// ─────────────────────────────────────────────────────────────────────────────
// PatientTimelineWorker
// ----------------------------------------------------------------------------
// Background Service that listens to the Azure Service Bus topic subscription.
//
// Business Purpose
// ----------------
// Whenever an AppointmentConfirmed event is published, this worker receives
// the message and creates a permanent audit entry in the PatientTimeline table.
//
// This demonstrates an event-driven architecture where the Appointment API
// publishes an event once, and multiple independent services react without
// changing the API.
// ─────────────────────────────────────────────────────────────────────────────
public class PatientTimelineWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PatientTimelineWorker> _logger;


    public PatientTimelineWorker(
        IConfiguration configuration,
        ILogger<PatientTimelineWorker> logger)
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
            _configuration["ServiceBus:Subscriptions:PatientTimeline"]
            ?? throw new InvalidOperationException("PatientTimeline subscription not configured.");


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
        // Fired whenever a new AppointmentConfirmed event arrives.
        // ---------------------------------------------------------------------
        processor.ProcessMessageAsync += async args =>
        {
            try
            {
                _logger.LogInformation(
                    "[PatientTimeline] Processing MessageId={MessageId}",
                    args.Message.MessageId);


                var appointment = JsonSerializer.Deserialize<AppointmentEvent>(
                    args.Message.Body.ToString());


                if (appointment is null)
                {
                    throw new InvalidOperationException(
                        "Unable to deserialize AppointmentEvent.");
                }


                using var connection = new SqlConnection(sqlConnectionString);


                const string sql = @"
INSERT INTO PatientTimeline
(
    PatientId,
    AppointmentId,
    EventType,
    EventDate,
    ProviderName,
    DepartmentName,
    Notes
)
VALUES
(
    @PatientId,
    @AppointmentId,
    'Appointment Confirmed',
    @AppointmentDate,
    @ProviderName,
    @DepartmentName,
    @Notes
);";


                await connection.ExecuteAsync(sql, new
                {
                    appointment.PatientId,
                    appointment.AppointmentId,
                    appointment.AppointmentDate,
                    appointment.ProviderName,
                    appointment.DepartmentName,
                    appointment.Notes
                });


                _logger.LogInformation(
                    "[PatientTimeline] Timeline updated successfully for PatientId={PatientId}",
                    appointment.PatientId);


                // Tell Azure Service Bus that processing completed successfully.
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[PatientTimeline] Failed to process MessageId={MessageId}",
                    args.Message.MessageId);


                // Return the message to the queue for retry.
                await args.AbandonMessageAsync(args.Message);
            }
        };


        // ---------------------------------------------------------------------
        // Fired whenever the Service Bus processor encounters an error.
        // ---------------------------------------------------------------------
        processor.ProcessErrorAsync += args =>
        {
            _logger.LogError(
                args.Exception,
                "[PatientTimeline] Service Bus Error. Entity={EntityPath}, Source={ErrorSource}",
                args.EntityPath,
                args.ErrorSource);


            return Task.CompletedTask;
        };


        _logger.LogInformation(
            "PatientTimelineWorker started. Listening on Topic='{Topic}', Subscription='{Subscription}'.",
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


        _logger.LogInformation("PatientTimelineWorker stopped.");
    }
}

