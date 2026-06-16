var builder = WebApplication.CreateBuilder(args);

// These three lines were already here from the project template.
// AddControllers() enables the [ApiController] style we used above.
// AddEndpointsApiExplorer() and AddSwaggerGen() together create the
// interactive Swagger test page (the one with the green/blue buttons).
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===================================================================
// CORS CONFIGURATION
// We define a named POLICY called "AllowAll" that permits requests
// from ANY origin, using ANY HTTP method, with ANY headers.
//
// SECURITY NOTE: "AllowAll" is intentionally permissive and is meant
// ONLY for local training/demo use. A real production system would
// restrict this to specific, known origins (e.g. only the hospital's
// own front-end address).
// ===================================================================
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

// Only show the Swagger test page when running in Development mode
// (this is the default when you run from Visual Studio).
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Activate the CORS policy we defined above. This MUST come before
// UseAuthorization() and MapControllers() to take effect correctly.
app.UseCors("AllowAll");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();


