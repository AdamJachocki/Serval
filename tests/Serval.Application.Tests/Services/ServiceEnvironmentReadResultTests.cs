using System.Text.Json;
using Serval.Application.Services;
using Serval.Domain.Services;
using Xunit;

namespace Serval.Application.Tests.Services;

public sealed class ServiceEnvironmentReadResultTests
{
    [Fact]
    public void EmptyEnvironmentIsACompleteSuccess()
    {
        var result = new ServiceEnvironmentReadResult.Success(new SystemServiceId("example.service"), [], [], new EnvironmentValues([]));
        Assert.Equal(EnvironmentModelScope.SupportedDeclarations, result.Scope);
        Assert.Empty(result.Sources);
        Assert.Empty(result.Variables);
    }

    [Theory]
    [InlineData(EnvironmentReadFailureCode.NotFound)]
    [InlineData(EnvironmentReadFailureCode.ProtectedTarget)]
    [InlineData(EnvironmentReadFailureCode.InvalidSource)]
    [InlineData(EnvironmentReadFailureCode.SourceUnavailable)]
    [InlineData(EnvironmentReadFailureCode.InconsistentSnapshot)]
    [InlineData(EnvironmentReadFailureCode.LimitExceeded)]
    [InlineData(EnvironmentReadFailureCode.Timeout)]
    [InlineData(EnvironmentReadFailureCode.TransportError)]
    public void FailuresAreDistinctAndCannotContainValues(EnvironmentReadFailureCode code)
    {
        ServiceEnvironmentReadResult result = new ServiceEnvironmentReadResult.Failure(code);
        Assert.Equal(code, Assert.IsType<ServiceEnvironmentReadResult.Failure>(result).Code);
        Assert.Equal(["Code", "Reason", "SourceId"], result.GetType().GetProperties().Select(property => property.Name));
    }

    [Fact]
    public void UnsupportedFailureRequiresOnlyTypedDiagnostics()
    {
        var result = new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.UnsupportedConfiguration,
            EnvironmentUnsupportedReason.UnsetEnvironment, 0);
        Assert.Equal(EnvironmentUnsupportedReason.UnsetEnvironment, result.Reason);
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.UnsupportedConfiguration));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.NotFound, EnvironmentUnsupportedReason.UnsetEnvironment));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceEnvironmentReadResult.Failure((EnvironmentReadFailureCode)999));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.UnsupportedConfiguration, (EnvironmentUnsupportedReason)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceEnvironmentReadResult.Failure(EnvironmentReadFailureCode.InvalidSource, sourceId: -1));
    }

    [Fact]
    public void IncompleteOrAmbiguousMetadataCannotBecomeSuccess()
    {
        var id = new SystemServiceId("example.service");
        var source = new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment);
        var variable = new EnvironmentVariableMetadata("SYNTHETIC", 0);
        var values = CreateValues();
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [source], [variable], new EnvironmentValues([])));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [source], [], values));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [], [variable], values));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [source, source], [variable], values));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [source], [variable, variable], values));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id,
            [new EnvironmentSourceMetadata(0, EnvironmentSourceKind.EnvironmentFile, true, true)], [variable], values));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [null!], [], new EnvironmentValues([])));
        Assert.Throws<ArgumentException>(() => new ServiceEnvironmentReadResult.Success(id, [source], [null!], values));
    }

    [Fact]
    public void CollectionsAreCopiedAndValuesRequireExplicitAccess()
    {
        var secret = Guid.NewGuid().ToString("N");
        var input = new Dictionary<string, string> { ["SYNTHETIC"] = secret };
        var values = new EnvironmentValues(input);
        var sources = new List<EnvironmentSourceMetadata> { new(0, EnvironmentSourceKind.ManagerEnvironment) };
        var variables = new List<EnvironmentVariableMetadata> { new("SYNTHETIC", 0) };
        var result = new ServiceEnvironmentReadResult.Success(new SystemServiceId("example.service"), sources, variables, values);
        input.Clear();
        sources.Clear();
        variables.Clear();
        Assert.Single(result.Sources);
        Assert.Single(result.Variables);
        // Keep secret operands out of xUnit's failure diagnostics.
        var matches = string.Equals(secret, result.Values.Reveal("SYNTHETIC"), StringComparison.Ordinal);
        Assert.True(matches);
        Assert.False(JsonSerializer.Serialize(result).Contains(secret, StringComparison.Ordinal));
        Assert.False(JsonSerializer.Serialize(values).Contains(secret, StringComparison.Ordinal));
        Assert.False(result.ToString()!.Contains(secret, StringComparison.Ordinal));
        Assert.Equal("[REDACTED]", values.ToString());
        var exception = Assert.Throws<KeyNotFoundException>(() => values.Reveal(secret));
        Assert.False(exception.ToString().Contains(secret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1NAME")]
    [InlineData("NAME=VALUE")]
    [InlineData("BAD\nNAME")]
    public void InvalidNamesAreRejectedWithoutEcho(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => new EnvironmentVariableMetadata(name, 0));
        Assert.Equal("Invalid environment variable name. (Parameter 'name')", error.Message);
    }

    [Fact]
    public void InvalidSourceStatesAndDuplicateValuesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new EnvironmentSourceMetadata(0, EnvironmentSourceKind.ManagerEnvironment, true));
        Assert.Throws<ArgumentException>(() => new EnvironmentSourceMetadata(0, EnvironmentSourceKind.EnvironmentFile, false, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnvironmentSourceMetadata(-1, EnvironmentSourceKind.EnvironmentFile));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnvironmentSourceMetadata(0, (EnvironmentSourceKind)999));
        var secret = Guid.NewGuid().ToString("N");
        var error = Assert.Throws<ArgumentException>(() => new EnvironmentValues(
            [new("SYNTHETIC", secret), new("SYNTHETIC", secret)]));
        Assert.False(error.ToString().Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void ContractIsClosedAndTransportIndependent()
    {
        var method = Assert.Single(typeof(ISystemServiceEnvironmentReader).GetMethods());
        Assert.Equal(typeof(Task<ServiceEnvironmentReadResult>), method.ReturnType);
        Assert.Equal([typeof(SystemServiceId), typeof(CancellationToken)], method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(typeof(ServiceEnvironmentReadResult).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic),
            constructor => Assert.True(constructor.IsPrivate));
        Assert.All(typeof(ServiceEnvironmentReadResult).GetNestedTypes(), type => Assert.True(type.IsSealed));
        Assert.DoesNotContain(typeof(ISystemServiceEnvironmentReader).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.Contains("Systemd", StringComparison.Ordinal) || assembly.Name.Contains("DBus", StringComparison.Ordinal));
    }

    private static EnvironmentValues CreateValues() => new([new("SYNTHETIC", Guid.NewGuid().ToString("N"))]);
}
