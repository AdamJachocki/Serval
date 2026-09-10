using Serval.Domain.Services;
using Serval.Systemd.DBus;

namespace Serval.Systemd;

// Id and Names are authoritative; never follow alias chains or infer identity from paths.
internal sealed class SystemdServiceIdentity
{
    private readonly HashSet<SystemServiceId> _names;

    internal SystemdServiceIdentity(SystemdUnitProperties properties)
    {
        Id = ValidateName(properties.Id);
        _names = properties.Names.Select(ValidateName).ToHashSet();
        if (IsTemplate(Id.Value) || !_names.Contains(Id) ||
            _names.Any(name => IsTemplate(name.Value) ||
                !string.Equals(InstancePart(name.Value), InstancePart(Id.Value), StringComparison.Ordinal)))
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
        }

        Names = Array.AsReadOnly(_names.OrderBy(name => name.Value, StringComparer.Ordinal).ToArray());
    }

    internal SystemServiceId Id { get; }
    internal IReadOnlyList<SystemServiceId> Names { get; }

    internal void RequireName(string name)
    {
        if (!_names.Contains(ValidateName(name)))
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
        }
    }

    internal static bool IsTemplate(string name) => InstancePart(name) == string.Empty;

    private static string? InstancePart(string name)
    {
        var separator = name.IndexOf('@');
        return separator < 0 ? null : name[(separator + 1)..^".service".Length];
    }

    internal static SystemServiceId ValidateName(string name)
    {
        try
        {
            return new SystemServiceId(name);
        }
        catch (ArgumentException)
        {
            throw new SystemdDbusException(SystemdDbusFailureKind.MalformedReply);
        }
    }
}
