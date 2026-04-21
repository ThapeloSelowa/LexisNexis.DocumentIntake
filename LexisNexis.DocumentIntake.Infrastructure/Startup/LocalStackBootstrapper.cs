using Serilog;
using System.Diagnostics;

namespace LexisNexis.DocumentIntake.Infrastructure.Startup;

/// <summary>
/// In Development, automatically starts LocalStack via docker compose so developers
/// don't have to remember to run it manually before starting the API.
/// Idempotent — if LocalStack is already running, docker compose up -d is a no-op.
/// </summary>
public static class LocalStackBootstrapper
{
    public static async Task EnsureRunningAsync(CancellationToken ct = default)
    {
        var composeFile = FindDockerComposeFile();
        if (composeFile is null)
        {
            Log.Warning("docker-compose.yml not found — skipping LocalStack auto-start.");
            return;
        }

        Log.Information("Starting LocalStack via docker compose (compose file: {File})...", composeFile);

        try
        {
            using var processCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            processCts.CancelAfter(TimeSpan.FromSeconds(30));

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "compose up localstack -d",
                WorkingDirectory = Path.GetDirectoryName(composeFile)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                Log.Warning("Could not launch docker — is Docker Desktop running?");
                return;
            }

            // Read streams concurrently with WaitForExitAsync to avoid pipe-buffer deadlock
            // (docker pulls the image on first run and produces a lot of stdout output)
            var stdoutTask = process.StandardOutput.ReadToEndAsync(processCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(processCts.Token);
            await process.WaitForExitAsync(processCts.Token);
            await Task.WhenAll(stdoutTask, stderrTask);

            Log.Information("docker compose exited with code {Code}. stdout={Stdout} stderr={Stderr}",
                process.ExitCode,
                stdoutTask.Result.Trim(),
                stderrTask.Result.Trim());

            if (process.ExitCode != 0)
            {
                Log.Warning("docker compose up failed (exit code {Code}) — start LocalStack manually: docker compose up localstack -d", process.ExitCode);
                return;
            }

            // Poll the LocalStack health endpoint until it's ready (max 30s)
            await WaitForHealthyAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Warning("docker compose up timed out after 30s — LocalStack may not be running. Start it manually: docker compose up localstack -d");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not auto-start LocalStack ({ExType}) — start it manually with: docker compose up localstack -d", ex.GetType().Name);
        }
    }

    private static async Task WaitForHealthyAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                var response = await http.GetAsync("http://localhost:4566/_localstack/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    Log.Information("LocalStack is ready (health check passed on attempt {Attempt}).", attempt);
                    return;
                }

                Log.Debug("LocalStack health check attempt {Attempt}/30 — HTTP {Status}.", attempt, (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Debug("LocalStack health check attempt {Attempt}/30 — {Error}.", attempt, ex.Message);
            }

            await Task.Delay(1000, ct);
        }

        Log.Warning("LocalStack did not become healthy within 30s — S3 operations will fail until it starts. Run: docker compose up localstack -d");
    }

    private static string? FindDockerComposeFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docker-compose.yml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
