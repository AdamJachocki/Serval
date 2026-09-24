namespace Serval.Systemd;

internal static class EnvironmentReadLimits
{
    internal const int MaxSourceBytes = 1_048_576;
    internal const int MaxTotalSourceBytes = 4_194_304;
    internal const int MaxSources = 65;
    internal const int MaxLogicalRecordBytes = 65_536;
    internal const int MaxAssignments = 16_384;
    internal const int MaxPathBytes = 4_096;
    internal const int MaxConfigurationPaths = 129;
    internal static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(5);
}
