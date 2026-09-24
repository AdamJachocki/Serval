using System.Text.Json.Serialization;
using Serval.Application.Services;
using Serval.Domain.Services;

namespace Serval.Systemd;

internal abstract class SystemdEnvironmentSourceReadResult
{
    private SystemdEnvironmentSourceReadResult() { }

    internal sealed class Success : SystemdEnvironmentSourceReadResult, IDisposable
    {
        private bool _disposed;

        internal Success(
            SystemServiceId canonicalServiceId,
            LoadedEnvironmentResult.Success managerSource,
            IEnumerable<SystemdEnvironmentFileSource> fileSources)
        {
            ArgumentNullException.ThrowIfNull(canonicalServiceId);
            ArgumentNullException.ThrowIfNull(managerSource);
            ArgumentNullException.ThrowIfNull(fileSources);
            var files = fileSources.ToArray();
            if (files.Any(file => file is null) ||
                files.Where((file, index) => file.SourceId != index + 1).Any())
                throw new ArgumentException("Environment sources are not consecutive.", nameof(fileSources));

            CanonicalServiceId = canonicalServiceId;
            ManagerSource = managerSource;
            FileSources = Array.AsReadOnly(files);
        }

        public SystemServiceId CanonicalServiceId { get; }

        [JsonIgnore]
        internal LoadedEnvironmentResult.Success ManagerSource { get; }

        public IReadOnlyList<SystemdEnvironmentFileSource> FileSources { get; }

        public void Dispose()
        {
            if (_disposed)
                return;
            for (var index = FileSources.Count - 1; index >= 0; index--)
                FileSources[index].Dispose();
            ManagerSource.Dispose();
            _disposed = true;
        }

        public override string ToString() => "[REDACTED]";
    }

    internal sealed class Failure : SystemdEnvironmentSourceReadResult
    {
        internal Failure(
            EnvironmentReadFailureCode code,
            EnvironmentUnsupportedReason? reason = null,
            int? sourceId = null)
        {
            if (!Enum.IsDefined(code))
                throw new ArgumentOutOfRangeException(nameof(code));
            if ((code == EnvironmentReadFailureCode.UnsupportedConfiguration) != reason.HasValue ||
                reason is { } definedReason && !Enum.IsDefined(definedReason))
                throw new ArgumentException("Unsupported configuration requires a defined reason.", nameof(reason));
            if (sourceId < 0)
                throw new ArgumentOutOfRangeException(nameof(sourceId));
            Code = code;
            Reason = reason;
            SourceId = sourceId;
        }

        public EnvironmentReadFailureCode Code { get; }
        public EnvironmentUnsupportedReason? Reason { get; }
        public int? SourceId { get; }
    }
}

internal sealed class SystemdEnvironmentFileSource : IDisposable
{
    private readonly ClearableBytes? _content;

    internal SystemdEnvironmentFileSource(int sourceId, bool isOptional, bool isMissing, ClearableBytes? content)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        if (isMissing && !isOptional || isMissing != (content is null))
            throw new ArgumentException("Invalid environment file source state.");
        SourceId = sourceId;
        IsOptional = isOptional;
        IsMissing = isMissing;
        _content = content;
    }

    public int SourceId { get; }
    public bool IsOptional { get; }
    public bool IsMissing { get; }

    internal ReadOnlySpan<byte> Reveal()
    {
        if (_content is null)
            throw new InvalidOperationException("The source is missing.");
        return _content.Reveal();
    }

    public void Dispose() => _content?.Dispose();
    public override string ToString() => "[REDACTED]";
}
