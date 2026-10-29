using Serval.Application.Authorization;
using Serval.Application.Ipc;
using Serval.Application.Services;
using Serval.Domain.Services;
using Serval.Systemd;

namespace Serval.Agent;

/// <summary>Agent-side identity, policy, protection and audit gate for typed operations.</summary>
public sealed class AgentDispatcher(
    AgentConfiguration configuration,
    IAgentIdentityChecks identities,
    AgentSessions sessions,
    IAgentPolicyStore policy,
    ISystemServiceIdentityInventory inventory,
    TimeProvider clock,
    ISystemServiceEnvironmentReader? environmentReader = null)
{
    public async Task<AgentResult> DispatchAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ValidCorrelation(request.CorrelationId))
        {
            return Failure(request, AgentResultCode.InvalidRequest);
        }

        AgentPrincipal? verifiedActor = null;
        string? verifiedService = null;
        void Remember(AgentPrincipal principal) => verifiedActor = principal;
        void RememberService(string service) => verifiedService = service;
        try
        {
            var result = request switch
            {
                AgentRequest.Login login => await LoginAsync(login, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.Logout logout => await LogoutAsync(logout, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.ListServices list => await ListServicesAsync(list, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.InspectService inspect => await InspectServiceAsync(inspect, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.ListGrants grants => await ListGrantsAsync(grants, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.AddGrant add => await AddGrantAsync(add, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.RemoveGrant remove => await RemoveGrantAsync(remove, Remember, cancellationToken).ConfigureAwait(false),
                AgentRequest.ListEnvironment list => await ReadEnvironmentAsync(list, list.Session, list.Service, null, Remember, RememberService, cancellationToken).ConfigureAwait(false),
                AgentRequest.RevealEnvironmentVariable reveal => await ReadEnvironmentAsync(reveal, reveal.Session, reveal.Service, reveal.Name, Remember, RememberService, cancellationToken).ConfigureAwait(false),
                _ => throw new AgentProtocolException(),
            };
            if (result.Code == AgentResultCode.Success)
            {
                switch (request)
                {
                    case AgentRequest.ListServices list:
                        sessions.RecordAcceptedActivity(list.Session);
                        break;
                    case AgentRequest.InspectService inspect:
                        sessions.RecordAcceptedActivity(inspect.Session);
                        break;
                    case AgentRequest.ListGrants grants:
                        sessions.RecordAcceptedActivity(grants.Session);
                        break;
                    case AgentRequest.AddGrant add:
                        sessions.RecordAcceptedActivity(add.Session);
                        break;
                    case AgentRequest.RemoveGrant remove:
                        sessions.RecordAcceptedActivity(remove.Session);
                        break;
                    case AgentRequest.ListEnvironment list:
                        sessions.RecordAcceptedActivity(list.Session);
                        break;
                    case AgentRequest.RevealEnvironmentVariable reveal:
                        sessions.RecordAcceptedActivity(reveal.Session);
                        break;
                }
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (request is AgentRequest.ListEnvironment or AgentRequest.RevealEnvironmentVariable)
            {
                try
                {
                    policy.AppendAudit(Audit(verifiedActor,
                        request is AgentRequest.ListEnvironment ? "ListEnvironment" : "RevealEnvironmentVariable",
                        verifiedService, "Unavailable", request.CorrelationId) with
                    {
                        VariableName = request is AgentRequest.RevealEnvironmentVariable reveal &&
                            ServiceOperations.IsValidVariableName(reveal.Name) ? reveal.Name : null,
                    });
                }
                catch { /* Cancellation still publishes no data when audit is unavailable. */ }
            }
            throw;
        }
        catch
        {
            try
            {
                var operation = request switch
                {
                    AgentRequest.Login => "Login",
                    AgentRequest.Logout => "Logout",
                    AgentRequest.ListServices => "ListServices",
                    AgentRequest.InspectService => "InspectService",
                    AgentRequest.ListGrants => "ListGrants",
                    AgentRequest.AddGrant => "AddGrant",
                    AgentRequest.RemoveGrant => "RemoveGrant",
                    AgentRequest.ListEnvironment => "ListEnvironment",
                    AgentRequest.RevealEnvironmentVariable => "RevealEnvironmentVariable",
                    _ => null,
                };
                if (operation is not null)
                {
                    policy.AppendAudit(Audit(verifiedActor, operation, verifiedService, "Unavailable", request.CorrelationId) with
                    {
                        VariableName = request is AgentRequest.RevealEnvironmentVariable reveal &&
                            ServiceOperations.IsValidVariableName(reveal.Name) ? reveal.Name : null,
                    });
                }
            }
            catch
            {
                // If audit storage itself is unavailable, deny without disclosing the cause.
            }

            return Failure(request, AgentResultCode.Unavailable);
        }
    }

    private async Task<AgentResult> ReadEnvironmentAsync(AgentRequest request, string token, string service,
        string? name, Action<AgentPrincipal> remember, Action<string> rememberService, CancellationToken cancellationToken)
    {
        var isReveal = request is AgentRequest.RevealEnvironmentVariable;
        var operation = isReveal ? "RevealEnvironmentVariable" : "ListEnvironment";
        var context = await AuthenticateAndResolveAsync(token, remember, cancellationToken).ConfigureAwait(false);
        var actor = context?.Principal;
        string? canonical = null;
        AgentResult Finish(AgentResultCode code, string? failure = null)
        {
            policy.AppendAudit(Audit(actor, operation, canonical, code.ToString(), request.CorrelationId) with
            {
                VariableName = ServiceOperations.IsValidVariableName(name) ? name : null,
            });
            return isReveal ? new AgentResult.RevealEnvironmentVariable(code, null, failure)
                : new AgentResult.ListEnvironment(code, null, failure);
        }
        if (context is null) return Finish(AgentResultCode.Unauthenticated);
        if (!TryConcreteService(service, out var id) || isReveal && !ServiceOperations.IsValidVariableName(name))
            return Finish(AgentResultCode.InvalidRequest);
        if (SystemdProtectedServicePolicy.IsProtected(id!, [id!])) return Finish(AgentResultCode.NotFound);
        var inspection = await inventory.InspectIdentityAsync(id!, cancellationToken).ConfigureAwait(false);
        if (inspection is not ServiceIdentityInspectionResult.Found found) return Finish(AgentResultCode.NotFound);
        canonical = found.Identity.Service.Id.Value;
        rememberService(canonical);
        if (SystemdProtectedServicePolicy.IsProtected(found.Identity.Service.Id, found.Identity.Names))
            return Finish(AgentResultCode.NotFound);
        bool Allowed(LinuxAccount account, IReadOnlyList<StoredGrant> grants) =>
            CanPerform(ServiceOperations.EnvironmentView, canonical, account, grants) &&
            (!isReveal || CanPerform(ServiceOperations.EnvironmentReveal, canonical, account, grants));
        if (!Allowed(context.Value.Account, policy.ListGrants())) return Finish(AgentResultCode.Denied);
        if (environmentReader is null) return Finish(AgentResultCode.Unavailable);
        var read = await environmentReader.ReadAsync(found.Identity.Service.Id, cancellationToken).ConfigureAwait(false);
        using var snapshot = read as ServiceEnvironmentReadResult.Success;
        if (snapshot is null)
        {
            var failure = ((ServiceEnvironmentReadResult.Failure)read).Code;
            return failure is EnvironmentReadFailureCode.NotFound or EnvironmentReadFailureCode.ProtectedTarget
                ? Finish(AgentResultCode.NotFound) : Finish(AgentResultCode.Unavailable, failure.ToString());
        }
        if (snapshot.CanonicalServiceId != found.Identity.Service.Id) return Finish(AgentResultCode.Unavailable);
        context = await AuthenticateAndResolveAsync(token, remember, cancellationToken).ConfigureAwait(false);
        if (context is null) return Finish(AgentResultCode.Unauthenticated);
        var finalIdentity = await inventory.InspectIdentityAsync(id!, cancellationToken).ConfigureAwait(false);
        if (finalIdentity is not ServiceIdentityInspectionResult.Found current ||
            current.Identity.Service.Id != found.Identity.Service.Id ||
            SystemdProtectedServicePolicy.IsProtected(current.Identity.Service.Id, current.Identity.Names))
            return Finish(AgentResultCode.NotFound);
        var currentGrants = policy.ListGrants();
        if (!Allowed(context.Value.Account, currentGrants)) return Finish(AgentResultCode.Denied);
        cancellationToken.ThrowIfCancellationRequested();
        if (!isReveal)
        {
            var metadata = new AgentEnvironment(canonical, snapshot.Scope, snapshot.Sources,
                snapshot.Variables.OrderBy(variable => variable.Name, StringComparer.Ordinal).ToArray(),
                CanPerform(ServiceOperations.EnvironmentReveal, canonical, context.Value.Account, currentGrants));
            _ = Finish(AgentResultCode.Success);
            return new AgentResult.ListEnvironment(AgentResultCode.Success, metadata);
        }
        var variable = snapshot.Variables.FirstOrDefault(variable => variable.Name == name);
        if (variable is null) return Finish(AgentResultCode.NotFound);
        var payload = new RevealedEnvironmentVariable(canonical, name!,
            snapshot.Sources.Single(source => source.Id == variable.WinningSourceId), snapshot.Values.Reveal(name!));
        try
        {
            _ = Finish(AgentResultCode.Success);
            cancellationToken.ThrowIfCancellationRequested();
            return new AgentResult.RevealEnvironmentVariable(AgentResultCode.Success, payload);
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }

    private async Task<AgentResult> LoginAsync(AgentRequest.Login request,
        Action<AgentPrincipal> remember, CancellationToken cancellationToken)
    {
        var authentication = await identities.AuthenticateAsync(request.Username, request.Password, cancellationToken)
            .ConfigureAwait(false);
        if (authentication.Status != AgentResultStatus.Success || authentication.Account is null)
        {
            var code = authentication.Status == AgentResultStatus.Unavailable
                ? AgentResultCode.Unavailable : AgentResultCode.Denied;
            policy.AppendAudit(Audit(null, "Login", null, code.ToString(), request.CorrelationId));
            return new AgentResult.Login(code, null);
        }

        var account = authentication.Account;
        remember(new AgentPrincipal(account.Name, account.Uid));
        var token = sessions.Create(account.Name, account.Uid);
        try
        {
            policy.AppendAudit(Audit(new AgentPrincipal(account.Name, account.Uid),
                "Login", null, "Success", request.CorrelationId));
            return new AgentResult.Login(AgentResultCode.Success, token);
        }
        catch
        {
            sessions.Revoke(token);
            throw;
        }
    }

    private async Task<AgentResult> LogoutAsync(AgentRequest.Logout request,
        Action<AgentPrincipal> remember, CancellationToken cancellationToken)
    {
        var principal = await sessions.AuthenticateAsync(request.Session, cancellationToken).ConfigureAwait(false);
        if (principal is null)
        {
            policy.AppendAudit(Audit(null, "Logout", null, "Unauthenticated", request.CorrelationId));
            return new AgentResult.Logout(AgentResultCode.Unauthenticated);
        }

        remember(principal);
        sessions.Revoke(request.Session);
        policy.AppendAudit(Audit(principal, "Logout", null, "Success", request.CorrelationId));
        return new AgentResult.Logout(AgentResultCode.Success);
    }

    private async Task<AgentResult> ListServicesAsync(
        AgentRequest.ListServices request, Action<AgentPrincipal> remember,
        CancellationToken cancellationToken)
    {
        var context = await AuthenticateAndResolveAsync(request.Session, remember, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return AuditedFailure(request, null, "ListServices", null, AgentResultCode.Unauthenticated);
        }

        var grants = policy.ListGrants();
        var snapshot = await inventory.ListIdentitiesAsync(cancellationToken).ConfigureAwait(false);
        var published = new List<AgentService>();
        foreach (var identity in snapshot)
        {
            if (SystemdProtectedServicePolicy.IsProtected(identity.Service.Id, identity.Names))
            {
                continue;
            }

            if (CanView(identity.Service.Id.Value, context.Value.Account, grants))
            {
                published.Add(ToAgentService(identity));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        published.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        policy.AppendAudit(Audit(context.Value.Principal, "ListServices", null,
            "Success", request.CorrelationId));
        return new AgentResult.ListServices(AgentResultCode.Success, published.AsReadOnly());
    }

    private async Task<AgentResult> InspectServiceAsync(
        AgentRequest.InspectService request, Action<AgentPrincipal> remember,
        CancellationToken cancellationToken)
    {
        var context = await AuthenticateAndResolveAsync(request.Session, remember, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return AuditedFailure(request, null, "InspectService", null, AgentResultCode.Unauthenticated);
        }

        if (!TryConcreteService(request.Service, out var id))
        {
            return AuditedFailure(request, context.Value.Principal, "InspectService", null,
                AgentResultCode.InvalidRequest);
        }

        if (SystemdProtectedServicePolicy.IsProtected(id!, [id!]))
        {
            return AuditedFailure(request, context.Value.Principal, "InspectService", id!.Value,
                AgentResultCode.NotFound);
        }

        var result = await inventory.InspectIdentityAsync(id!, cancellationToken).ConfigureAwait(false);
        if (result is not ServiceIdentityInspectionResult.Found found)
        {
            return AuditedFailure(request, context.Value.Principal, "InspectService", null,
                AgentResultCode.NotFound);
        }

        var canonical = found.Identity.Service.Id.Value;
        if (SystemdProtectedServicePolicy.IsProtected(found.Identity.Service.Id, found.Identity.Names))
        {
            return AuditedFailure(request, context.Value.Principal, "InspectService", canonical,
                AgentResultCode.NotFound);
        }

        var grants = policy.ListGrants();
        if (!CanView(canonical, context.Value.Account, grants))
        {
            return AuditedFailure(request, context.Value.Principal, "InspectService", canonical,
                AgentResultCode.Denied);
        }

        policy.AppendAudit(Audit(context.Value.Principal, "InspectService", canonical,
            "Success", request.CorrelationId));
        return new AgentResult.InspectService(AgentResultCode.Success, ToAgentService(found.Identity));
    }

    private async Task<AgentResult> ListGrantsAsync(
        AgentRequest.ListGrants request, Action<AgentPrincipal> remember,
        CancellationToken cancellationToken)
    {
        var context = await AuthenticateAndResolveAsync(request.Session, remember, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return AuditedFailure(request, null, "ListGrants", null, AgentResultCode.Unauthenticated);
        }

        if (!IsAdmin(context.Value.Account))
        {
            return AuditedFailure(request, context.Value.Principal, "ListGrants", null,
                AgentResultCode.Denied);
        }

        var grants = policy.ListGrants().Select(item => new AgentGrant(item.Id, item.SubjectKind,
            item.SubjectName, item.Operation, item.Service)).ToArray();
        policy.AppendAudit(Audit(context.Value.Principal, "ListGrants", null,
            "Success", request.CorrelationId));
        return new AgentResult.ListGrants(AgentResultCode.Success, Array.AsReadOnly(grants));
    }

    private async Task<AgentResult> AddGrantAsync(
        AgentRequest.AddGrant request, Action<AgentPrincipal> remember,
        CancellationToken cancellationToken)
    {
        var context = await AuthenticateAndResolveAsync(request.Session, remember, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return AuditedFailure(request, null, "AddGrant", null, AgentResultCode.Unauthenticated);
        }

        if (!IsAdmin(context.Value.Account))
        {
            return AuditedFailure(request, context.Value.Principal, "AddGrant", null,
                AgentResultCode.Denied);
        }

        if (!ServiceOperations.IsSupported(request.Operation) ||
            request.SubjectKind is not ("User" or "Group") ||
            !TryConcreteService(request.Service, out var id) ||
            !ValidSubject(request.Subject))
        {
            return AuditedFailure(request, context.Value.Principal, "AddGrant", null,
                AgentResultCode.InvalidRequest);
        }

        if (SystemdProtectedServicePolicy.IsProtected(id!, [id!]))
        {
            return AuditedFailure(request, context.Value.Principal, "AddGrant", null,
                AgentResultCode.NotFound);
        }

        uint? subjectId;
        if (request.SubjectKind == "User")
        {
            var user = await identities.ResolveAccountAsync(request.Subject, cancellationToken)
                .ConfigureAwait(false);
            subjectId = user?.Name == request.Subject ? user.Uid : null;
        }
        else
        {
            var group = await identities.ResolveGroupAsync(request.Subject, cancellationToken)
                .ConfigureAwait(false);
            subjectId = group?.Name == request.Subject ? group.Gid : null;
        }
        if (subjectId is null)
        {
            return AuditedFailure(request, context.Value.Principal, "AddGrant", null,
                AgentResultCode.InvalidRequest);
        }

        var inspection = await inventory.InspectIdentityAsync(id!, cancellationToken).ConfigureAwait(false);
        if (inspection is not ServiceIdentityInspectionResult.Found found ||
            found.Identity.Service.Id != id ||
            SystemdProtectedServicePolicy.IsProtected(found.Identity.Service.Id, found.Identity.Names))
        {
            return AuditedFailure(request, context.Value.Principal, "AddGrant", null,
                AgentResultCode.NotFound);
        }

        var grantId = policy.AddGrant(new StoredGrant(0, request.SubjectKind, request.Subject,
            subjectId.Value, request.Operation, id!.Value),
            Audit(context.Value.Principal, "AddGrant", id.Value, "Success", request.CorrelationId));
        return new AgentResult.AddGrant(AgentResultCode.Success, grantId);
    }

    private async Task<AgentResult> RemoveGrantAsync(
        AgentRequest.RemoveGrant request, Action<AgentPrincipal> remember,
        CancellationToken cancellationToken)
    {
        var context = await AuthenticateAndResolveAsync(request.Session, remember, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            return AuditedFailure(request, null, "RemoveGrant", null, AgentResultCode.Unauthenticated);
        }

        if (!IsAdmin(context.Value.Account))
        {
            return AuditedFailure(request, context.Value.Principal, "RemoveGrant", null,
                AgentResultCode.Denied);
        }

        if (request.GrantId <= 0)
        {
            return AuditedFailure(request, context.Value.Principal, "RemoveGrant", null,
                AgentResultCode.InvalidRequest);
        }

        var removed = policy.RemoveGrant(request.GrantId,
            Audit(context.Value.Principal, "RemoveGrant", null, "Success", request.CorrelationId));
        return new AgentResult.RemoveGrant(removed ? AgentResultCode.Success : AgentResultCode.NotFound);
    }

    private async Task<(AgentPrincipal Principal, LinuxAccount Account)?> AuthenticateAndResolveAsync(
        string token, Action<AgentPrincipal> remember, CancellationToken cancellationToken)
    {
        var principal = await sessions.AuthenticateAsync(token, cancellationToken).ConfigureAwait(false);
        if (principal is null)
        {
            return null;
        }

        remember(principal);
        var account = await identities.ResolveAccountAsync(principal.Name, cancellationToken)
            .ConfigureAwait(false);
        return account is not null && account.Uid == principal.Uid && account.Name == principal.Name
            ? (principal, account)
            : null;
    }

    private bool CanView(string service, LinuxAccount account, IReadOnlyList<StoredGrant> grants) =>
        CanPerform(ServiceOperations.View, service, account, grants);

    private bool CanPerform(string operation, string service, LinuxAccount account, IReadOnlyList<StoredGrant> grants) =>
        ServiceOperations.IsSupported(operation) && (IsAdmin(account) || grants.Any(grant => grant.Operation == operation &&
            string.Equals(grant.Service, service, StringComparison.Ordinal) &&
            (grant.SubjectKind == "User" && grant.SubjectName == account.Name &&
                grant.SubjectId == account.Uid ||
             grant.SubjectKind == "Group" && account.Groups.Any(group =>
                grant.SubjectName == group.Name && grant.SubjectId == group.Gid))));

    private bool IsAdmin(LinuxAccount account) => account.Groups.Any(group =>
        group.Name == configuration.AdminGroup && group.Gid == configuration.AdminGid);

    private AgentResult AuditedFailure(AgentRequest request, AgentPrincipal? principal,
        string operation, string? service, AgentResultCode code)
    {
        policy.AppendAudit(Audit(principal, operation, service, code.ToString(), request.CorrelationId));
        return Failure(request, code);
    }

    private AuditEntry Audit(AgentPrincipal? principal, string operation, string? service,
        string outcome, string correlationId) =>
        new(principal?.Name, principal?.Uid, operation, service, outcome,
            correlationId, clock.GetUtcNow());

    private static AgentService ToAgentService(SystemServiceIdentity identity)
    {
        var service = identity.Service;
        return new AgentService(service.Id.Value, service.Description, service.LoadState.Value,
            service.ActiveState.Value, service.SubState.Value);
    }

    private static bool TryConcreteService(string? name, out SystemServiceId? id)
    {
        id = null;
        if (name is null || name.EndsWith("@.service", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            id = new SystemServiceId(name);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidSubject(string? subject) =>
        subject is { Length: > 0 and <= 128 } &&
        !subject.Any(value => value is '/' or '\0' || char.IsControl(value));

    private static bool ValidCorrelation(string? value) =>
        value is { Length: > 0 and <= 64 } &&
        value.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or
            >= '0' and <= '9' or '-' or '_');

    private static AgentResult Failure(AgentRequest request, AgentResultCode code) => request switch
    {
        AgentRequest.Login => new AgentResult.Login(code, null),
        AgentRequest.Logout => new AgentResult.Logout(code),
        AgentRequest.ListServices => new AgentResult.ListServices(code, null),
        AgentRequest.InspectService => new AgentResult.InspectService(code, null),
        AgentRequest.ListGrants => new AgentResult.ListGrants(code, null),
        AgentRequest.AddGrant => new AgentResult.AddGrant(code, null),
        AgentRequest.RemoveGrant => new AgentResult.RemoveGrant(code),
        AgentRequest.ListEnvironment => new AgentResult.ListEnvironment(code, null),
        AgentRequest.RevealEnvironmentVariable => new AgentResult.RevealEnvironmentVariable(code, null),
        _ => throw new AgentProtocolException(),
    };
}
