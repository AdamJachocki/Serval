using System.Globalization;
using Serval.Systemd.DBus;

namespace Serval.Systemd;

internal static class SystemdCompatibility
{
    internal static void ValidateVersion(string version)
    {
        var length = 0;
        while (length < version.Length && char.IsAsciiDigit(version[length]))
        {
            length++;
        }

        if (length == 0 || !int.TryParse(version.AsSpan(0, length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var major))
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.IncompatibleReply);
        }

        if (major < 249)
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.UnsupportedVersion);
        }
    }
}
