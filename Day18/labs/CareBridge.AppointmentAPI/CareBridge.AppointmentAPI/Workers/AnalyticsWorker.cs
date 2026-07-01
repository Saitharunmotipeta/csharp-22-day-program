using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Workers;


// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsWorker
// ----------------------------------------------------------------------------
// Background Service that listens for AppointmentConfirmed events from
// Azure Service Bus.
//
// Business Purpose
// ----------------
// Maintains daily appointment confirmation statistics grouped by
// Department and Provider.
//
// The Appointment API can read these statistics using the Cache-Aside
// Pattern:
//
// 1. Check IMemoryCache
// 2. If cache exists → return cached data
// 3. If cache is empty → query SQL Server and cache the result
//
// Whenever a new appointment is confirmed, this worker updates SQL Server
// and invalidates today's cache so the next API request retrieves fresh data.
// ─────────────────────────────────────────────────────────────────────────────
public class AnalyticsWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AnalyticsWorker> _logger;


    public AnalyticsWorker(
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<AnalyticsWorker> logger)
    {
        _configuration = configuration;
        _cache = cache;
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
            _configuration["ServiceBus:Subscriptions:Analytics"]
            ?? throw new InvalidOperationException("Analytics subscription not configured.");


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
                    "[Analytics] Processing MessageId={MessageId}",
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


                // MERGE performs an UPSERT.
                // If today's analytics row exists, increment TotalConfirmed.
                // Otherwise create a new row.
                const string sql = @"
MERGE Analytics AS Target
USING
(
    SELECT
        @RecordDate AS RecordDate,
        @DepartmentId AS DepartmentId,
        @DepartmentName AS DepartmentName,
        @ProviderId AS ProviderId,
        @ProviderName AS ProviderName
) AS Source
ON
    Target.RecordDate = Source.RecordDate
    AND Target.DepartmentId = Source.DepartmentId
    AND Target.ProviderId = Source.ProviderId


WHEN MATCHED THEN
    UPDATE SET
        TotalConfirmed = Target.TotalConfirmed + 1,
        LastUpdated = GETUTCDATE()


WHEN NOT MATCHED THEN
    INSERT
    (
        RecordDate,
        DepartmentId,
        DepartmentName,
        TotalConfirmed,
        ProviderId,
        ProviderName,
        LastUpdated
    )
    VALUES
    (
        Source.RecordDate,
        Source.DepartmentId,
        Source.DepartmentName,
        1,
        Source.ProviderId,
        Source.ProviderName,
        GETUTCDATE()
    );";


                await connection.ExecuteAsync(sql, new
                {
                    RecordDate = DateTime.UtcNow.Date,
                    appointment.DepartmentId,
                    appointment.DepartmentName,
                    appointment.ProviderId,
                    appointment.ProviderName
                });


                // -----------------------------------------------------------------
                // Cache-Aside Pattern
                // Remove today's cache entry.
                // The next API request will reload fresh analytics from SQL Server.
                // -----------------------------------------------------------------
                var cacheKey = $"analytics-{DateTime.UtcNow:yyyy-MM-dd}";
                _cache.Remove(cacheKey);


                _logger.LogInformation(
                    "[Analytics] Statistics updated for Department={Department}, Provider={Provider}",
                    appointment.DepartmentName,
                    appointment.ProviderName);


                // Remove the message from the subscription.
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[Analytics] Failed to process MessageId={MessageId}",
                    args.Message.MessageId);


                // Return the message to Service Bus so it can be retried.
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
                "[Analytics] Service Bus Error. Entity={EntityPath}, Source={ErrorSource}",
                args.EntityPath,
                args.ErrorSource);


            return Task.CompletedTask;
        };


        _logger.LogInformation(
            "AnalyticsWorker started. Listening on Topic='{Topic}', Subscription='{Subscription}'.",
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


        _logger.LogInformation("AnalyticsWorker stopped.");
    }
}
