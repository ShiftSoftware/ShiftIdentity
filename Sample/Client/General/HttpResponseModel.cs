using System.Net;

namespace Sample.Client.General;

public class HttpResponseModel<T>
{
    public HttpStatusCode StatusCode { get; set; }

    public string ErrorMessage { get; set; }

    public T? Data { get; set; }

    public HttpResponseModel(T data)
    {
        StatusCode = HttpStatusCode.OK;
        Data = data;
    }

    public HttpResponseModel(string errorMessage, HttpStatusCode statusCode)
    {
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
    }
}
