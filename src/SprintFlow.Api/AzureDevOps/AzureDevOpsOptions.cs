namespace SprintFlow.AzureDevOps;

public sealed class AzureDevOpsOptions
{
    public string ExecutablePath { get; init; } = "az";
    public string Organization { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string Team { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 60;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Organization) && !string.IsNullOrWhiteSpace(Project) && !string.IsNullOrWhiteSpace(Team);
}

public sealed record AzureDevOpsDiagnostic(bool CliAvailable, bool ExtensionAvailable, bool Authenticated, bool Configured, string Message);
