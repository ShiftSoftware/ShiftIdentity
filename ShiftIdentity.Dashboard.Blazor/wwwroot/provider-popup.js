// The Microsoft popup that confirms an administrator whose account signs in only with Microsoft. The dialog opens it on
// the button's click (before any request, so the browser treats it as the user's), points it at Microsoft once the
// server answers, and hears back from provider-confirmation.html when Microsoft has returned.

const channelName = 'shift-identity-provider-confirmation';
const popups = new Map();

export function open(key) {
    const popup = window.open('about:blank', 'shift-identity-' + key, 'popup,width=520,height=680');
    if (!popup) return false;
    popups.set(key, popup);
    return true;
}

export function navigate(key, url) {
    const popup = popups.get(key);
    if (!popup || popup.closed) return false;
    popup.location.href = url;
    return true;
}

export function close(key) {
    const popup = popups.get(key);
    popups.delete(key);
    try { if (popup && !popup.closed) popup.close(); } catch { /* the popup may be on Microsoft's page */ }
}

// Reports each return to the dialog: null when Microsoft proved the account, otherwise the failure's name. The page
// posts on a BroadcastChannel (which Microsoft's opener policy cannot cut) and to its opener, so either may arrive.
export function listen(dotnet) {
    const report = data => {
        if (!data || data.kind !== channelName) return;
        dotnet.invokeMethodAsync('ProviderReturned', typeof data.error === 'string' ? data.error : null);
    };
    const channel = typeof BroadcastChannel === 'function' ? new BroadcastChannel(channelName) : null;
    if (channel) channel.onmessage = event => report(event.data);
    const onMessage = event => { if (event.origin === window.location.origin) report(event.data); };
    window.addEventListener('message', onMessage);
    return {
        stop: () => {
            channel?.close();
            window.removeEventListener('message', onMessage);
        }
    };
}
