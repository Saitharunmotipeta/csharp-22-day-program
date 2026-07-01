using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Hubs;
using CareBridge.AppointmentAPI.Models;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Workers;


// ─────────────────────────────────────────────────────────────────────────────
// NotificationWorker
// ----------------------------------------------------------------------------
// Background Service that listens for AppointmentConfirmed events from
// Azure Service Bus.
//
// Business Purpose
// ----------------
// Sends a real-time notification to every connected browser using SignalR.
//
// Instead of users repeatedly refreshing the page or polling the API,
// SignalR immediately pushes the notification to all connected clients.
//
// This worker demonstrates the Push Notification Pattern using
// Azure Service Bus + SignalR.
// ─────────────────────────────────────────────────────────────────────────────
public class NotificationWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IHubContext<AppointmentHub> _hubContext;
    private readonly ILogger<NotificationWorker> _logger;


    public NotificationWorker(
        IConfiguration configuration,
        IHubContext<AppointmentHub> hubContext,
        ILogger<NotificationWorker> logger)
    {
        _configuration = configuration;
        _hubContext = hubContext;
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
            _configuration["ServiceBus:Subscriptions:SignalR"]
            ?? throw new InvalidOperationException("SignalR subscription not configured.");


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
                    "[SignalR] Processing MessageId={MessageId}",
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
                // Broadcast the notification to every connected browser.
                // JavaScript clients listen for the ReceiveNotification event.
                // -----------------------------------------------------------------
                await _hubContext.Clients.All.SendAsync(
                    "ReceiveNotification",
                    new
                    {
                        Message = $"Appointment #{appointment.AppointmentId} confirmed for {appointment.ProviderName}",
                        AppointmentId = appointment.AppointmentId,
                        PatientName = appointment.PatientName,
                        ProviderName = appointment.ProviderName,
                        Department = appointment.DepartmentName,
                        AppointmentDate = appointment.AppointmentDate.ToString("yyyy-MM-dd HH:mm:ss"),
                        ConfirmedAt = appointment.ConfirmedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        ConfirmedBy = appointment.ConfirmedBy
                    },
                    stoppingToken);


                _logger.LogInformation(
                    "[SignalR] Notification broadcast successfully for AppointmentId={AppointmentId}",
                    appointment.AppointmentId);


                // Remove the message from the subscription.
                await args.CompleteMessageAsync(args.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[SignalR] Failed to process MessageId={MessageId}",
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
                "[SignalR] Service Bus Error. Entity={EntityPath}, Source={ErrorSource}",
                args.EntityPath,
                args.ErrorSource);


            return Task.CompletedTask;
        };


        _logger.LogInformation(
            "NotificationWorker started. Listening on Topic='{Topic}', Subscription='{Subscription}'.",
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


        _logger.LogInformation("NotificationWorker stopped.");
    }
}
