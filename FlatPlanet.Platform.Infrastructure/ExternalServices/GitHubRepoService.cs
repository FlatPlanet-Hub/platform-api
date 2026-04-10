using System.Text;
using Microsoft.Extensions.Options;
using Octokit;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Configuration;
using DomainProject = FlatPlanet.Platform.Domain.Entities.Project;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class GitHubRepoService : IGitHubRepoService
{
    private readonly IProjectRepository _projectRepo;
    private readonly IDbProxyService _dbProxy;
    private readonly GitHubSettings _settings;

    public GitHubRepoService(
        IProjectRepository projectRepo,
        IDbProxyService dbProxy,
        IOptions<GitHubSettings> settings)
    {
        _projectRepo = projectRepo;
        _dbProxy = dbProxy;
        _settings = settings.Value;
    }

    // ── Service token client (used for all operations) ────────────────────────

    private GitHubClient GetServiceClient() => new(new ProductHeaderValue("FlatPlanetHub"))
    {
        Credentials = new Credentials(_settings.ServiceToken)
    };

    private static (string Owner, string RepoName) ParseRepo(string gitHubRepo)
    {
        var parts = gitHubRepo.Split('/', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new InvalidOperationException(
                $"Invalid github_repo format '{gitHubRepo}'. Expected 'owner/repo'.");
        return (parts[0], parts[1]);
    }

    // ── IGitHubRepoService ────────────────────────────────────────────────────

    public async Task<(string RepoFullName, string RepoLink)> CreateRepoAsync(string repoName)
    {
        var client = GetServiceClient();
        var repo = await client.Repository.Create(_settings.OrgName, new NewRepository(repoName)
        {
            Private     = true,
            AutoInit    = true,
            Description = $"FlatPlanet project: {repoName}"
        });
        return ($"{_settings.OrgName}/{repoName}", repo.HtmlUrl);
    }

    public async Task PushClaudeMdAsync(string repoFullName, string branch, string content)
    {
        var client = GetServiceClient();
        var (owner, repoName) = ParseRepo(repoFullName);

        try
        {
            var existing = await client.Repository.Content.GetAllContents(owner, repoName, "CLAUDE.md");
            var sha = existing.FirstOrDefault()?.Sha;
            await client.Repository.Content.UpdateFile(owner, repoName, "CLAUDE.md",
                new UpdateFileRequest("chore: update CLAUDE.md", content, sha, branch));
        }
        catch (NotFoundException)
        {
            await client.Repository.Content.CreateFile(owner, repoName, "CLAUDE.md",
                new CreateFileRequest("chore: add CLAUDE.md", content, branch));
        }
    }

    public async Task SeedProjectFilesAsync(DomainProject project)
    {
        if (string.IsNullOrWhiteSpace(project.GitHubRepo)) return;

        var client = GetServiceClient();
        var (owner, repoName) = ParseRepo(project.GitHubRepo);

        // .gitignore
        try
        {
            await client.Repository.Content.GetAllContents(owner, repoName, ".gitignore");
        }
        catch (NotFoundException)
        {
            await client.Repository.Content.CreateFile(owner, repoName, ".gitignore",
                new CreateFileRequest("chore: seed .gitignore", BuildGitignore(), "main"));
        }

        // GitHub Actions CI/CD workflow
        const string workflowPath = ".github/workflows/ci.yml";
        try
        {
            await client.Repository.Content.GetAllContents(owner, repoName, workflowPath);
        }
        catch (NotFoundException)
        {
            var workflow = BuildWorkflow(project);
            await client.Repository.Content.CreateFile(owner, repoName, workflowPath,
                new CreateFileRequest("ci: add GitHub Actions workflow", workflow, "main"));
        }
    }

    public async Task SyncDataDictionaryAsync(Guid projectId, string schema)
    {
        var project = await _projectRepo.GetByIdAsync(projectId);
        if (project is null || string.IsNullOrWhiteSpace(project.GitHubRepo)) return;

        var client = GetServiceClient();
        var (owner, repoName) = ParseRepo(project.GitHubRepo);

        var tables = (await _dbProxy.GetTablesAsync(schema)).ToList();
        var allColumns = (await _dbProxy.GetColumnsAsync(schema)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Data Dictionary");
        sb.AppendLine();
        sb.AppendLine($"_Auto-generated. Schema: `{schema}`  ");
        sb.AppendLine($"Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC_");
        sb.AppendLine();

        foreach (var table in tables.OrderBy(t => t.TableName))
        {
            sb.AppendLine($"## {table.TableName}");
            sb.AppendLine();
            sb.AppendLine("| Column | Type | Nullable | Default |");
            sb.AppendLine("|--------|------|----------|---------|");

            var cols = allColumns.Where(c => c.TableName == table.TableName).OrderBy(c => c.OrdinalPosition);
            foreach (var col in cols)
                sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {(col.IsNullable ? "Yes" : "No")} | {col.ColumnDefault ?? "-"} |");

            sb.AppendLine();
        }

        var markdown = sb.ToString();

        string? existingSha = null;
        try
        {
            var existing = await client.Repository.Content.GetAllContents(owner, repoName, "DATA_DICTIONARY.md");
            existingSha = existing.FirstOrDefault()?.Sha;
        }
        catch (NotFoundException) { }

        if (existingSha is null)
        {
            await client.Repository.Content.CreateFile(owner, repoName, "DATA_DICTIONARY.md",
                new CreateFileRequest("docs: initialise DATA_DICTIONARY.md", markdown, "main"));
        }
        else
        {
            await client.Repository.Content.UpdateFile(owner, repoName, "DATA_DICTIONARY.md",
                new UpdateFileRequest("docs: sync DATA_DICTIONARY.md", markdown, existingSha, "main"));
        }
    }

    public async Task InviteCollaboratorAsync(string repo, string githubUsername, string permission)
    {
        var client = GetServiceClient();
        var (owner, repoName) = ParseRepo(repo);

        var normalizedPermission = permission.ToLowerInvariant() switch
        {
            "admin" => "admin",
            "push" => "push",
            _ => "pull"
        };

        await client.Repository.Collaborator.Add(owner, repoName,
            githubUsername, new CollaboratorRequest(normalizedPermission));
    }

    public async Task RemoveCollaboratorAsync(string repo, string githubUsername)
    {
        var client = GetServiceClient();
        var (owner, repoName) = ParseRepo(repo);
        await client.Repository.Collaborator.Delete(owner, repoName, githubUsername);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildWorkflow(DomainProject project)
    {
        var type = project.ProjectType.ToLowerInvariant();
        return type switch
        {
            "frontend" => BuildFrontendWorkflow(),
            "backend"  => BuildBackendWorkflow(project.AppSlug ?? project.SchemaName),
            _          => BuildFullstackWorkflow(project.AppSlug ?? project.SchemaName) // fullstack + database
        };
    }

    private static string BuildFrontendWorkflow() => """
        name: CI

        on:
          push:
            branches: [ main ]
          pull_request:
            branches: [ main ]

        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4

              - name: Setup Node.js
                uses: actions/setup-node@v4
                with:
                  node-version: '20'
                  cache: 'npm'

              - name: Install dependencies
                run: npm ci

              - name: Build
                run: npm run build

              - name: Test
                run: npm test --if-present
        """;

    private static string BuildBackendWorkflow(string appSlug) => """
        name: Build, Test & Deploy

        on:
          push:
            branches: [ main ]
          pull_request:
            branches: [ main ]

        jobs:
          build-test-deploy:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4

              - name: Setup .NET
                uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: '10.0.x'

              - name: Restore
                run: dotnet restore

              - name: Build
                run: dotnet build --configuration Release --no-restore

              - name: Test
                run: dotnet test --configuration Release --no-build --no-restore

              - name: Publish
                if: github.ref == 'refs/heads/main'
                run: dotnet publish --configuration Release --output ./publish

              - name: Deploy to Azure App Service
                if: github.ref == 'refs/heads/main'
                uses: azure/webapps-deploy@v3
                with:
                  app-name: __APP_SLUG__
                  publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
                  package: ./publish
        """.Replace("__APP_SLUG__", appSlug);

    private static string BuildFullstackWorkflow(string appSlug) => """
        name: Build, Test & Deploy

        on:
          push:
            branches: [ main ]
          pull_request:
            branches: [ main ]

        jobs:
          frontend:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4

              - name: Setup Node.js
                uses: actions/setup-node@v4
                with:
                  node-version: '20'
                  cache: 'npm'

              - name: Install dependencies
                run: npm ci --if-present

              - name: Build frontend
                run: npm run build --if-present

              - name: Test frontend
                run: npm test --if-present

          backend:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4

              - name: Setup .NET
                uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: '10.0.x'

              - name: Restore
                run: dotnet restore --ignore-failed-sources

              - name: Build
                run: dotnet build --configuration Release --no-restore

              - name: Test
                run: dotnet test --configuration Release --no-build --no-restore

              - name: Publish
                if: github.ref == 'refs/heads/main'
                run: dotnet publish --configuration Release --output ./publish

              - name: Deploy to Azure App Service
                if: github.ref == 'refs/heads/main'
                uses: azure/webapps-deploy@v3
                with:
                  app-name: __APP_SLUG__
                  publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
                  package: ./publish
        """.Replace("__APP_SLUG__", appSlug);

    private static string BuildGitignore() => """
        # Dependencies
        node_modules/
        .pnp
        .pnp.js

        # Build outputs
        dist/
        build/
        .next/
        out/

        # Environment variables
        .env
        .env.local
        .env.*.local

        # IDE
        .vscode/
        .idea/
        *.suo
        *.user

        # OS
        .DS_Store
        Thumbs.db

        # Logs
        *.log
        npm-debug.log*
        """;
}
