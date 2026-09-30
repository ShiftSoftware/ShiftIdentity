namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;

/// <summary>
/// The attempts and the error panels of one security screen or dialog. Every press of one of its buttons is an attempt:
/// the errors on screen slide closed and clear first, and then the attempt runs, one at a time. A failure sets its error
/// again and the panel opens again, so every failed press is seen. <see cref="LoginBox"/> and
/// <see cref="AuthFeedbackScope"/> provide one to their content; <see cref="AuthForm"/> and <see cref="AuthError"/> use it.
/// </summary>
public sealed class AuthFeedback
{
    private readonly List<AuthError> panels = [];

    /// <summary>An attempt is under way. The screen's buttons stay disabled until it ends.</summary>
    public bool Attempting { get; private set; }

    /// <summary>Raised when <see cref="Attempting"/> changes.</summary>
    public event Action? Changed;

    internal void Add(AuthError panel) => panels.Add(panel);
    internal void Remove(AuthError panel) => panels.Remove(panel);

    /// <summary>
    /// Runs one attempt after the errors on screen have closed. A press while an attempt is under way does nothing and
    /// returns false.
    /// </summary>
    public async Task<bool> AttemptAsync(Func<Task> attempt)
    {
        if (Attempting) return false;
        Attempting = true;
        Changed?.Invoke();
        try
        {
            await Task.WhenAll(panels.ToArray().Select(panel => panel.CloseAsync()));
            await attempt();
            return true;
        }
        finally
        {
            Attempting = false;
            Changed?.Invoke();
        }
    }
}
