using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services
{
    public class HttpService
    {
        private readonly HttpClient http;

        public HttpService(HttpClient http)
        {
            this.http = http;
        }

        public async Task<HttpResponse<TResult>> PostAsync<TResult, TValue>(string url, TValue value)
            where TValue : class
            where TResult : class
        {
            using var response = await http.PostAsJsonAsync(url, value);

            return new HttpResponse<TResult>
                ((await response.Content.ReadFromJsonAsync<TResult>())!, response.StatusCode);
        }

        public async Task<HttpResponse<TResult>> GetAsync<TResult>(
            string url,
            string? odataQuery = null,
            object? query = null)
            where TResult : class
        {
            var generatedUrl = url;

            var queryString = query?.ToQueryString() ?? "";

            if (!string.IsNullOrWhiteSpace(queryString) || !string.IsNullOrWhiteSpace(odataQuery))
                generatedUrl += $"?{queryString}&${odataQuery}";

            using var response = await http.GetAsync(generatedUrl);

            if (!response.IsSuccessStatusCode)
                return new HttpResponse<TResult>(await response.Content.ReadAsStringAsync(), response.StatusCode);

            return new HttpResponse<TResult>((await response.Content?.ReadFromJsonAsync<TResult>()!)!, response.StatusCode);
        }

        public async Task<HttpResponse<TResult>> PutAsync<TResult, TValue>(string url, TValue value)
            where TResult : class
            where TValue : class
        {
            using var response = await http.PutAsJsonAsync(url, value);

            return new HttpResponse<TResult>
                ((await response.Content.ReadFromJsonAsync<TResult>())!, response.StatusCode);
        }

        public async Task<HttpResponse<TResult>> DeleteAsync<TResult>(string url)
            where TResult : class
        {
            using var response = await http.DeleteAsync(url);

            return new HttpResponse<TResult>
                ((await response.Content.ReadFromJsonAsync<TResult>())!, response.StatusCode);
        }
    }
}
