using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Workers;


// ─────────────────────────────────────────────────────────────────────────────
// CacheRefreshWorker
// ----------------------------------------------------------------------------
// Background Service that listens for AppointmentConfirmed events from
// Azure Service Bus.
//
// Business Purpose
// ----------------
// When an appointment changes from Pending to Confirmed, any cached data
// representing pending appointments becomes stale.
//
// This worker demonstrates the Cache-Aside Pattern by invalidating cache
// entries after the database has been updated.
//
// Cache is never the source of truth.
// SQL Server remains the authoritative data source.
// ─────────────────────────────────────────────────────────────────────────────
public class CacheRefreshWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CacheRefreshWorker> _logger;


    public CacheRefreshWorker(
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<CacheRefreshWorker> logger)
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
            _configuration["ServiceBus:Subscriptions:CacheRefresh"]
            ?? throw new InvalidOperationException("CacheRefresh subscription not configured.");


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
                    "[CacheRefresh] Processing MessageId={MessageId}",
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


                // -----------------------------------------------------------------
                // Invalidate cached data.
                // The next API request will reload fresh data from SQL Server.
                // -----------------------------------------------------------------


                // Cached list of pending appointments.
                _cache.Remove("pending-appointments");


                // Cached appointment details.
                _cache.Remove($"appointment-{appointment.AppointmentId}");


                // Cached analytics for today.
                _cache.Remove($"analytics-{DateTime.UtcNow:yyyy-MM-dd}");


                _logger.LogInformation(
                    "[CacheRefresh] Cache invalidated successfully for AppointmentId={AppointmentId}",
                    appointment.AppointmentId);


                // Remove the message from the subscription.
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[CacheRefresh] Failed to process MessageId={MessageId}",
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
                "[CacheRefresh] Service Bus Error. Entity={EntityPath}, Source={ErrorSource}",
                args.EntityPath,
                args.ErrorSource);


            return Task.CompletedTask;
        };


        _logger.LogInformation(
            "CacheRefreshWorker started. Listening on Topic='{Topic}', Subscription='{Subscription}'.",
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


        _logger.LogInformation("CacheRefreshWorker stopped.");
    }
}



