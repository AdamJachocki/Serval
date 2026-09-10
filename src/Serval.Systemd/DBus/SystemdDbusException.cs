namespace Serval.Systemd.DBus;

internal enum SystemdDbusFailureKind
{
    Unavailable,
    RemoteError,
    IncompatibleReply,
    MalformedReply,
    Timeout,
    UnsupportedVersion,
}

internal sealed class SystemdDbusException : Exception
{
    internal SystemdDbusException(
        SystemdDbusFailureKind failureKind,
        string? remoteErrorName = null)
        : base(GetMessage(failureKind))
    {
        FailureKind = failureKind;
        RemoteErrorName = remoteErrorName;
    }

    public SystemdDbusFailureKind FailureKind { get; }

    public string? RemoteErrorName { get; }

    private static string GetMessage(SystemdDbusFailureKind failureKind) =>
        failureKind switch
        {
            SystemdDbusFailureKind.UnsupportedVersion =>
                "The systemd version is below the supported baseline.",
            SystemdDbusFailureKind.Unavailable =>
                "The systemd D-Bus service is unavailable.",
            SystemdDbusFailureKind.RemoteError =>
                "The systemd D-Bus service rejected the operation.",
            SystemdDbusFailureKind.IncompatibleReply =>
                "The systemd D-Bus service returned an incompatible reply.",
            SystemdDbusFailureKind.MalformedReply =>
                "The systemd D-Bus service returned a malformed reply.",
            SystemdDbusFailureKind.Timeout =>
                "The systemd D-Bus operation exceeded its deadline.",
            _ =>
                "The systemd D-Bus operation failed.",
        };
}
