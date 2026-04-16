namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

public record SignInWithTokenRequest(string Token, string RefreshToken, long? TokenLifeTimeInSeconds);
