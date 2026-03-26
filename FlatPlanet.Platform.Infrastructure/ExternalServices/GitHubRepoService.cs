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

    public async Task SeedProjectFilesAsync(DomainProject project)
    {
        if (string.IsNullOrWhiteSpace(project.GitHubRepo)) return;

        var client = GetServiceClient();
        var (owner, repoName) = ParseRepo(project.GitHubRepo);

        var files = new[]
        {
            ("DATA_DICTIONARY.md", "# Data Dictionary\n\n_No tables yet. This file is auto-updated when tables are created._\n"),
            (".gitignore", BuildGitignore())
        };

        foreach (var (path, content) in files)
        {
            try
            {
                // File already exists — skip (already seeded)
                await client.Repository.Content.GetAllContents(owner, repoName, path);
            }
            catch (NotFoundException)
            {
                // File does not exist — create it
                await client.Repository.Content.CreateFile(owner, repoName, path,
                    new CreateFileRequest($"chore: seed {path}", content, "main"));
            }
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

        # FlatPlanet — never commit these
        CLAUDE.md
        """;
}
