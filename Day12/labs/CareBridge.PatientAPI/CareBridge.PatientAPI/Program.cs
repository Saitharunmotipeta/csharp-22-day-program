
using CareBridge.PatientAPI.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Singleton: one instance shared for the application lifetime
builder.Services.AddControllers();
builder.Services.AddSingleton<PatientRepository>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "CareBridge Patient API",
        Version = "v1",
        Description = "REST API for CareBridge clinical patient management"
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

