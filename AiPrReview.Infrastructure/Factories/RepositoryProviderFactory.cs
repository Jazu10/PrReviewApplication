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

    public IRepositoryProvider Create(ProviderConfig config, string repoName)
    {
        return config.Provider switch
        {
            ProviderType.GitHub => CreateGitHubProvider(config, repoName),
            ProviderType.AzureDevOps => CreateAzureDevOpsProvider(config, repoName),
            _ => throw new NotSupportedException($"Provider {config.Provider} not supported.")
        };
    }

    private IRepositoryProvider CreateGitHubProvider(ProviderConfig config, string repoName)
    {
        var (owner, repo) = ParseGitHubRepoName(repoName);
        var logger = _serviceProvider.GetRequiredService<ILogger<GitHubRepositoryProvider>>();
        return new GitHubRepositoryProvider(config.ApiUrl, config.AccessToken, owner, repo, logger);
    }

    private IRepositoryProvider CreateAzureDevOpsProvider(ProviderConfig config, string repoName)
    {
        var (organization, project, repository) = ParseAzureDevOpsRepoName(repoName);
        // For Azure DevOps, the ApiUrl might be "https://dev.azure.com/{organization}" or a custom base URL.
        // We'll pass the ApiUrl as is; the provider will construct the full endpoint.
        var logger = _serviceProvider.GetRequiredService<ILogger<AzureDevOpsRepositoryProvider>>();
        return new AzureDevOpsRepositoryProvider(config.ApiUrl, organization, project, repository, config.AccessToken, logger);
    }

    private static (string owner, string repo) ParseGitHubRepoName(string repoName)
    {
        var parts = repoName.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException($"GitHub repository name must be in format 'owner/repo'. Actual: {repoName}");
        return (parts[0], parts[1]);
    }

    private static (string organization, string project, string repository) ParseAzureDevOpsRepoName(string repoName)
    {
        var parts = repoName.Split('/');
        if (parts.Length != 3)
            throw new ArgumentException($"Azure DevOps repository name must be in format 'organization/project/repository'. Actual: {repoName}");
        return (parts[0], parts[1], parts[2]);
    }
}