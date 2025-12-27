using System.Net.Http;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

// Marker type for DI to avoid circular dependency
public class ShiftIdentityHttpClient : HttpClient { }
