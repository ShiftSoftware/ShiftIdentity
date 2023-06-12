using Microsoft.AspNetCore.Mvc;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Extensions;

internal static class ControllerBaseExtensions
{

    /// <summary>
    /// Get base uri without trailing slash
    /// </summary>
    /// <param name="controller"></param>
    /// <returns></returns>
    internal static string GetBaseUri(this ControllerBase controller)
    {
        var uriBuilder = new UriBuilder(controller.Request.Scheme, controller.Request.Host.Host, controller.Request.Host.Port ?? -1);
        if (uriBuilder.Uri.IsDefaultPort)
        {
            uriBuilder.Port = -1;
        }

        return uriBuilder.Uri.AbsoluteUri;
    }
}
