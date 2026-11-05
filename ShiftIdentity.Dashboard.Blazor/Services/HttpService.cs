using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using System.Net.Http.Json;
using System.Text.Json;

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

            return await ReadWriteAnswerAsync<TResult>(response);
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

            return await ReadWriteAnswerAsync<TResult>(response);
        }

        public async Task<HttpResponse<TResult>> DeleteAsync<TResult>(string url)
            where TResult : class
        {
            using var response = await http.DeleteAsync(url);

            return await ReadWriteAnswerAsync<TResult>(response);
        }

        /// <summary>
        /// Reads the answer to a write. A failed answer can carry a JSON message, and callers read that from
        /// <see cref="HttpResponse{T}.Data"/>. It can also have no body, or be an HTML page from a proxy. Those are not
        /// JSON, so the caller gets the status and the body text instead of an exception.
        /// </summary>
        private static async Task<HttpResponse<TResult>> ReadWriteAnswerAsync<TResult>(HttpResponseMessage response)
            where TResult : class
        {
            if (response.IsSuccessStatusCode)
                return new HttpResponse<TResult>((await response.Content.ReadFromJsonAsync<TResult>())!, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();

            try
            {
                var message = JsonSerializer.Deserialize<TResult>(body, FailedAnswerJson);

                if (message is not null)
                    return new HttpResponse<TResult>(message, response.StatusCode);
            }
            catch (JsonException)
            {
            }

            return new HttpResponse<TResult>(body, response.StatusCode);
        }

        // The same options that ReadFromJsonAsync uses.
        private static readonly JsonSerializerOptions FailedAnswerJson = new(JsonSerializerDefaults.Web);
    }
}
