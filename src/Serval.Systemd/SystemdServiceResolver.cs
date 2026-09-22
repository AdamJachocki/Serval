using Serval.Domain.Services;
using Serval.Systemd.DBus;
using static Serval.Systemd.SystemdServiceIdentity;

namespace Serval.Systemd;

internal static class SystemdServiceResolver
{
    internal static async Task<ResolvedSystemdService?> ResolveOnceAsync(
        ISystemdDbusTransport transport,
        SystemServiceId requested,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SystemdListedUnit> units;
        try
        {
            units = await transport.ListUnitsByNamesAsync([requested], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
            exception.RemoteErrorName == "org.freedesktop.systemd1.NoSuchUnit")
        {
            return null;
        }

        if (units.Count == 0)
            return null;
        if (units.Count != 1 || IsTemplate(units[0].Name))
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);

        SystemdUnitProperties properties;
        try
        {
            properties = await transport.ReadUnitPropertiesAsync(units[0].Unit, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SystemdDbusException exception) when (
            exception.FailureKind == SystemdDbusFailureKind.RemoteError &&
            exception.RemoteErrorName is "org.freedesktop.systemd1.NoSuchUnit" or
                "org.freedesktop.DBus.Error.UnknownObject")
        {
            return null;
        }

        var identity = new SystemdServiceIdentity(properties);
        identity.RequireName(requested.Value);
        identity.RequireName(units[0].Name);
        return properties.LoadState == "not-found"
            ? null
            : new ResolvedSystemdService(units[0].Unit, identity, properties);
    }
}

internal sealed record ResolvedSystemdService(
    SystemdUnitReference Unit,
    SystemdServiceIdentity Identity,
    SystemdUnitProperties Properties);
