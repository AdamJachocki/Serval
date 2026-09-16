namespace Serval.Systemd;

internal static class EnvironmentReadLimits
{
    internal const int MaxSourceBytes = 1_048_576;
    internal const int MaxLogicalRecordBytes = 65_536;
    internal const int MaxAssignments = 16_384;
}
