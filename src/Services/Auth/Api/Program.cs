using System.Text;
using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Middleware;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.IntegrationEvents;
using TaxVision.Auth.Infrastructure;
using TaxVision.Auth.Infrastructure.Security;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;
using BuildingBlocks.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("auth-service");
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddBuildingBlocks();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddAuthInfrastructure(builder.Configuration);

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException("JWT configuration is missing.");

if (Encoding.UTF8.GetByteCount(jwtOptions.Secret) < 32)
    throw new InvalidOperationException("JWT secret must contain at least 32 bytes.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(LoginHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var rabbitUri = builder.Configuration["RabbitMq:Uri"]
        ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    var sqlConn = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(new Uri(rabbitUri)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();

    options.PublishMessage<UserRegisteredIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    options.ListenToRabbitQueue("auth-tenant-events", queue =>
    {
        queue.BindExchange("taxvision-events", string.Empty);
    }).UseDurableInbox();

    options.Policies.OnException<Exception>()
        .RetryWithCooldown(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15));
});


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Auth API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
