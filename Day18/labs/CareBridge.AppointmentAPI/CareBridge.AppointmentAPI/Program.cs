
using Azure.Messaging.ServiceBus;
using CareBridge.AppointmentAPI.Hubs;
using CareBridge.AppointmentAPI.Services;
using CareBridge.AppointmentAPI.Workers;


var builder = WebApplication.CreateBuilder(args);


// ─────────────────────────────────────────────────────────────────────────────
// 1. Register MVC Controllers
// ----------------------------------------------------------------------------
// Enables ASP.NET Core Web API controllers.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();


// ─────────────────────────────────────────────────────────────────────────────
// 2. Register Swagger / OpenAPI
// ----------------------------------------------------------------------------
// Provides an interactive UI for testing the API.
// Available at /swagger during development.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();


builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "CareBridge Appointment API",
        Version = "v1",
        Description = "Azure Service Bus Event-Driven Healthcare Demo"
    });
});


// ─────────────────────────────────────────────────────────────────────────────
// 3. Register SignalR
// ----------------------------------------------------------------------------
// Enables real-time communication between server and browser clients.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();


// ─────────────────────────────────────────────────────────────────────────────
// 4. Register In-Memory Cache
// ----------------------------------------------------------------------------
// Demonstrates the Cache-Aside Pattern.
//
// Suitable for demos and single-server deployments.
// Production systems typically use Azure Cache for Redis.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();


// ─────────────────────────────────────────────────────────────────────────────
// 5. Serve Static Files
// ----------------------------------------------------------------------------
// Allows dashboard.html / index.html to be served from wwwroot.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddDirectoryBrowser();


// ─────────────────────────────────────────────────────────────────────────────
// 6. Register Azure Service Bus Client
// ----------------------------------------------------------------------------
// ServiceBusClient is thread-safe and expensive to create.
// Microsoft recommends registering it as a Singleton.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();


    return new ServiceBusClient(
        configuration["ServiceBus:ConnectionString"]
        ?? throw new InvalidOperationException(
            "Service Bus connection string not configured."));
});


// ─────────────────────────────────────────────────────────────────────────────
// 7. Register Business Services
// ----------------------------------------------------------------------------
// AppointmentService publishes AppointmentConfirmed events.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<AppointmentService>();


// ─────────────────────────────────────────────────────────────────────────────
// 8. Register Background Workers
// ----------------------------------------------------------------------------
// Each Hosted Service listens to its own Azure Service Bus subscription.
//
// Every worker independently reacts to the same AppointmentConfirmed event.
// This demonstrates an Event-Driven Architecture.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddHostedService<PatientTimelineWorker>();
builder.Services.AddHostedService<ProviderScheduleWorker>();
builder.Services.AddHostedService<AnalyticsWorker>();
builder.Services.AddHostedService<AuditWorker>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<CacheRefreshWorker>();


// ─────────────────────────────────────────────────────────────────────────────
// 9. Configure CORS
// ----------------------------------------------------------------------------
// Allow browser clients to access the API and SignalR.
//
// Production environments should restrict allowed origins.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


var app = builder.Build();


// ─────────────────────────────────────────────────────────────────────────────
// Configure HTTP Request Pipeline
// ----------------------------------------------------------------------------
// Middleware execution order is important.
// ─────────────────────────────────────────────────────────────────────────────


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();


    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "CareBridge Appointment API v1");


        options.DocumentTitle = "CareBridge Appointment API";
    });
}


// Serve wwwroot/index.html automatically.
app.UseDefaultFiles();
app.UseStaticFiles();


app.UseHttpsRedirection();


app.UseCors();


app.UseAuthorization();


// Map Web API Controllers.
app.MapControllers();


// Map the SignalR Hub.
//
// Browser clients connect to:
//
// https://localhost:5001/hubs/appointments
//
app.MapHub<AppointmentHub>("/hubs/appointments");


app.Run();



