using Sample.Client.General;

namespace Sample.Client.Services.Interfaces
{
    public interface IHttpService
    {
        Task<HttpResponseModel<TResult>> DeleteAsync<TResult>(string url) where TResult : class;
        Task<HttpResponseModel<TResult>> GetAsync<TResult>(string url, object query = null) where TResult : class;
        Task<HttpResponseModel<TResult>> PostAsync<TValue, TResult>(string url, TValue value)
            where TValue : class
            where TResult : class;
        Task<HttpResponseModel<string>> PostAsync<TValue>(string url, TValue value) where TValue : class;
        Task<HttpResponseModel<TResult>> PutAsync<TResult, TValue>(string url, TValue value)
            where TResult : class
            where TValue : class;
    }
}