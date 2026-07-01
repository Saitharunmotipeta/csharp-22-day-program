using CareBridge.AppointmentAPI.Models;
using CareBridge.AppointmentAPI.Services;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;


namespace CareBridge.AppointmentAPI.Controllers;


// ApiController attribute enables automatic model validation,
// automatic HTTP 400 responses for bad input, and binding source inference.
// Route attribute defines the base URL pattern: api/appointment.
// [controller] is a token that resolves to "appointment" (lowercased class name minus "Controller").
[ApiController]
[Route("api/[controller]")]
public class AppointmentController : ControllerBase
{
    // Dependencies injected via the constructor (Constructor Injection).
    // ASP.NET Core's built-in DI container resolves these automatically
    // based on what was registered in Program.cs.
    private readonly AppointmentService _service;
    private readonly ILogger<AppointmentController> _logger;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;


    // Constructor receives all dependencies from the DI container.
    // AppointmentService: handles business logic and Service Bus publishing.
    // ILogger: captures structured logs for this controller type.
    // IMemoryCache: in-memory caching for expensive/computed data.
    // IConfiguration: reads settings from appsettings.json and environment variables.
    public AppointmentController(
        AppointmentService service,
        ILogger<AppointmentController> logger,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _service = service;
        _logger = logger;
        _cache = cache;
        _configuration = configuration;
    }


    // ── GET: api/appointment/pending ──
    // Returns all appointments with Status = 'Pending'.
    // Called by the Reception Dashboard to show appointments awaiting confirmation.
    // ActionResult<T> wraps the response with HTTP status codes.
    // IEnumerable<Appointment> returns a collection that can be streamed.
    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<Appointment>>> GetPending()
    {
        // Delegate to the service layer. Controllers should be thin —
        // they handle HTTP concerns (routing, status codes) and delegate
        // business logic to services for testability and separation of concerns.
        var appointments = await _service.GetPendingAsync();


        // Ok() returns HTTP 200 with the payload serialized as JSON.
        // ASP.NET Core handles JSON serialization automatically.
        return Ok(appointments);
    }


    // ── POST: api/appointment/confirm ──
    // Confirms an appointment and publishes an event to Azure Service Bus.
    // [FromBody] tells ASP.NET Core to deserialize the JSON request body
    // into a ConfirmRequest object using the built-in JSON serializer.
    // CancellationToken is injected by the framework and signals when
    // the client disconnects or the request times out — pass it to async
    // operations so they can cancel gracefully instead of hanging.
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmRequest request,
        CancellationToken cancellationToken)
    {
        // ── Input Validation (Manual Guard Clauses) ──
        // While [ApiController] enables automatic model validation,
        // explicit guard clauses provide clearer, more specific error messages.
        // They also run before any service/database calls, failing fast.


        // Null check: if the request body was empty or malformed deserialization failed.
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }


        // Business rule: AppointmentId must be a positive integer.
        // IDs in databases typically start at 1. Zero or negative values
        // indicate a bug in the client or a default/uninitialized value.
        if (request.AppointmentId <= 0)
        {
            return BadRequest("AppointmentId must be greater than zero.");
        }


        // Business rule: the person confirming must be identified.
        // string.IsNullOrWhiteSpace checks for null, empty string, or only whitespace.
        // This prevents anonymous confirmations that cannot be audited.
        if (string.IsNullOrWhiteSpace(request.ConfirmedBy))
        {
            return BadRequest("ConfirmedBy is required.");
        }


        // Log the incoming request for observability and audit trails.
        // Structured logging (key=value) allows querying logs by AppointmentId
        // in tools like Azure Monitor, Splunk, or Seq.
        _logger.LogInformation(
            "Appointment confirmation request received. AppointmentId={AppointmentId}, ConfirmedBy={ConfirmedBy}",
            request.AppointmentId,
            request.ConfirmedBy);


        // ── Try-Catch-Finally Pattern ──
        // Wraps the business operation to handle different exception types
        // and return appropriate HTTP status codes instead of crashing.
        try
        {
            // Delegate to the service layer to update the database and publish the event.
            // Pass CancellationToken so the operation can be cancelled if the client disconnects.
            var appointment = await _service.ConfirmAppointmentAsync(
                request,
                cancellationToken);


            // Return HTTP 200 with a structured success response.
            // Anonymous object: creates a JSON shape inline without defining a separate class.
            // This is fine for simple controller responses; for reuse, define a DTO class.
            return Ok(new
            {
                Success = true,
                Message = "Appointment confirmed successfully. AppointmentConfirmed event published to Azure Service Bus.",
                Appointment = appointment
            });
        }
        // Catch known business errors thrown by the service layer.
        // InvalidOperationException is thrown when the appointment is already confirmed
        // or does not exist. Log as Warning (not Error) because this is expected behavior.
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,                          // Pass exception for stack trace in logs
                "Unable to confirm AppointmentId={AppointmentId}",
                request.AppointmentId);


            // Return HTTP 400 (Bad Request) — the client sent a valid request
            // but the business state does not allow the operation.
            return BadRequest(new
            {
                Success = false,
                Message = ex.Message
            });
        }
        // Catch-all for unexpected errors (database down, network issues, bugs).
        // Log as Error because this indicates a system problem that needs investigation.
        // Never expose raw exception details to the client (security risk).
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while confirming AppointmentId={AppointmentId}",
                request.AppointmentId);


            // Return HTTP 500 (Internal Server Error) with a generic message.
            // StatusCodes.Status500InternalServerError is a constant (500) for readability.
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                Success = false,
                Message = "An unexpected error occurred while confirming the appointment."
            });
        }
    }


    // ── GET: api/appointment/analytics ──
    // Returns today's appointment confirmation statistics by department and provider.
    // Demonstrates the Cache-Aside Pattern: check cache first, fall back to database.
    // Cache-Aside is useful when data changes infrequently but is queried often.
    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        // Build a cache key that includes today's date.
        // This ensures each day gets its own cache entry.
        // Using UTC avoids timezone issues in distributed systems.
        var cacheKey = $"analytics-{DateTime.UtcNow:yyyy-MM-dd}";


        // ── Cache-Aside Step 1: Check the cache ──
        // TryGetValue returns true if the key exists and is not expired.
        // out object? cachedData: the "?" makes it nullable — the cache might return null.
        // If cached, return immediately (fast path) to avoid a database round-trip.
        if (_cache.TryGetValue(cacheKey, out object? cachedData))
        {
            // Return cached data with a flag so the client knows it came from cache.
            return Ok(new
            {
                source = "cache",
                data = cachedData
            });
        }


        // ── Cache-Aside Step 2: Query the database ──
        // Cache miss: open a SQL connection and fetch fresh data.
        // Connection string is read from configuration — never hardcode credentials.
        using var connection = new SqlConnection(
            _configuration.GetConnectionString("CareBridgeDB"));


        // Query the Analytics table for today's records.
        // ORDER BY TotalConfirmed DESC: shows busiest departments first.
        const string sql = @"
SELECT
    DepartmentName,
    ProviderName,
    TotalConfirmed,
    LastUpdated
FROM Analytics
WHERE RecordDate = @RecordDate
ORDER BY TotalConfirmed DESC;";


        // Execute query with a strongly typed parameter.
        // Dapper prevents SQL injection by using parameterized queries.
        // DateTime.UtcNow.Date strips the time component (midnight) for date comparison.
        var rows = await connection.QueryAsync(sql, new
        {
            RecordDate = DateTime.UtcNow.Date
        });


        // ── Transform dynamic results to strongly typed objects ──
        // Dapper returns dynamic objects (DapperRow) when no generic type is specified.
        // .Select() projects each row into an anonymous object with specific types.
        // (string?)r.DepartmentName: nullable cast — some departments might be null.
        // (int)r.TotalConfirmed: non-nullable cast — this column is required.
        // .ToList(): materializes the LINQ query into a concrete list.
        var result = rows.Select(r => new
        {
            departmentName = (string?)r.DepartmentName,
            providerName = (string?)r.ProviderName,
            totalConfirmed = (int)r.TotalConfirmed,
            lastUpdated = (DateTime)r.LastUpdated
        }).ToList();


        // ── Cache-Aside Step 3: Store in cache ──
        // Set the cache entry with an absolute expiration of 5 minutes.
        // After 5 minutes, the entry is automatically removed and the next
        // request will hit the database again (cache refresh).
        // TimeSpan.FromMinutes(5) is more readable than new TimeSpan(0, 5, 0).
        _cache.Set(
            cacheKey,
            result,
            TimeSpan.FromMinutes(5));


        // Return fresh data with a flag indicating it came from the database.
        return Ok(new
        {
            source = "database",
            data = result
        });
    }


    // ── GET: api/appointment/timeline/{patientId} ──
    // Returns the complete event history for a specific patient.
    // {patientId:int} is a route constraint: ensures patientId is an integer.
    // Without the constraint, "abc" would match and fail during binding.
    // With the constraint, "abc" returns HTTP 404 (not found) automatically.
    [HttpGet("timeline/{patientId:int}")]
    public async Task<IActionResult> GetTimeline(int patientId)
    {
        // Open a database connection for this read-only query.
        // "using" ensures the connection closes even if an exception occurs.
        using var connection = new SqlConnection(
            _configuration.GetConnectionString("CareBridgeDB"));


        // Query the PatientTimeline table joined with Patient for the name.
        // ORDER BY EventDate DESC: shows the most recent events first (newest on top).
        const string sql = @"
SELECT
    pt.TimelineId,
    pt.EventType,
    pt.EventDate,
    pt.ProviderName,
    pt.DepartmentName,
    pt.Notes,
    p.FullName AS PatientName
FROM PatientTimeline pt
INNER JOIN Patient p
    ON p.PatientId = pt.PatientId
WHERE pt.PatientId = @PatientId
ORDER BY pt.EventDate DESC;";


        // Execute the query with the route parameter bound to @PatientId.
        // Dapper maps column names to object properties automatically.
        // Since no generic type is specified, returns dynamic objects.
        var timeline = await connection.QueryAsync(sql, new
        {
            PatientId = patientId
        });


        // Return HTTP 200 with the timeline data.
        // If no records exist, returns an empty array [] with HTTP 200 (not 404).
        // Empty result is valid — the patient simply has no timeline events yet.
        return Ok(timeline);
    }
}


