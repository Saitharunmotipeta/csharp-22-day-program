using Microsoft.AspNetCore.SignalR;


namespace CareBridge.AppointmentAPI.Hubs;


// ─────────────────────────────────────────────────────────────────────────────
// AppointmentHub
// ----------------------------------------------------------------------------
// SignalR Hub responsible for managing real-time client connections.
//
// Browser clients connect to:
//      /hubs/appointments
//
// The Worker Service does not call methods on this Hub directly.
// Instead, it uses IHubContext<AppointmentHub> to push notifications to all
// connected clients whenever an AppointmentConfirmed event is received from
// Azure Service Bus.
// ─────────────────────────────────────────────────────────────────────────────
public class AppointmentHub : Hub
{
    private readonly ILogger<AppointmentHub> _logger;


    public AppointmentHub(ILogger<AppointmentHub> logger)
    {
        _logger = logger;
    }


    // ─────────────────────────────────────────────────────────────────────
    // Called automatically whenever a browser establishes a SignalR
    // connection with the server.
    // ─────────────────────────────────────────────────────────────────────
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "SignalR client connected. ConnectionId={ConnectionId}",
            Context.ConnectionId);


        await base.OnConnectedAsync();
    }


    // ─────────────────────────────────────────────────────────────────────
    // Called automatically whenever a browser disconnects.
    // Examples:
    // • Browser tab closed
    // • User refreshes the page
    // • Internet connection lost
    // • Application shuts down
    // ─────────────────────────────────────────────────────────────────────
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is null)
        {
            _logger.LogInformation(
                "SignalR client disconnected. ConnectionId={ConnectionId}",
                Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning(
                exception,
                "SignalR client disconnected unexpectedly. ConnectionId={ConnectionId}",
                Context.ConnectionId);
        }


        await base.OnDisconnectedAsync(exception);
    }
}
