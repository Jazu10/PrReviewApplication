using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AiPrReview.Core.Entities;
using AiPrReview.Core.Enums;
using AiPrReview.Core.Interfaces;
using AiPrReview.Application.Interfaces;
using AiPrReview.Infrastructure.RepositoryProviders;

namespace AiPrReview.Infrastructure.Factories;

public class RepositoryProviderFactory : IRepositoryProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public RepositoryProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IRepositoryProvider Create(RepositoryConfig config)
    {
        return config.Provider switch
        {
            ProviderType.GitHub => CreateGitHubProvider(config),
            ProviderType.AzureDevOps => CreateAzureDevOpsProvider(config),
            _ => throw new NotSupportedException($"Provider {config.Provider} not supported.")
        };
    }

    private IRepositoryProvider CreateGitHubProvider(RepositoryConfig config)
    {
        var (owner, repo) = ExtractGitHubOwnerAndRepo(config.ApiUrl, config.Name);
        var logger = _serviceProvider.GetRequiredService<ILogger<GitHubRepositoryProvider>>();
        return new GitHubRepositoryProvider(config.ApiUrl, config.AccessToken, owner, repo, logger);
    }

    private IRepositoryProvider CreateAzureDevOpsProvider(RepositoryConfig config)
    {
        var organization = ExtractAzureDevOpsOrganization(config.ApiUrl);
        if (string.IsNullOrEmpty(config.ProjectName))
            throw new ArgumentException("ProjectName is required for Azure DevOps repository.");
        var logger = _serviceProvider.GetRequiredService<ILogger<AzureDevOpsRepositoryProvider>>();
        return new AzureDevOpsRepositoryProvider(organization, config.ProjectName, config.Name, config.AccessToken, logger);
    }

    private static (string owner, string repo) ExtractGitHubOwnerAndRepo(string apiUrl, string repoName)
    {
        // Expected API URL format: "https://api.github.com/repos/{owner}/{repo}"
        // If not, fallback to extracting from repoName (if stored as "owner/repo") or throw.
        var uri = new Uri(apiUrl);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals("repos", StringComparison.OrdinalIgnoreCase))
        {
            return (segments[1], repoName);
        }
        // Fallback: if repoName contains "/", treat it as owner/repo
        var nameParts = repoName.Split('/');
        if (nameParts.Length == 2)
            return (nameParts[0], nameParts[1]);
        throw new ArgumentException($"Unable to extract owner from GitHub API URL: {apiUrl}. Ensure URL contains '/repos/owner/' or store repository name as 'owner/repo'.");
    }

    private static string ExtractAzureDevOpsOrganization(string apiUrl)
    {
        var uri = new Uri(apiUrl);
        if (!uri.Host.Contains("dev.azure.com"))
            throw new NotSupportedException("Only Azure DevOps dev.azure.com URLs are supported.");
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Azure DevOps URL must contain organization.");
        return segments[0];
    }
}