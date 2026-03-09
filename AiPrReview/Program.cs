using AiPrReview.Application.Interfaces;
using AiPrReview.Application.Services;
using AiPrReview.Core.Interfaces;
using AiPrReview.Infrastructure.Caching;
using AiPrReview.Infrastructure.Data;
using AiPrReview.Infrastructure.Factories;
using AiPrReview.Infrastructure.Policies;
using AiPrReview.Infrastructure.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();


// Ensure the connection string is not null by using the null-coalescing operator
string sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

// Register DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(sqlConnectionString));

// Repositories
builder.Services.AddScoped<IProviderConfigRepository, SqlProviderConfigRepository>();
builder.Services.AddScoped<ILlmProviderRepository, SqlLlmProviderRepository>();
builder.Services.AddScoped<IProviderConfigRepository, SqlProviderConfigRepository>();

// Caching
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IConfigurationCache, MemoryConfigurationCache>();

// Application services
builder.Services.AddScoped<IConfigurationService, ConfigurationService>();
builder.Services.AddScoped<IPromptBuilder, PromptBuilder>();
builder.Services.AddScoped<IAiResponseParser, AiResponseParser>();
builder.Services.AddScoped<IReviewStrategyExecutor, ReviewStrategyExecutor>();
builder.Services.AddScoped<ReviewOrchestrator>();

// Factories (interfaces -> implementations)
builder.Services.AddScoped<IRepositoryProviderFactory, RepositoryProviderFactory>();
builder.Services.AddScoped<ILlmClientFactory, LlmClientFactory>();

// Retry policies
builder.Services.AddSingleton(RetryPolicies.GetDefaultRetryPolicy());

// HTTP Client for AI clients
builder.Services.AddHttpClient();


await builder.Build().RunAsync();