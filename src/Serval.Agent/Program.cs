using System.Runtime.InteropServices;
using Serval.Agent;
using Serval.Infrastructure.Policy;
using Serval.Systemd;

try
{
    if (!OperatingSystem.IsLinux())
    {
        throw new PlatformNotSupportedException();
    }

    var configuration = AgentConfiguration.Load();
    configuration.ValidateRuntimeDirectory();
    AgentConfiguration.ValidatePolicyLocation();
    AgentConfiguration.ValidatePamService();

    using var shutdown = new CancellationTokenSource();
    using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
    {
        context.Cancel = true;
        shutdown.Cancel();
    });
    using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
    {
        context.Cancel = true;
        shutdown.Cancel();
    });

    var identities = new LinuxAccountChecks();
    await AgentStartup.ValidateDeploymentIdentitiesAsync(configuration, identities, shutdown.Token);

    var policy = new AgentPolicyStore();
    policy.Initialize();
    File.SetUnixFileMode(AgentConfiguration.PolicyPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite);
    AgentConfiguration.ValidatePolicyLocation();

    var sessions = new AgentSessions(configuration.IdleTimeout, identities, TimeProvider.System);
    var dispatcher = new AgentDispatcher(configuration, identities, sessions,
        policy, new SystemdServiceInventory(), TimeProvider.System);
    var socket = new AgentSocketServer(configuration, dispatcher.DispatchAsync);
    await socket.RunAsync(shutdown.Token);
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch
{
    Console.Error.WriteLine("Agent startup or operation failed.");
    return 1;
}
