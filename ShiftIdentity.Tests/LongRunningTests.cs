namespace ShiftIdentity.Tests;

/// <summary>
/// The switch behind every LongRunning test. Those tests wait the production response floors for real, tens of
/// seconds in total, so they are skipped unless <see cref="Variable"/> is set to 1: the opt-in pipeline gate sets
/// it, and a developer sets it before a run that should include them. The Sql gate never selects them anyway.
/// </summary>
public static class LongRunningTests
{
    public const string Variable = "SHIFT_IDENTITY_LONG_RUNNING";
    public const string SkipReason = "Real-clock timing evidence; set " + Variable + "=1 to run.";
    public static bool Enabled => Environment.GetEnvironmentVariable(Variable) == "1";
}
