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
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();
var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (isProduction && string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres must be configured in production.");
}

builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));

if (isProduction)
{
    builder.Services
        .AddOptions<InfrastructureOptions>()
        .Bind(builder.Configuration.GetSection(InfrastructureOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services
        .AddOptions<GmailSmtpOptions>()
        .Bind(builder.Configuration.GetSection(GmailSmtpOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<AuthOptions>().ValidateDataAnnotations().ValidateOnStart();
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
    .AddSignInManager()
    .AddDefaultTokenProviders();
if (isProduction)
{
    builder.Services.AddScoped<IAuthenticationEmailSender, GmailAuthenticationEmailSender>();
}
else
{
    builder.Services.AddSingleton<DevelopmentAuthenticationEmailSender>();
    builder.Services.AddScoped<IAuthenticationEmailSender>(provider => provider.GetRequiredService<DevelopmentAuthenticationEmailSender>());
}
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<LegalEntityAccessService>();
builder.Services.AddScoped<IAuthorizationHandler, TenantCapabilityHandler>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TenantRead", policy => policy.AddRequirements(new TenantCapabilityRequirement(TenantCapability.Read)));
    options.AddPolicy("TenantReview", policy => policy.AddRequirements(new TenantCapabilityRequirement(TenantCapability.Review)));
    options.AddPolicy("TenantWrite", policy => policy.AddRequirements(new TenantCapabilityRequirement(TenantCapability.Write)));
    options.AddPolicy("TenantManage", policy => policy.AddRequirements(new TenantCapabilityRequirement(TenantCapability.Manage)));
});

var webOrigin = builder.Configuration["Infrastructure:WebOrigin"];
if (!string.IsNullOrWhiteSpace(webOrigin))
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        policy.WithOrigins(webOrigin).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
}

var app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!string.IsNullOrWhiteSpace(connectionString))
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
app.MapAuthorizationEndpoints();
if (!isProduction)
{
    app.MapGet("/api/auth/development/emails/{email}", (string email, DevelopmentAuthenticationEmailSender sender) =>
    {
        var message = sender.GetLatest(email);
        return message is null ? Results.NotFound() : Results.Ok(message);
    });
}

app.Run();

public partial class Program;
