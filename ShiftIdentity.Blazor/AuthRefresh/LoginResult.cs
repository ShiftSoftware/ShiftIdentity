using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// Outcome of the JWT login and password-change operations on <see cref="JwtRefreshStrategy"/>.
/// Success carries the claims used to seed the in-memory auth state and a
/// <see cref="RequirePasswordChange"/> hint for the UI; failure carries the server-provided
/// error message + title for display.
/// </summary>
public class LoginResult
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorTitle { get; init; }

    public bool RequirePasswordChange { get; init; }

    public List<UserClaimModel>? Claims { get; init; }

    public static LoginResult Failure(string? message, string? title = null) =>
        new() { IsSuccess = false, ErrorMessage = message, ErrorTitle = title };
}
