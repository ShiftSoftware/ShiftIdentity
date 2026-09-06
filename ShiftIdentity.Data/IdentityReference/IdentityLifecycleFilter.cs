namespace ShiftSoftware.ShiftIdentity.Data.IdentityReference;

/// <summary>
/// Which lifecycle states a <b>roster</b> read includes. Identity rows live in three states, not two:
///
/// <list type="bullet">
///   <item><b>Active</b> — in use.</item>
///   <item><b>Terminated</b> — a company or branch that has closed but is still referenced by historical
///     rows. Carried by <c>TerminationDate</c>, which exists on companies and branches only.</item>
///   <item><b>Deleted</b> — soft-deleted. Nothing is ever removed from the store, so the document
///     survives with <c>IsDeleted</c> set.</item>
/// </list>
///
/// <para>This applies to rosters only. <b>Resolving a single row by id never filters</b>, whichever value
/// is in play — see <see cref="IIdentityReferenceSource"/> for why that rule is not negotiable.</para>
///
/// <para><b>Why <see cref="All"/> is the zero value.</b> Anything that gets a default-constructed value —
/// an options object nobody configured, a struct field, a deserialized blank — lands on <see cref="All"/>
/// and therefore hides nothing. The failure mode of over-filtering (a real row silently missing) is far
/// worse and far harder to spot than the failure mode of under-filtering (a closed branch showing up in a
/// picker), so the value you get for doing nothing is the one that cannot lose data.</para>
/// </summary>
public enum IdentityLifecycleFilter
{
    /// <summary>
    /// Every row, including soft-deleted and terminated ones. The safe default, and the only value that
    /// answers for a row referenced by historical data.
    /// </summary>
    All = 0,

    /// <summary>
    /// Drops soft-deleted rows, keeps terminated ones. The right choice for a list that has to account for
    /// historical references — a closed branch is still a real branch.
    /// </summary>
    ExcludeDeleted = 1,

    /// <summary>
    /// Drops both soft-deleted and terminated rows. The right choice for a picker, where the question is
    /// "what can someone choose today".
    /// </summary>
    ActiveOnly = 2,
}
