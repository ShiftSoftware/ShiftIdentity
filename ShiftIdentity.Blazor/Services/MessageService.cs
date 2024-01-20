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

    public MessageService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    internal async Task ShowWarningMessageAsync(string message)
    {
        var script = $"var existingMessageElement = document.getElementById('myLibraryMessage');" +
                         $"if (!existingMessageElement) {{" +
                         $"    var messageElement = document.createElement('div');" +
                         $"    messageElement.id = 'myLibraryMessage';" +
                         $"    messageElement.innerText = '{message}';" +
                         $"    messageElement.style.position = 'fixed';" +
                         $"    messageElement.style.bottom = '0';" +
                         $"    messageElement.style.left = '0';" +
                         $"    messageElement.style.width = '100%';" +
                         $"    messageElement.style.backgroundColor = '#fff8c4';" + // Background color
                         $"    messageElement.style.border = '1px solid #f7deae';" + // Border color
                         $"    messageElement.style.opacity = '0.7';" + // Opacity 0.7
                         $"    messageElement.style.color = 'black';" + // Text color
                         $"    messageElement.style.padding = '10px';" +
                         $"    messageElement.style.borderRadius = '0 0 5px 5px';" + // Rounded bottom corners
                         $"    messageElement.style.textAlign = 'center';" + // Center the text
                         $"    var closeButton = document.createElement('button');" +
                         $"    closeButton.style.backgroundColor = 'red';" + // Close button color
                         $"    closeButton.style.color = 'white';" +
                         $"    closeButton.style.border = 'none';" +
                         $"    closeButton.style.padding = '5px';" +
                         $"    closeButton.style.textAlign = 'center';" +
                         $"    closeButton.style.textDecoration = 'none';" +
                         $"    closeButton.style.display = 'inline-block';" +
                         $"    closeButton.style.fontSize = '16px';" +
                         $"    closeButton.style.position = 'absolute';" +
                         $"    closeButton.style.top = '0';" +
                         $"    closeButton.style.right = '0';" +
                         $"    closeButton.innerHTML = '&times;';" + // Close icon (times symbol)
                         $"    closeButton.onclick = function() {{ document.body.removeChild(messageElement); }};" +
                         $"    messageElement.appendChild(closeButton);" +
                         $"    document.body.appendChild(messageElement);" +
                         $"}}";

        await this.jsRuntime.InvokeVoidAsync("eval", script);
    }
}
