using FinancialPlanner.Api.Auth;
using FinancialPlanner.Api.Email;
using FinancialPlanner.Api;
using FinancialPlanner.Infrastructure;
using FinancialPlanner.Infrastructure.Identity;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();
var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (isProduction && string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres must be configured in production.");
}

if (isProduction)
{
    builder.Services
        .AddOptions<InfrastructureOptions>()
        .Bind(builder.Configuration.GetSection(InfrastructureOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services
        .AddOptions<AuthOptions>()
        .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
}

builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddPlannerPersistence(connectionString);
    builder.Services.AddHangfire(configuration => configuration.UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString)));
}
else
{
    builder.Services.AddDbContext<PlannerDbContext>(options => options.UseInMemoryDatabase("financial-planner"));
}

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.Password.RequiredLength = 14;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
        options.SignIn.RequireConfirmedEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<PlannerDbContext>()
    .AddSignInManager();
builder.Services.AddScoped<IAuthenticationEmailSender, DevelopmentAuthenticationEmailSender>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<SessionService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                builder.Configuration["Authentication:JwtSigningKey"]
                ?? throw new InvalidOperationException("Authentication:JwtSigningKey must be configured.")))
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies["fp_access"];
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var webOrigin = builder.Configuration["Infrastructure:WebOrigin"];
if (!string.IsNullOrWhiteSpace(webOrigin))
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod()));
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(connectionString) && isProduction)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PlannerDbContext>().Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapControllers();
app.MapAuthenticationEndpoints();

app.Run();

public partial class Program;
