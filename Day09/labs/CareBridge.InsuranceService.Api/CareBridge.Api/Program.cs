using CareBridge.Api.Data;
using Microsoft.EntityFrameworkCore;
using CareBridge.Api.Services;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===================================================================
// REGISTER THE DATABASE CONTEXT
// 'AddDbContext' tells ASP.NET Core: "whenever any piece of code asks
// for a CareBridgeDbContext, create one for them, configured to talk
// to SQL Server using the connection string we just defined in
// appsettings.json (section 3.7)".
//
// This is called DEPENDENCY INJECTION - you saw this pattern on Day 8
// too. We are not creating the database connection ourselves anywhere
// in our controller code; we just ASK for it, and ASP.NET Core hands
// us a ready-to-use one.
// ===================================================================
builder.Services.AddDbContext<CareBridgeDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CareBridgeDb")));

// ===================================================================
// POLICY 1: TIMEOUT
// ===================================================================
// Policy.TimeoutAsync<HttpResponseMessage> creates a policy that will
// wrap ONE attempt of an operation that returns an HttpResponseMessage
// (which is exactly what an HTTP call returns).
//
// TimeSpan.FromSeconds(2) - this is the LIMIT. If the wrapped
// operation has not completed within 2 seconds, Polly gives up on
// THIS ATTEMPT and throws a TimeoutRejectedException - even if the
// Insurance Service might eventually have responded.
//
// TimeoutStrategy.Optimistic - this tells Polly HOW to enforce the
// timeout. 'Optimistic' relies on the operation itself respecting
// .NET's CancellationToken (which HttpClient does). This is the
// recommended, lightweight strategy for HTTP calls, and is what
// we use throughout this document.
//
// REMEMBER FROM PART 2 (section 2.5): we configured the Insurance
// Service's 'slow' chaos mode to delay every response by 5 seconds.
// This 2-second timeout will trigger EVERY TIME 'slow' mode is
// active - this is INTENTIONAL, and we will see exactly this happen
// in the live demo (Part 5.2).
var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(
    TimeSpan.FromSeconds(2),
    TimeoutStrategy.Optimistic);


// ===================================================================
// POLICY 2: RETRY WITH EXPONENTIAL BACKOFF
// ===================================================================
// HttpPolicyExtensions.HandleTransientHttpError() is a READY-MADE
// rule, provided by the Polly.Extensions.Http package (section 3.4).
// It says: 'treat any HTTP 5xx response (server-side errors, like
// the 500 our 'flaky' mode sometimes returns - see section 2.5) OR
// HTTP 408 (Request Timeout) as something worth retrying.'
//
// .Or<TimeoutRejectedException>() ADDS a second condition: 'ALSO
// retry if the operation failed because OUR OWN Timeout policy
// (defined above) gave up on it.' This is the critical link between
// Policy 1 and Policy 2 - WITHOUT this line, a timeout would NOT
// trigger a retry, it would just fail immediately.
//
// .WaitAndRetryAsync(...) - configures HOW MANY times to retry, and
// HOW LONG TO WAIT between each attempt:
//   retryCount: 3        -> try up to 3 ADDITIONAL times after the
//                           first attempt fails (so up to 4 attempts
//                           total)
//   sleepDurationProvider -> Math.Pow(2, attempt - 1) calculates:
//                           attempt 1 -> 2^0 = 1 second
//                           attempt 2 -> 2^1 = 2 seconds
//                           attempt 3 -> 2^2 = 4 seconds
//                           This DOUBLING pattern is 'exponential
//                           backoff' from Part 1.3.1 - each retry
//                           waits longer, giving a struggling
//                           dependency more breathing room.
//
// onRetry: ... -> this is a CALLBACK - a piece of code that runs
// EVERY TIME a retry is about to happen. We use it to print a clear,
// human-readable message to the console. THIS IS THE LINE THAT
// PRODUCES THE '[RETRY] Attempt...' MESSAGES YOU WILL SEE DURING THE
// LIVE DEMO.
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .Or<TimeoutRejectedException>()
    .WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
        onRetry: (outcome, timespan, retryAttempt, context) =>
        {
            Console.WriteLine(
                $"[RETRY] Attempt {retryAttempt} after {timespan.TotalSeconds}s. " +
                $"Reason: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
        });

// ===================================================================
// POLICY 3: CIRCUIT BREAKER
// ===================================================================
// Just like Retry, this policy watches for transient HTTP errors AND
// our own TimeoutRejectedException - the SAME conditions, because
// both Retry and Circuit Breaker need to agree on what counts as
// 'this call failed'.
//
// .CircuitBreakerAsync(...) configures the THREE STATES from Part
// 1.3.3 (CLOSED, OPEN, HALF-OPEN):
//
//   handledEventsAllowedBeforeBreaking: 3
//     -> After 3 CONSECUTIVE failures (as defined above), the
//        circuit transitions from CLOSED to OPEN.
//        IMPORTANT: Circuit Breaker counts FULL retry-sequences as
//        ONE 'event' each - it sits OUTSIDE Retry (we explain the
//        ordering/layering below). So '3 consecutive failures' here
//        means '3 whole verify-insurance requests, each of which
//        ALREADY went through up to 4 attempts via Retry, and ALL
//        of which still ultimately failed'.
//
//   durationOfBreak: TimeSpan.FromSeconds(30)
//     -> Once OPEN, stay OPEN for 30 seconds. During this time, NO
//        new attempts are made AT ALL - calls fail INSTANTLY with a
//        BrokenCircuitException (we handle this in section 3.12).
//
//   onBreak / onReset / onHalfOpen -> three CALLBACKS, one for each
//   state TRANSITION. Like onRetry above, these print clear
//   messages to the console so we can SEE the circuit breaker's
//   state changes happen live during the demo.
var circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .Or<TimeoutRejectedException>()
    .CircuitBreakerAsync(
        handledEventsAllowedBeforeBreaking: 3,
        durationOfBreak: TimeSpan.FromSeconds(30),
        onBreak: (outcome, breakDelay) =>
        {
            // Fires the MOMENT the circuit flips from CLOSED to OPEN.
            // 'breakDelay' is how long it will STAY open (30 seconds,
            // as configured above).
            Console.WriteLine(
                $"[CIRCUIT BREAKER] OPENED for {breakDelay.TotalSeconds}s. " +
                $"Reason: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
        },
        onReset: () =>
        {
            // Fires when the circuit goes back to CLOSED - meaning
            // a HALF-OPEN test call SUCCEEDED, and normal operation
            // has resumed.
            Console.WriteLine("[CIRCUIT BREAKER] CLOSED - calls will be attempted again.");
        },
        onHalfOpen: () =>
        {
            // Fires when the 30-second cooldown ends, and Polly is
            // about to cautiously allow exactly ONE test call through.
            Console.WriteLine("[CIRCUIT BREAKER] HALF-OPEN - testing with next call.");
        });

builder.Services.AddHttpClient<IInsuranceServiceClient, InsuranceServiceClient>(client =>
{
    // Read the Insurance Service's address from appsettings.json
    // (section 3.7). 'client' here is the underlying HttpClient -
    // we are setting its BaseAddress ONCE, here, so every call made
    // through InsuranceServiceClient (section 3.10) can use a SHORT,
    // relative path like "/api/eligibility/verify" instead of
    // repeating the full address every time.
    var baseUrl = builder.Configuration["InsuranceServiceSettings:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl!);
})
// =================================================================
// THE ORDER OF THESE THREE LINES IS NOT ARBITRARY - IT DEFINES THE
// 'NESTING DOLL' LAYERING FROM PART 1.3.4:
//
//   AddPolicyHandler(circuitBreakerPolicy)  <- OUTERMOST (added first)
//   AddPolicyHandler(retryPolicy)           <- MIDDLE
//   AddPolicyHandler(timeoutPolicy)         <- INNERMOST (added last)
//
.AddPolicyHandler(circuitBreakerPolicy)
.AddPolicyHandler(retryPolicy)
.AddPolicyHandler(timeoutPolicy);


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// app.UseDefaultFiles() - if someone requests the 'root' address
// (e.g. just https://localhost:<port>/ with nothing after it),
// automatically serve wwwroot/index.html, without them having to
// type '/index.html' explicitly.
app.UseDefaultFiles();

// app.UseStaticFiles() - actually turns on the feature of serving
// files from the wwwroot folder at all. Without this line,
// requesting /index.html would return a 404 Not Found, even though
// the file physically exists on disk.
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
