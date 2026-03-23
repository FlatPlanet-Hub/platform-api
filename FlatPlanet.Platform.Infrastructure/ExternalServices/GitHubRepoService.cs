using System.Text;
using Microsoft.Extensions.Options;
using Octokit;
using FlatPlanet.Platform.Application.DTOs;
using FlatPlanet.Platform.Application.DTOs.Repo;
using FlatPlanet.Platform.Application.Interfaces;
using FlatPlanet.Platform.Infrastructure.Common.Helpers;
using FlatPlanet.Platform.Infrastructure.Configuration;
// Alias app DTOs that clash with Octokit type names
using AppTreeResponse = FlatPlanet.Platform.Application.DTOs.Repo.TreeResponse;
using AppDeleteFileRequest = FlatPlanet.Platform.Application.DTOs.Repo.DeleteFileRequest;
using AppTreeItemDto = FlatPlanet.Platform.Application.DTOs.Repo.TreeItemDto;

namespace FlatPlanet.Platform.Infrastructure.ExternalServices;

public sealed class GitHubRepoService : IGitHubRepoService
{
    private readonly IUserRepository _userRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IAuditService _audit;
    private readonly IDbProxyService _dbProxy;
    private readonly EncryptionSettings _encryption;

    public GitHubRepoService(
        IUserRepository userRepo,
        IProjectRepository projectRepo,
        IAuditService audit,
        IDbProxyService dbProxy,
        IOptions<EncryptionSettings> encryption)
    {
        _userRepo = userRepo;
        _projectRepo = projectRepo;
        _audit = audit;
        _dbProxy = dbProxy;
        _encryption = encryption.Value;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(GitHubClient Client, string Owner, string RepoName)> GetClientAsync(
        Guid userId, Guid projectId, bool requireRepo = true)
    {
        var user = await _userRepo.GetByIdAsync(userId)
            ?? throw new InvalidOperationException("User not found.");

        if (string.IsNullOrWhiteSpace(user.GitHubAccessToken))
            throw new InvalidOperationException("No GitHub access token on file. Please re-authenticate.");

        var token = EncryptionHelper.Decrypt(user.GitHubAccessToken, _encryption.Key);
        var client = new GitHubClient(new ProductHeaderValue("FlatPlanetHub"))
        {
            Credentials = new Credentials(token)
        };

        if (!requireRepo)
            return (client, string.Empty, string.Empty);

        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new InvalidOperationException("Project not found.");

        if (string.IsNullOrWhiteSpace(project.GitHubRepo))
            throw new InvalidOperationException("No GitHub repository linked to this project.");

        var parts = project.GitHubRepo.Split('/', 2);
        if (parts.Length != 2
            || string.IsNullOrWhiteSpace(parts[0])
            || string.IsNullOrWhiteSpace(parts[1]))
            throw new InvalidOperationException(
                $"Invalid github_repo format '{project.GitHubRepo}'. Expected 'owner/repo'.");

        return (client, parts[0], parts[1]);
    }

    // ── Repo Management ──────────────────────────────────────────────────────

    public async Task<RepoResponse> CreateRepoAsync(Guid userId, Guid projectId, CreateRepoRequest request)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new InvalidOperationException("Project not found.");

        if (!string.IsNullOrWhiteSpace(project.GitHubRepo))
            throw new InvalidOperationException("A GitHub repository is already linked to this project.");

        var (client, _, _) = await GetClientAsync(userId, projectId, requireRepo: false);

        Repository repo;

        var newRepo = new NewRepository(request.RepoName)
        {
            Description = request.Description,
            Private = request.IsPrivate,
            AutoInit = false
        };

        if (!string.IsNullOrWhiteSpace(request.Org))
        {
            try
            {
                repo = await client.Repository.Create(request.Org, newRepo);
            }
            catch (ForbiddenException)
            {
                throw new UnauthorizedAccessException(
                    $"You do not have permission to create repositories under the '{request.Org}' organization. " +
                    "Please ask an admin for assistance.");
            }
        }
        else
        {
            repo = await client.Repository.Create(newRepo);
        }

        // Seed initial files as a single commit
        await SeedInitialFilesAsync(client, repo.Owner.Login, repo.Name, project.Name, project.Description);

        // Persist repo link on the project
        project.GitHubRepo = repo.FullName;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);

        await _audit.LogAsync(userId, projectId, "repo.create", "project",
            new { repo.FullName, repo.HtmlUrl });

        return new RepoResponse(repo.FullName, repo.HtmlUrl, repo.CloneUrl, repo.DefaultBranch ?? "main");
    }

    public async Task<RepoResponse> GetRepoAsync(Guid userId, Guid projectId)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);
        var repo = await client.Repository.Get(owner, repoName);
        return new RepoResponse(repo.FullName, repo.HtmlUrl, repo.CloneUrl, repo.DefaultBranch ?? "main");
    }

    public async Task DeleteRepoAsync(Guid userId, Guid projectId)
    {
        var project = await _projectRepo.GetByIdAsync(projectId)
            ?? throw new InvalidOperationException("Project not found.");

        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        await client.Repository.Delete(owner, repoName);

        project.GitHubRepo = null;
        project.UpdatedAt = DateTime.UtcNow;
        await _projectRepo.UpdateAsync(project);

        await _audit.LogAsync(userId, projectId, "repo.delete", "project",
            new { owner, repoName });
    }

    // ── File Operations ──────────────────────────────────────────────────────

    public async Task<object> GetFilesAsync(Guid userId, Guid projectId, string? path, string? branch)
    {
        ValidateFilePath(path);
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var effectivePath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        var contents = string.IsNullOrWhiteSpace(branch)
            ? await client.Repository.Content.GetAllContents(owner, repoName, effectivePath)
            : await client.Repository.Content.GetAllContentsByRef(owner, repoName, effectivePath, branch);

        if (contents.Count == 1 && contents[0].Type.Value == ContentType.File)
        {
            var f = contents[0];
            var decoded = f.Encoding == "base64"
                ? Encoding.UTF8.GetString(Convert.FromBase64String(f.Content.Replace("\n", "")))
                : f.Content;

            return new FileContentDto("file", f.Name, f.Path, decoded, f.Sha, f.Size);
        }

        var items = contents.Select(c => new FileItemDto(
            c.Name, c.Path,
            c.Type.Value == ContentType.Dir ? "directory" : "file",
            c.Type.Value == ContentType.File ? c.Size : null)).ToList();

        return new DirectoryContentDto("directory", effectivePath, items);
    }

    public async Task<AppTreeResponse> GetTreeAsync(Guid userId, Guid projectId, string? branch)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var repoInfo = await client.Repository.Get(owner, repoName);
        var ref_ = branch ?? repoInfo.DefaultBranch ?? "main";

        var tree = await client.Git.Tree.GetRecursive(owner, repoName, ref_);

        var items = tree.Tree.Select(t => new AppTreeItemDto(
            t.Path,
            t.Type.Value == TreeType.Blob ? "blob" : "tree",
            t.Type.Value == TreeType.Blob ? t.Size : null)).ToList();

        return new AppTreeResponse(tree.Sha, items);
    }

    public async Task<FileContentDto> UpsertFileAsync(Guid userId, Guid projectId, UpsertFileRequest request)
    {
        ValidateFilePath(request.Path);
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        RepositoryContentChangeSet result;

        if (string.IsNullOrWhiteSpace(request.Sha))
        {
            result = await client.Repository.Content.CreateFile(owner, repoName,
                request.Path, new CreateFileRequest(request.Message, request.Content, request.Branch));
        }
        else
        {
            result = await client.Repository.Content.UpdateFile(owner, repoName,
                request.Path, new UpdateFileRequest(request.Message, request.Content, request.Sha, request.Branch));
        }

        var content = result.Content;
        return new FileContentDto(
            "file", content.Name, content.Path,
            request.Content, content.Sha, Encoding.UTF8.GetByteCount(request.Content));
    }

    public async Task DeleteFileAsync(Guid userId, Guid projectId, AppDeleteFileRequest request)
    {
        ValidateFilePath(request.Path);
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        await client.Repository.Content.DeleteFile(owner, repoName,
            request.Path, new Octokit.DeleteFileRequest(request.Message, request.Sha, request.Branch));
    }

    // ── Commit Operations ────────────────────────────────────────────────────

    public async Task<CommitResponse> CreateMultiFileCommitAsync(
        Guid userId, Guid projectId, CreateCommitRequest request)
    {
        if (request.Files.Count == 0)
            throw new ArgumentException("At least one file is required.");

        const long maxFileSizeBytes = 50L * 1024 * 1024; // 50 MB

        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        // 1. Get current HEAD
        var branchRef = await client.Git.Reference.Get(owner, repoName, $"heads/{request.Branch}");
        var baseCommitSha = branchRef.Object.Sha;
        var baseCommit = await client.Git.Commit.Get(owner, repoName, baseCommitSha);

        // 2. Build new tree items (Tree has private setter — use .Add())
        var newTree = new NewTree { BaseTree = baseCommit.Tree.Sha };

        foreach (var file in request.Files)
        {
            if (file.Action == "delete")
            {
                newTree.Tree.Add(new NewTreeItem
                {
                    Path = file.Path,
                    Mode = "100644",
                    Type = TreeType.Blob,
                    Sha = null
                });
                continue;
            }

            var content = file.Content ?? string.Empty;
            var sizeBytes = Encoding.UTF8.GetByteCount(content);
            if (sizeBytes > maxFileSizeBytes)
                throw new InvalidOperationException($"File '{file.Path}' exceeds the 50 MB size limit.");

            var blob = await client.Git.Blob.Create(owner, repoName, new NewBlob
            {
                Content = content,
                Encoding = EncodingType.Utf8
            });

            newTree.Tree.Add(new NewTreeItem
            {
                Path = file.Path,
                Mode = "100644",
                Type = TreeType.Blob,
                Sha = blob.Sha
            });
        }

        // 3. Create tree
        var createdTree = await client.Git.Tree.Create(owner, repoName, newTree);

        // 4. Create commit
        var newCommit = await client.Git.Commit.Create(owner, repoName,
            new NewCommit(request.Message, createdTree.Sha, new[] { baseCommitSha }));

        // 5. Update branch ref
        await client.Git.Reference.Update(owner, repoName,
            $"heads/{request.Branch}", new ReferenceUpdate(newCommit.Sha));

        await _audit.LogAsync(userId, projectId, "repo.commit", "project",
            new { Branch = request.Branch, Sha = newCommit.Sha, FilesChanged = request.Files.Count });

        return new CommitResponse(
            newCommit.Sha,
            $"https://github.com/{owner}/{repoName}/commit/{newCommit.Sha}",
            request.Files.Count);
    }

    public async Task<IEnumerable<CommitSummaryDto>> ListCommitsAsync(
        Guid userId, Guid projectId, string? branch, int page, int pageSize)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var commitRequest = new CommitRequest();
        if (!string.IsNullOrWhiteSpace(branch)) commitRequest.Sha = branch;

        var options = new ApiOptions { PageCount = 1, PageSize = pageSize, StartPage = page };
        var commits = await client.Repository.Commit.GetAll(owner, repoName, commitRequest, options);

        return commits.Select(c => new CommitSummaryDto(
            c.Sha,
            c.Commit.Message,
            c.Author?.Login ?? c.Commit.Author.Name,
            c.Commit.Author.Date));
    }

    // ── Branch Operations ────────────────────────────────────────────────────

    public async Task<IEnumerable<BranchDto>> ListBranchesAsync(Guid userId, Guid projectId)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);
        var repoInfo = await client.Repository.Get(owner, repoName);
        var branches = await client.Repository.Branch.GetAll(owner, repoName);

        return branches.Select(b => new BranchDto(
            b.Name,
            b.Name == repoInfo.DefaultBranch,
            b.Commit.Sha));
    }

    public async Task<BranchDto> CreateBranchAsync(Guid userId, Guid projectId, CreateBranchRequest request)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var fromRef = await client.Git.Reference.Get(owner, repoName, $"heads/{request.FromBranch}");
        var newRef = await client.Git.Reference.Create(owner, repoName,
            new NewReference($"refs/heads/{request.Name}", fromRef.Object.Sha));

        return new BranchDto(request.Name, false, newRef.Object.Sha);
    }

    public async Task DeleteBranchAsync(Guid userId, Guid projectId, string branchName)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var repoInfo = await client.Repository.Get(owner, repoName);
        if (branchName == repoInfo.DefaultBranch)
            throw new InvalidOperationException("Cannot delete the default branch.");

        await client.Git.Reference.Delete(owner, repoName, $"heads/{branchName}");
    }

    // ── Pull Request Operations ──────────────────────────────────────────────

    public async Task<PullRequestDto> CreatePullRequestAsync(
        Guid userId, Guid projectId, CreatePullRequestRequest request)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var pr = await client.PullRequest.Create(owner, repoName,
            new NewPullRequest(request.Title, request.Head, request.Base) { Body = request.Body });

        await _audit.LogAsync(userId, projectId, "repo.pr.create", "project",
            new { pr.Number, pr.Title });

        return MapPr(pr);
    }

    public async Task<IEnumerable<PullRequestDto>> ListPullRequestsAsync(
        Guid userId, Guid projectId, string? state)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var itemState = state?.ToLowerInvariant() switch
        {
            "closed" => ItemStateFilter.Closed,
            "all" => ItemStateFilter.All,
            _ => ItemStateFilter.Open
        };

        var prs = await client.PullRequest.GetAllForRepository(owner, repoName,
            new PullRequestRequest { State = itemState });

        return prs.Select(MapPr);
    }

    public async Task<PullRequestDto> GetPullRequestAsync(Guid userId, Guid projectId, int prNumber)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);
        try
        {
            var pr = await client.PullRequest.Get(owner, repoName, prNumber);
            return MapPr(pr);
        }
        catch (NotFoundException)
        {
            throw new KeyNotFoundException($"Pull request #{prNumber} not found.");
        }
    }

    public async Task<MergeResultDto> MergePullRequestAsync(
        Guid userId, Guid projectId, int prNumber, MergePullRequestRequest request)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        var mergeMethod = request.MergeMethod.ToLowerInvariant() switch
        {
            "squash" => PullRequestMergeMethod.Squash,
            "rebase" => PullRequestMergeMethod.Rebase,
            _ => PullRequestMergeMethod.Merge
        };

        var result = await client.PullRequest.Merge(owner, repoName, prNumber,
            new MergePullRequest { MergeMethod = mergeMethod });

        await _audit.LogAsync(userId, projectId, "repo.pr.merge", "project",
            new { prNumber, result.Sha, MergeMethod = request.MergeMethod });

        return new MergeResultDto(result.Sha, result.Merged, result.Message);
    }

    // ── Collaborator Operations ──────────────────────────────────────────────

    public async Task<IEnumerable<CollaboratorDto>> ListCollaboratorsAsync(Guid userId, Guid projectId)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);
        var collabs = await client.Repository.Collaborator.GetAll(owner, repoName);

        return collabs.Select(c => new CollaboratorDto(
            c.Login, c.AvatarUrl,
            c.Permissions?.Admin == true ? "admin"
            : c.Permissions?.Push == true ? "push"
            : "pull"));
    }

    public async Task InviteCollaboratorAsync(
        Guid userId, Guid projectId, InviteCollaboratorRequest request)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

        // CollaboratorRequest takes a string permission in Octokit v14
        var permission = request.Permission.ToLowerInvariant() switch
        {
            "admin" => "admin",
            "push" => "push",
            _ => "pull"
        };

        await client.Repository.Collaborator.Add(owner, repoName,
            request.GitHubUsername, new CollaboratorRequest(permission));

        await _audit.LogAsync(userId, projectId, "repo.collaborator.invite", "project",
            new { request.GitHubUsername, request.Permission });
    }

    public async Task RemoveCollaboratorAsync(Guid userId, Guid projectId, string githubUsername)
    {
        var (client, owner, repoName) = await GetClientAsync(userId, projectId);
        await client.Repository.Collaborator.Delete(owner, repoName, githubUsername);

        await _audit.LogAsync(userId, projectId, "repo.collaborator.remove", "project",
            new { githubUsername });
    }

    // ── DATA_DICTIONARY Sync ─────────────────────────────────────────────────

    public async Task SyncDataDictionaryAsync(Guid userId, Guid projectId, string schema)
    {
        var project = await _projectRepo.GetByIdAsync(projectId);
        if (project is null || string.IsNullOrWhiteSpace(project.GitHubRepo))
            return; // No repo linked — skip silently

        var (client, owner, repoName) = await GetClientAsync(userId, projectId);

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
            {
                sb.AppendLine($"| {col.ColumnName} | {col.DataType} | {(col.IsNullable ? "Yes" : "No")} | {col.ColumnDefault ?? "-"} |");
            }

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

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void ValidateFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return; // empty = repo root, allowed

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/'))
            throw new ArgumentException($"File path must not start with '/': '{path}'");

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == ".." || segment == ".")
                throw new ArgumentException($"File path must not contain '.' or '..' segments: '{path}'");
        }
    }

    private static async Task SeedInitialFilesAsync(
        GitHubClient client, string owner, string repoName,
        string projectName, string? description)
    {
        var files = new List<(string path, string content)>
        {
            ("README.md", $"# {projectName}\n\n{description ?? string.Empty}\n"),
            ("PROJECT.md", BuildProjectMd(projectName, description)),
            ("DATA_DICTIONARY.md", "# Data Dictionary\n\n_No tables yet. This file is auto-updated when tables are created._\n"),
            (".gitignore", BuildGitignore())
        };

        // Seed via Git tree API as a single commit (repo has no base tree yet)
        var newTree = new NewTree();
        foreach (var (path, content) in files)
        {
            var blob = await client.Git.Blob.Create(owner, repoName, new NewBlob
            {
                Content = content,
                Encoding = EncodingType.Utf8
            });
            newTree.Tree.Add(new NewTreeItem { Path = path, Mode = "100644", Type = TreeType.Blob, Sha = blob.Sha });
        }

        var tree = await client.Git.Tree.Create(owner, repoName, newTree);
        var commit = await client.Git.Commit.Create(owner, repoName,
            new NewCommit("chore: initial project scaffold", tree.Sha, Array.Empty<string>()));

        await client.Git.Reference.Create(owner, repoName,
            new NewReference("refs/heads/main", commit.Sha));
    }

    private static string BuildProjectMd(string name, string? description) => $"""
        # {name}

        {description ?? "Describe your project here."}

        ## Goals

        - [ ] Define project objectives

        ## Tech Stack

        - Database: Supabase (PostgreSQL)
        - API: FlatPlanet Hub Proxy API

        ## Notes

        """;

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

    private static PullRequestDto MapPr(PullRequest pr) => new(
        pr.Number, pr.Title, pr.State.StringValue,
        pr.Head.Ref, pr.Base.Ref, pr.HtmlUrl,
        pr.User.Login, pr.CreatedAt);
}
