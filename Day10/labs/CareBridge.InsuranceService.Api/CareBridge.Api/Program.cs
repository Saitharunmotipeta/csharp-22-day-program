using Microsoft.AspNetCore.Authentication.JwtBearer;      // [AUTH] JWT authentication support.
using Microsoft.AspNetCore.Identity;                      // [AUTH] User and role management.
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;                     // [AUTH] Token validation.
using System.Text;
using CareBridge.Api.Data;
using CareBridge.Api.Models;
using CareBridge.Api.Services;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// ── DATABASE ──────────────────────────────────────────────────────────────
// Existing database configuration from previous days.
builder.Services.AddDbContext<CareBridgeDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CareBridgeDb")));

// ── [AUTH] ASP.NET IDENTITY ───────────────────────────────────────────────
// Registers Identity for user accounts, passwords, roles,
// lockout protection, and account management.
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // [AUTH] Password security rules.
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;

    // [AUTH] Locks account after repeated failed login attempts.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
})



// [AUTH] Stores users and roles in SQL Server.
.AddEntityFrameworkStores<CareBridgeDbContext>()
.AddDefaultTokenProviders();

// ── [AUTH] JWT TOKEN AUTHENTICATION ───────────────────────────────────────
// Reads JWT settings from appsettings.json.
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["Secret"]!;

// [AUTH] Configures how incoming JWT tokens are validated.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultChallengeScheme =
        JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters =
        new TokenValidationParameters
        {
            // [AUTH] Token must come from the expected issuer.
            ValidateIssuer = true,

            // [AUTH] Token must be intended for this application.
            ValidateAudience = true,

            // [AUTH] Expired tokens are rejected.
            ValidateLifetime = true,

            // [AUTH] Signature must match our secret key.
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],

            // [AUTH] Secret key used to verify token authenticity.
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(secretKey)),

            // [AUTH] No grace period after expiry.
            ClockSkew = TimeSpan.Zero
        };
});


// [AUTH] Enables role-based authorization checks.
builder.Services.AddAuthorization();

// ── POLLY + HTTP CLIENT (UNCHANGED) ──────────────────────────────────────
var insuranceServiceUrl =
    builder.Configuration["InsuranceServiceSettings:BaseUrl"]
    ?? "https://localhost:7056";

var timeoutPolicy =
    Policy.TimeoutAsync<HttpResponseMessage>(2);

var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .Or<Polly.Timeout.TimeoutRejectedException>()
    .WaitAndRetryAsync(
        3,
        attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
        (outcome, timespan, attempt, ctx) =>
            Console.WriteLine(
                $"[RETRY] Attempt {attempt} after {timespan.TotalSeconds}s"));

var circuitBreakerPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(
        3,
        TimeSpan.FromSeconds(30),
        onBreak: (_, _) =>
            Console.WriteLine("[CIRCUIT BREAKER] OPENED"),
        onReset: () =>
            Console.WriteLine("[CIRCUIT BREAKER] RESET"),
        onHalfOpen: () =>
            Console.WriteLine("[CIRCUIT BREAKER] HALF-OPEN"));

builder.Services
    .AddHttpClient<IInsuranceServiceClient, InsuranceServiceClient>(
        client => client.BaseAddress = new Uri(insuranceServiceUrl))
    .AddPolicyHandler(circuitBreakerPolicy)
    .AddPolicyHandler(retryPolicy)
    .AddPolicyHandler(timeoutPolicy);

// ── SWAGGER ───────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    // [AUTH] Allows JWT tokens to be entered in Swagger.
    var securityScheme =
        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "Enter: Bearer {your token}",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
            {
                Type =
                    Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        };

    c.AddSecurityDefinition("Bearer", securityScheme);

    c.AddSecurityRequirement(
        new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            { securityScheme, Array.Empty<string>() }
        });
});

// ── CORS ──────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()));

var app = builder.Build();

// ── [AUTH] ROLE + ADMIN SEEDING ───────────────────────────────────────────
// Creates required roles and a default administrator account
// when the application starts for the first time.
using (var scope = app.Services.CreateScope())
{
    var roleManager =
        scope.ServiceProvider
             .GetRequiredService<RoleManager<IdentityRole>>();

    var userManager =
        scope.ServiceProvider
             .GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles =
    {
        "Nurse",
        "Receptionist",
        "InsuranceCoordinator",
        "Administrator"
    };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(
                new IdentityRole(role));
        }
    }

    const string adminEmail = "admin@carebridge.local";
    const string adminPassword = "Admin@123";

    if (await userManager.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = "System Administrator",
            Department = "IT"
        };

        await userManager.CreateAsync(
            admin,
            adminPassword);

        await userManager.AddToRoleAsync(
            admin,
            "Administrator");
    }
}

// ── MIDDLEWARE PIPELINE ───────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseCors();
app.UseHttpsRedirection();

// [AUTH] Step 1:
// Validates the JWT token and identifies the user.
app.UseAuthentication();

// [AUTH] Step 2:
// Checks whether the authenticated user
// has permission to access the endpoint.
app.UseAuthorization();

app.MapControllers();

app.Run();


