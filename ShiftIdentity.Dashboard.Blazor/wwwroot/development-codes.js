// Hide stale codes synchronously, including while a suspended tab waits for .NET to resume.
export function watch(element, receiver) {
    let timer;
    let disposed = false;
    const suspend = () => element.setAttribute('data-codes-suspended', '');
    const visibility = () => {
        suspend();
        clearTimeout(timer);
        receiver.invokeMethodAsync('VisibilityChanged', !document.hidden).catch(() => {});
    };
    document.addEventListener('visibilitychange', visibility);
    window.addEventListener('pageshow', visibility);
    if (document.hidden) visibility();
    return {
        arm(milliseconds) {
            clearTimeout(timer);
            if (disposed || document.hidden || milliseconds <= 0) { suspend(); return; }
            element.removeAttribute('data-codes-suspended');
            timer = setTimeout(suspend, milliseconds);
        },
        dispose() {
            disposed = true;
            clearTimeout(timer);
            document.removeEventListener('visibilitychange', visibility);
            window.removeEventListener('pageshow', visibility);
        }
    };
}
