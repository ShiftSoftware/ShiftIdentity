export function clearFragment(expectedUri) {
    // A newer navigation must keep its own fragment until that page processes it.
    if (new URL(expectedUri).href !== window.location.href) return false;
    window.history.replaceState(window.history.state, "", window.location.pathname + window.location.search);
    return true;
}
