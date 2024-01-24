using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

public class MessageService
{
    private readonly IJSRuntime jsRuntime;
    private const string MessageElementId = "1b59934b-235f-403b-b29e-786bed455da1";

    public MessageService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    internal async Task ShowWarningMessageAsync(string message)
    {
        var script = $"var existingMessageElement = document.getElementById('{MessageElementId}');" +
                         $"if (existingMessageElement) {{" +
                         $"    document.body.removeChild(existingMessageElement);" + // Remove the existing message element
                         $"}}" +
                         $"var messageElement = document.createElement('div');" +
                         $"messageElement.id = '{MessageElementId}';" +
                         $"messageElement.innerText = '{message}';" +
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
                         $"messageElement.style.textAlign = 'center';" + // Center the text
                         $"document.body.appendChild(messageElement);"; // Append messageElement to body

        await jsRuntime.InvokeVoidAsync("eval", script);
    }

    public async Task RemoveWarningMessageAsync()
    {
        // Embed JavaScript code directly
        var removeScript = $"var existingMessageElement = document.getElementById('{MessageElementId}');" +
                           $"if (existingMessageElement) {{" +
                           $"    document.body.removeChild(existingMessageElement);" + // Remove the existing message element
                           $"}}";

        // Append the script to remove the message element
        await jsRuntime.InvokeVoidAsync("eval", removeScript);
    }
}
