using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Models;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Text.Json;


namespace CareBridge.AppointmentAPI.Services;


// This class is registered as a Singleton in Program.cs.
// Why Singleton? ServiceBusClient manages TCP connections to Azure Service Bus.
// Creating a new one per request is slow and wastes resources.
// Singleton ensures one instance lives for the entire application lifetime.
public class AppointmentService
{
    private readonly string _connStr;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<AppointmentService> _logger;


    // Constructor receives dependencies via Dependency Injection (DI).
    // IConfiguration: reads settings from appsettings.json.
    // ServiceBusClient: injected as Singleton (registered in Program.cs).
    // ILogger: captures logs for debugging and monitoring.
    public AppointmentService(
        IConfiguration config,
        ServiceBusClient serviceBusClient,
        ILogger<AppointmentService> logger)
    {
        _logger = logger;


        // Read the database connection string from appsettings.json.
        // "CareBridgeDB" is the key in the ConnectionStrings section.
        // ?? throw: if the key is missing, fail fast with a clear error.
        _connStr = config.GetConnectionString("CareBridgeDB")
            ?? throw new InvalidOperationException(
                "CareBridgeDB connection string not configured.");


        // Read the Service Bus topic name from appsettings.json.
        // Falls back to "appointment-events" if not configured.
        // This lets you change the topic per environment (dev/staging/prod).
        var topicName = config["ServiceBus:TopicName"] ?? "appointment-events";


        // Create a sender for the topic. The sender is reusable and thread-safe.
        // It batches messages internally for better performance.
        _sender = serviceBusClient.CreateSender(topicName);
    }


    // Fetches all appointments with Status = 'Pending' for the receptionist dashboard.
    // Uses INNER JOINs to pull human-readable names (Patient, Provider, Department)
    // instead of just showing raw IDs.
    // Returns IEnumerable<Appointment> - a list that can be iterated.
    public async Task<IEnumerable<Appointment>> GetPendingAsync()
    {
        // 'using' ensures the SQL connection is closed and disposed automatically,
        // even if an exception occurs. This prevents connection pool exhaustion.
        using var conn = new SqlConnection(_connStr);


        // SQL query: joins 4 tables to build a rich view of pending appointments.
        // INNER JOIN ensures only appointments with valid Patient, Provider and
        // Department records are returned (orphaned records are filtered out).
        // ORDER BY AppointmentDate: shows earliest appointments first.
        const string sql = @"
SELECT
    a.AppointmentId,
    a.PatientId,
    p.FullName AS PatientName,
    a.ProviderId,
    pr.FullName AS ProviderName,
    a.DepartmentId,
    d.Name AS DepartmentName,
    a.AppointmentDate,
    a.Status,
    a.Notes
FROM Appointment a
INNER JOIN Patient p
    ON p.PatientId = a.PatientId
INNER JOIN Provider pr
    ON pr.ProviderId = a.ProviderId
INNER JOIN Department d
    ON d.DepartmentId = a.DepartmentId
WHERE a.Status = 'Pending'
ORDER BY a.AppointmentDate;";


        // Dapper's QueryAsync maps SQL result columns to the Appointment object
        // by matching column names (e.g., PatientName -> Appointment.PatientName).
        return await conn.QueryAsync<Appointment>(sql);
    }


    // Confirms an appointment and publishes an event to Azure Service Bus.
    // This follows the Outbox Pattern concept: update database first, then notify.
    // If publishing fails, the DB is already updated — downstream services may retry.
    // Returns AppointmentEvent: the confirmed appointment details for the API response.
    public async Task<AppointmentEvent> ConfirmAppointmentAsync(
        ConfirmRequest request,
        CancellationToken cancellationToken = default)
    {
        // Open a new SQL connection for this operation.
        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(cancellationToken);


        // ── STEP 1: Update the appointment status in the database ──
        // Only update if Status is 'Pending'. This prevents double-confirming
        // an already confirmed appointment (race condition protection).
        // ConfirmedBy: tracks who confirmed (receptionist user ID).
        // ConfirmedAt: stores UTC timestamp for consistency across time zones.
        const string updateSql = @"
UPDATE Appointment
SET Status = 'Confirmed',
    ConfirmedBy = @ConfirmedBy,
    ConfirmedAt = @ConfirmedAt
WHERE AppointmentId = @AppointmentId
  AND Status = 'Pending';";


        // ExecuteAsync runs the UPDATE. Returns the number of rows affected.
        // If 0 rows affected, the appointment was not found or already confirmed.
        var affected = await conn.ExecuteAsync(
            updateSql,
            new
            {
                request.AppointmentId,
                request.ConfirmedBy,
                ConfirmedAt = DateTime.UtcNow
            });


        // Guard clause: if no rows were updated, the appointment doesn't exist
        // or was already confirmed. Throw to signal failure to the controller.
        if (affected == 0)
        {
            throw new InvalidOperationException(
                $"Appointment {request.AppointmentId} was not found or has already been confirmed.");
        }


        // ── STEP 2: Retrieve the confirmed appointment details ──
        // We re-query after update to get the latest state including ConfirmedAt.
        // Same joins as GetPendingAsync to get human-readable names.
        // QuerySingleAsync: expects exactly one row. Throws if 0 or 2+ rows.
        const string selectSql = @"
SELECT
    a.AppointmentId,
    a.PatientId,
    p.FullName AS PatientName,
    a.ProviderId,
    pr.FullName AS ProviderName,
    a.DepartmentId,
    d.Name AS DepartmentName,
    a.AppointmentDate,
    a.ConfirmedAt,
    a.ConfirmedBy,
    a.Notes
FROM Appointment a
INNER JOIN Patient p
    ON p.PatientId = a.PatientId
INNER JOIN Provider pr
    ON pr.ProviderId = a.ProviderId
INNER JOIN Department d
    ON d.DepartmentId = a.DepartmentId
WHERE a.AppointmentId = @AppointmentId;";


        var appointment = await conn.QuerySingleAsync<AppointmentEvent>(
            selectSql,
            new { request.AppointmentId });


        // ── STEP 3: Serialize the event to JSON ──
        // JsonSerializer converts the C# object to a JSON string.
        // PropertyNamingPolicy.CamelCase: converts PascalCase C# properties
        // (e.g., AppointmentId) to camelCase JSON keys (appointmentId).
        // This is the standard for JSON APIs and matches JavaScript conventions.
        var json = JsonSerializer.Serialize(
            appointment,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });


        // ── STEP 4: Build the Service Bus message ──
        // ServiceBusMessage wraps the JSON payload for Azure Service Bus.
        // MessageId: unique identifier for idempotency. If the same MessageId
        // is sent twice, Service Bus deduplicates it (within a time window).
        // Format: appt-{id}-{timestamp} ensures uniqueness per confirmation.
        // Subject: labels the message type so subscribers know how to handle it.
        // ContentType: tells subscribers the payload format (JSON).
        var message = new ServiceBusMessage(json)
        {
            MessageId = $"appt-{appointment.AppointmentId}-{appointment.ConfirmedAt:yyyyMMddHHmmss}",
            Subject = "AppointmentConfirmed",
            ContentType = "application/json"
        };


        // ApplicationProperties: custom key-value metadata attached to the message.
        // Subscribers can filter messages by these properties without parsing JSON.
        // Example use case: a notification service only listens for specific departments.
        message.ApplicationProperties["EventType"] = "AppointmentConfirmed";
        message.ApplicationProperties["PatientId"] = appointment.PatientId;
        message.ApplicationProperties["Department"] = appointment.DepartmentName;


        // ── STEP 5: Publish the message to Service Bus ──
        // SendMessageAsync is non-blocking (async). The message is buffered
        // and sent in batches for efficiency. If the broker is unreachable,
        // this will throw and the API returns a 500 error.
        await _sender.SendMessageAsync(message, cancellationToken);


        // Log successful publish for observability. Structured logging
        // (key=value pairs) allows log aggregation tools to query by AppointmentId.
        _logger.LogInformation(
            "AppointmentConfirmed event published successfully. AppointmentId={AppointmentId}",
            appointment.AppointmentId);


        // Return the confirmed appointment to the controller.
        // The controller typically returns this as a 200 OK JSON response.
        return appointment;
    }
}
