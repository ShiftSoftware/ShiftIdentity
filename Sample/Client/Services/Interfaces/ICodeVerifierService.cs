namespace Sample.Client.Services.Interfaces
{
    public interface ICodeVerifierService
    {
        Task<string> GenerateCodeChallengeAsync();
        Task<string> LoadCodeVerifierAsync();
    }
}