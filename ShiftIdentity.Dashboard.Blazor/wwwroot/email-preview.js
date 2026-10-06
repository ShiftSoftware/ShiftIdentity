// Sizes the email preview frame to its message, now and whenever the message or the frame's width changes.
export function fit(frame) {
    const size = () => {
        const doc = frame.contentDocument;
        // The body, not the document: a document is never shorter than the frame, so it could only grow.
        if (doc?.body) frame.style.height = doc.body.scrollHeight + "px";
    };
    if (!frame.dataset.fitted) {
        frame.dataset.fitted = "true";
        frame.addEventListener("load", size);
        new ResizeObserver(size).observe(frame);
    }
    size();
}
