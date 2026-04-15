using LearnLuxembourgish.Api.Services;
using LearnLuxembourgish.Api.Services.Authentication;
using LearnLuxembourgish.Data.SQLite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add controllers
builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Database - default to SQLite for development
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=learnluxembourgish.db";
builder.Services.AddSQLiteDataStore(connectionString);

// Authentication configuration
var authConfig = new LearnLuxembourgish.Shared.Models.Authentication.AuthenticationConfig
{
    Jwt = new LearnLuxembourgish.Shared.Models.Authentication.JwtConfig
    {
        SecretKey = builder.Configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured"),
        Issuer = builder.Configuration["Jwt:Issuer"] ?? "LearnLuxembourgish",
        Audience = builder.Configuration["Jwt:Audience"] ?? "LearnLuxembourgish",
        AccessTokenExpirationMinutes = int.Parse(builder.Configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15"),
        RefreshTokenExpirationDays = int.Parse(builder.Configuration["Jwt:RefreshTokenExpirationDays"] ?? "7")
    },
    MedicalSecurity = new LearnLuxembourgish.Shared.Models.Authentication.MedicalSecurityConfig
    {
        RequireMfa = bool.Parse(builder.Configuration["MedicalSecurity:RequireMfa"] ?? "false")
    }
};

builder.Services.AddSingleton(authConfig);

// Authentication services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IIdTokenValidationService, IdTokenValidationService>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

// JWT Bearer authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(authConfig.Jwt.SecretKey)),
            ValidateIssuer = true,
            ValidIssuer = authConfig.Jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = authConfig.Jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();

// HTTP clients
builder.Services.AddHttpClient("translation");

// Clock abstraction — overridden with FakeTimeProvider in integration tests
builder.Services.AddSingleton(TimeProvider.System);

// Application services
builder.Services.AddScoped<ITranslationService, TranslationService>();
builder.Services.AddScoped<IGrammarService, GrammarService>();

// CORS for Blazor frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["https://localhost:5001"])
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders("Authorization") // Expose Authorization header
              .WithHeaders("Authorization", "Content-Type"); // Explicitly allow Authorization header
    });
});

var app = builder.Build();

// Apply EF migrations on startup
try
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Initializing database...");

    var db = services.GetRequiredService<LearnLuxembourgish.Data.Shared.LearnLuxembourgishDbContext>();

    // Check if database can connect
    var canConnect = await db.Database.CanConnectAsync();
    logger.LogInformation("Database can connect: {CanConnect}", canConnect);

    if (app.Environment.IsDevelopment())
    {
        // Get all migrations
        var allMigrations = db.Database.GetMigrations().ToList();
        logger.LogInformation("Total migrations defined: {Count} - {Migrations}", 
            allMigrations.Count, string.Join(", ", allMigrations));

        // Get applied migrations
        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        logger.LogInformation("Applied migrations: {Count}", appliedMigrations.Count);

        // Get pending migrations
        var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
        logger.LogInformation("Pending migrations: {Count}", pendingMigrations.Count);

        if (pendingMigrations.Any())
        {
            logger.LogInformation("Applying pending migrations...");
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully");
        }
        else
        {
            logger.LogInformation("Database is up to date");
        }
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "FATAL ERROR during database initialization: {Type} - {Message}", 
        ex.GetType().FullName, ex.Message);
    logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);

    if (ex.InnerException != null)
    {
        logger.LogError("Inner exception: {InnerType} - {InnerMessage}", 
            ex.InnerException.GetType().FullName, ex.InnerException.Message);
    }

    // Continue startup so we can see the error in the dashboard
    logger.LogWarning("Continuing startup despite database error - features requiring database will fail");
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// Always use authentication/authorization middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
