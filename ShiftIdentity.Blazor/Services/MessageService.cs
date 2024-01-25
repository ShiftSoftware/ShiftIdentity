using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class MessageService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IIdentityStore _identityStore;

    private const string MessageElementId = "1b59934b-235f-403b-b29e-786bed455da1";

    public MessageService(IJSRuntime jsRuntime, IIdentityStore identityStore)
    {
        this._jsRuntime = jsRuntime;
        _identityStore = identityStore;
    }

    internal async Task ShowWarningMessageAsync(string message, string? linkText = null)
    {
        // Embed JavaScript code directly
        var script = $"var existingMessageElement = document.getElementById('{MessageElementId}');" +
                     $"if (existingMessageElement) {{" +
                     $"    document.body.removeChild(existingMessageElement);" + // Remove the existing message element
                     $"}}" +
                     $"var messageElement = document.createElement('div');" +
                     $"messageElement.id = '{MessageElementId}';" +
                     $"messageElement.style.position = 'fixed';" +
                     $"messageElement.style.bottom = '0';" +
                     $"messageElement.style.left = '0';" +
                     $"messageElement.style.width = '100%';" +
                     $"messageElement.style.backgroundColor = '#fff8c4';" + // Background color
                     $"messageElement.style.border = '1px solid #f7deae';" + // Border color
                     $"messageElement.style.opacity = '0.7';" + // Opacity 0.7
                     $"messageElement.style.color = 'black';" + // Text color
                     $"messageElement.style.padding = '10px';" +
                     $"messageElement.style.borderRadius = '0 0 5px 5px';" + // Rounded bottom corners
                     $"messageElement.style.textAlign = 'center';"; // Center the text

        // Add the message content
        script += $"messageElement.innerHTML = '{message}';";
        

        // Add the link if provided
        if (!string.IsNullOrWhiteSpace(linkText))
        {
            script += $"var link = document.createElement('a');" +
                          $"link.href = window.location.href;" + // Redirect to the same current page
                          $"link.target = '_blank';" + // Open in a new tab
                          $"link.innerText = '{linkText}';" +
                          $"messageElement.appendChild(link);"; // Append link to messageElement
        }

        // Append the message element to body
        script += $"document.body.appendChild(messageElement);";

        // Execute the script
        await _jsRuntime.InvokeVoidAsync("eval", script);
    }

    [JSInvokable]
    public async Task RemoveTokenAndNavigate()
    {
        await _identityStore.RemoveTokenAsync();
        await _jsRuntime.InvokeVoidAsync("window.open", "about:blank", "_blank");
    }

    public async Task RemoveWarningMessageAsync()
    {
        // Embed JavaScript code directly
        var removeScript = $"var existingMessageElement = document.getElementById('{MessageElementId}');" +
                           $"if (existingMessageElement) {{" +
                           $"    document.body.removeChild(existingMessageElement);" + // Remove the existing message element
                           $"}}";

        // Append the script to remove the message element
        await _jsRuntime.InvokeVoidAsync("eval", removeScript);
    }
}
