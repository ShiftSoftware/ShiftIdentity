using Sample.Client.Extensions;
using Sample.Client.General;
using Sample.Client.Services.Interfaces;
using System.Net;
using System.Net.Http.Json;

namespace Sample.Client.Services;

public class HttpService : IHttpService
{
    private readonly HttpClient http;

    public HttpService(HttpClient http)
    {
        this.http = http;
    }

    public async Task<HttpResponseModel<TResult>> PostAsync<TValue, TResult>(string url, TValue value)
        where TValue : class
        where TResult : class
    {
        using var response = await http.PostAsJsonAsync(url, value);

        if (response.StatusCode != HttpStatusCode.OK)
            return new HttpResponseModel<TResult>(await response.Content.ReadAsStringAsync(), response.StatusCode);

        return new HttpResponseModel<TResult>(await response.Content.ReadFromJsonAsync<TResult>());
    }

    public async Task<HttpResponseModel<string>> PostAsync<TValue>(string url, TValue value)
        where TValue : class
    {
        using var response = await http.PostAsJsonAsync(url, value);

        if (response.StatusCode != HttpStatusCode.OK)
            return new HttpResponseModel<string>(await response.Content.ReadAsStringAsync(), response.StatusCode);

        return new HttpResponseModel<string>(await response.Content.ReadAsStringAsync());
    }

    public async Task<HttpResponseModel<TResult>> GetAsync<TResult>(string url, object query = null)
        where TResult : class
    {
        var queryString = query?.ToQueryString() ?? null;

        using var response = await http.GetAsync(url + (queryString is not null ? "?" + queryString : ""));

        if (response.StatusCode != HttpStatusCode.OK)
            return new HttpResponseModel<TResult>(await response.Content.ReadAsStringAsync(), response.StatusCode);

        return new HttpResponseModel<TResult>(await response.Content.ReadFromJsonAsync<TResult>());
    }

    public async Task<HttpResponseModel<TResult>> PutAsync<TResult, TValue>(string url, TValue value)
        where TResult : class
        where TValue : class
    {
        using var response = await http.PutAsJsonAsync(url, value);

        if (response.StatusCode != HttpStatusCode.OK)
            return new HttpResponseModel<TResult>(await response.Content.ReadAsStringAsync(), response.StatusCode);

        return new HttpResponseModel<TResult>(await response.Content.ReadFromJsonAsync<TResult>());
    }

    public async Task<HttpResponseModel<TResult>> DeleteAsync<TResult>(string url)
        where TResult : class
    {
        using var response = await http.DeleteAsync(url);

        if (response.StatusCode != HttpStatusCode.OK)
            return new HttpResponseModel<TResult>(await response.Content.ReadAsStringAsync(), response.StatusCode);

        return new HttpResponseModel<TResult>(await response.Content.ReadFromJsonAsync<TResult>());
    }
}
