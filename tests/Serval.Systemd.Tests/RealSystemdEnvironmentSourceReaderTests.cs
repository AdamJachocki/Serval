using System.Security.Cryptography;
using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd.DBus;
using Serval.Systemd.Tests.DBus;
using Xunit;

namespace Serval.Systemd.Tests;

public sealed class RealSystemdEnvironmentSourceReaderTests
{
    public static bool IsEnabled => RealSystemdDbusTransportTests.IsEnabled;

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task ReadsManagerOrderedRepeatedRuntimeSourcesAndPreservesFixtureBytes()
    {
        var prefix = RequiredPrefix();
        var canonicalName = prefix + "-source@sample.service";
        var aliasName = prefix + "-source-alias@sample.service";
        var sourcePaths = new[]
        {
            "/run/systemd/system/" + prefix + "-source-sample-one.env",
            "/run/systemd/system/" + prefix + "-source-sample-two.env",
        };
        var before = sourcePaths.ToDictionary(
            path => path,
            path => File.ReadAllBytes(path),
            StringComparer.Ordinal);
        try
        {
            var reader = new SystemdEnvironmentSourceReader();
            using var canonical = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
                await reader.ReadAsync(
                    new SystemServiceId(canonicalName),
                    TestContext.Current.CancellationToken));
            using var alias = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
                await reader.ReadAsync(
                    new SystemServiceId(aliasName),
                    TestContext.Current.CancellationToken));

            Assert.Equal(canonicalName, canonical.CanonicalServiceId.Value);
            Assert.Equal(canonicalName, alias.CanonicalServiceId.Value);
            Assert.Single(canonical.ManagerSource.Variables);
            Assert.Equal([1, 2, 3, 4], canonical.FileSources.Select(source => source.SourceId));
            Assert.Equal([false, true, false, false], canonical.FileSources.Select(source => source.IsOptional));
            Assert.Equal([false, true, false, false], canonical.FileSources.Select(source => source.IsMissing));
            Assert.Equal(canonical.FileSources.Select(source => source.IsMissing),
                alias.FileSources.Select(source => source.IsMissing));
            Assert.True(canonical.FileSources[0].Reveal().SequenceEqual(before[sourcePaths[0]]),
                "Source occurrence 1 did not match its fixed fixture.");
            Assert.True(canonical.FileSources[2].Reveal().SequenceEqual(before[sourcePaths[1]]),
                "Source occurrence 3 did not match its fixed fixture.");
            Assert.True(canonical.FileSources[3].Reveal().SequenceEqual(before[sourcePaths[0]]),
                "Repeated source occurrence did not preserve manager order.");
            foreach (var path in sourcePaths)
            {
                var after = File.ReadAllBytes(path);
                try
                {
                    Assert.True(before[path].AsSpan().SequenceEqual(after),
                        "A disposable source fixture changed during reading.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(after);
                }
            }
        }
        finally
        {
            foreach (var bytes in before.Values)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task FailsClosedForRequiredMissingUnsupportedAndProtectedTargets()
    {
        var prefix = RequiredPrefix();
        var reader = new SystemdEnvironmentSourceReader();

        var missing = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(prefix + "-source-required-missing.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.SourceUnavailable, missing.Code);
        Assert.Equal(1, missing.SourceId);

        var unsupported = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(prefix + "-source-unsupported.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.UnsupportedConfiguration, unsupported.Code);
        Assert.Equal(EnvironmentUnsupportedReason.UnsetEnvironment, unsupported.Reason);

        var protectedResult = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("systemd-journald.service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, protectedResult.Code);

        var instanceId = prefix["serval-enumeration-test-".Length..];
        var privilegedResult = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("serval-agent@" + instanceId + ".service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, privilegedResult.Code);

        var privilegedAliasResult = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId("serval-exclusion-alias@" + instanceId + ".service"),
                TestContext.Current.CancellationToken));
        Assert.Equal(EnvironmentReadFailureCode.ProtectedTarget, privilegedAliasResult.Code);

        using var lookalike = Assert.IsType<SystemdEnvironmentSourceReadResult.Success>(
            await reader.ReadAsync(
                new SystemServiceId("serval-agent-helper@" + instanceId + ".service"),
                TestContext.Current.CancellationToken));
        Assert.Equal("serval-agent-helper@" + instanceId + ".service", lookalike.CanonicalServiceId.Value);
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task DetectsConfigurationPathDisappearanceDuringObservation()
    {
        var prefix = RequiredPrefix();
        var name = prefix + "-source-disappear.service";
        var path = "/run/systemd/system/" + name;
        var files = new DisappearingFixtureFileAccess(path);
        var reader = new SystemdEnvironmentSourceReader(
            async token => await SystemdDbusTransport.ConnectAsync(
                EnvironmentReadLimits.OperationDeadline,
                token).ConfigureAwait(false),
            files,
            TimeProvider.System);

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(name),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.True(files.Removed);
        Assert.False(File.Exists(path));
    }

    [Fact(Skip = "Requires real systemd and disposable source-reader fixtures.", SkipUnless = nameof(IsEnabled))]
    public async Task MapsManagerDisappearanceAfterRealInitialObservation()
    {
        var name = RequiredPrefix() + "-source@sample.service";
        DisappearingEnvironmentTransport? wrapper = null;
        var reader = new SystemdEnvironmentSourceReader(
            async token =>
            {
                var transport = await SystemdDbusTransport.ConnectAsync(
                    EnvironmentReadLimits.OperationDeadline,
                    token).ConfigureAwait(false);
                wrapper = new DisappearingEnvironmentTransport(transport);
                return wrapper;
            },
            new LinuxSystemdSourceFileAccess(),
            TimeProvider.System);

        var failure = Assert.IsType<SystemdEnvironmentSourceReadResult.Failure>(
            await reader.ReadAsync(
                new SystemServiceId(name),
                TestContext.Current.CancellationToken));

        Assert.Equal(EnvironmentReadFailureCode.InconsistentSnapshot, failure.Code);
        Assert.Equal(2, wrapper?.EnvironmentReads);
    }

    private static string RequiredPrefix()
    {
        var prefix = Environment.GetEnvironmentVariable("SERVAL_ENUMERATION_FIXTURE_PREFIX");
        Assert.NotNull(prefix);
        Assert.Matches("^serval-enumeration-test-[a-f0-9-]+$", prefix);
        return prefix;
    }

    private sealed class DisappearingFixtureFileAccess(string fixturePath) : ISystemdSourceFileAccess
    {
        private readonly LinuxSystemdSourceFileAccess _inner = new();
        public bool Removed { get; private set; }

        public async ValueTask<SystemdFileObservation> ObserveAsync(
            string path,
            bool readContent,
            bool allowMissing,
            CancellationToken cancellationToken,
            int maximumContentBytes = EnvironmentReadLimits.MaxSourceBytes)
        {
            var observation = await _inner.ObserveAsync(
                path, readContent, allowMissing, cancellationToken, maximumContentBytes)
                .ConfigureAwait(false);
            try
            {
                if (!Removed && string.Equals(path, fixturePath, StringComparison.Ordinal))
                {
                    File.Delete(fixturePath);
                    Removed = true;
                }
                return observation;
            }
            catch
            {
                observation.Dispose();
                throw;
            }
        }

        public ValueTask<bool> IsStableAsync(
            SystemdFileObservation observation,
            CancellationToken cancellationToken) =>
            _inner.IsStableAsync(observation, cancellationToken);
    }

    private sealed class DisappearingEnvironmentTransport(ISystemdDbusTransport inner)
        : ISystemdDbusTransport
    {
        public int EnvironmentReads { get; private set; }

        public Task<string> GetManagerVersionAsync(CancellationToken cancellationToken) =>
            inner.GetManagerVersionAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdUnitFileEntry>> ListUnitFilesAsync(
            CancellationToken cancellationToken) => inner.ListUnitFilesAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdListedUnit>> ListServiceUnitsAsync(
            CancellationToken cancellationToken) => inner.ListServiceUnitsAsync(cancellationToken);

        public Task<IReadOnlyList<SystemdListedUnit>> ListUnitsByNamesAsync(
            IReadOnlyCollection<SystemServiceId> serviceIds,
            CancellationToken cancellationToken) => inner.ListUnitsByNamesAsync(serviceIds, cancellationToken);

        public Task<SystemdUnitProperties> ReadUnitPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken) => inner.ReadUnitPropertiesAsync(unit, cancellationToken);

        public Task<SystemdEnvironmentProperties> ReadEnvironmentPropertiesAsync(
            SystemdUnitReference unit,
            CancellationToken cancellationToken)
        {
            EnvironmentReads++;
            if (EnvironmentReads == 2)
                throw new SystemdDbusException(
                    SystemdDbusFailureKind.RemoteError,
                    "org.freedesktop.DBus.Error.UnknownObject");
            return inner.ReadEnvironmentPropertiesAsync(unit, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
