using ShiftSoftware.ShiftEntity.CosmosDbReplication.Services;

namespace ShiftSoftware.ShiftIdentity.AzureFunctions.Replication;

/// <summary>
/// What a catch-up of several Identity entities did (<c>ReplicateOrganizationAsync</c>, <c>ReplicateAllAsync</c>): one
/// entry per step, in the order the steps ran. A step that failed did not stop the steps after it.
/// </summary>
public sealed class IdentityCatchUpReport
{
    internal IdentityCatchUpReport(IReadOnlyList<IdentityCatchUpStep> steps) => Steps = steps;

    public IReadOnlyList<IdentityCatchUpStep> Steps { get; }

    /// <summary>Every step ran to its end and replicated every row it selected.</summary>
    public bool Succeeded => Steps.All(x => x.Succeeded);

    /// <summary>Throws <see cref="IdentityCatchUpException"/> when a step or a row failed, for a caller that must fail too.</summary>
    public void ThrowIfFailed()
    {
        if (!Succeeded)
            throw new IdentityCatchUpException(this);
    }

    public override string ToString() => string.Join(" ", Steps);
}

/// <summary>
/// One step of an Identity catch-up: the entity it replicated, and what came of it. A step that ran to its end has a
/// <see cref="Result"/>, with any rows that failed; their rows stay dirty for the next run. A step that threw has the
/// <see cref="Error"/> instead.
/// </summary>
public sealed class IdentityCatchUpStep
{
    internal IdentityCatchUpStep(string entity, CosmosDbReplicationResult? result, Exception? error)
    {
        Entity = entity;
        Result = result;
        Error = error;
    }

    public string Entity { get; }

    /// <summary>The step's report, or null when the step threw.</summary>
    public CosmosDbReplicationResult? Result { get; }

    /// <summary>What stopped the step, or null when it ran to its end.</summary>
    public Exception? Error { get; }

    public bool Succeeded => Error is null && Result is { Succeeded: true };

    public override string ToString() => Error is null
        ? Result!.ToString()
        : $"{Entity}: the step stopped. {Error.GetType().Name}: {Error.Message}";
}

/// <summary>An Identity catch-up in which a step or a row failed. <see cref="Report"/> has every step.</summary>
public class IdentityCatchUpException : Exception
{
    public IdentityCatchUpException(IdentityCatchUpReport report)
        : base("The Identity catch-up ran every step, and these failed: " +
            string.Join(" ", report.Steps.Where(x => !x.Succeeded)),
            report.Steps.FirstOrDefault(x => x.Error is not null)?.Error)
    {
        Report = report;
    }

    public IdentityCatchUpReport Report { get; }
}
