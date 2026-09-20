const boxes = new WeakMap();
const reducedMotion = () => matchMedia('(prefers-reduced-motion: reduce)').matches;
const frame = () => new Promise(resolve => requestAnimationFrame(resolve));

function state(root) {
    let value = boxes.get(root);
    if (value) return value;
    const viewport = root.querySelector('.identity-login-viewport');
    const content = root.querySelector('.identity-login-content');
    const outgoing = root.querySelector('.identity-login-outgoing');
    value = { viewport, content, outgoing, revision: 0 };
    value.observer = new ResizeObserver(() => { viewport.style.height = `${content.offsetHeight}px`; });
    value.observer.observe(content);
    boxes.set(root, value);
    return value;
}

export function prepare(root) {
    const s = state(root);
    s.outgoing.replaceChildren();
    const snapshot = s.content.cloneNode(true);
    snapshot.inert = true;
    snapshot.setAttribute('aria-hidden', 'true');
    snapshot.querySelectorAll('[id]').forEach(element => element.removeAttribute('id'));
    snapshot.querySelectorAll('input, textarea').forEach(element => { element.value = ''; element.removeAttribute('value'); });
    s.outgoing.append(snapshot);
    s.pulse?.cancel();
}

export async function sync(root, key, direction, trackHistory) {
    if (trackHistory) history.replaceState({ ...history.state, shiftIdentityLogin: true }, '');
    const s = state(root);
    const revision = ++s.revision;
    const valid = () => boxes.get(root) === s && revision === s.revision && root.isConnected;
    const changed = s.key !== undefined && s.key !== key;
    s.key = key;
    s.viewport.style.height = `${s.content.offsetHeight}px`;
    if (changed) {
        s.focusRequested = key;
        s.content.getAnimations().forEach(animation => animation.cancel());
        const options = { duration: reducedMotion() ? 0 : 280, easing: 'cubic-bezier(.22,.75,.25,1)' };
        const offset = (getComputedStyle(root).direction === 'rtl' ? -1 : 1) * direction * 55;
        const snapshot = s.outgoing.firstElementChild;
        snapshot?.animate([{ opacity: 1, transform: 'translateX(0)' }, { opacity: 0, transform: `translateX(${-offset}px)` }], { ...options, fill: 'forwards' });
        await s.content.animate([{ opacity: 0, transform: `translateX(${offset}px)` }, { opacity: 1, transform: 'translateX(0)' }], options).finished.catch(() => {});
        // A child render may supersede sync while the transition finishes. Always release this snapshot.
        snapshot?.remove();
        if (!valid()) return;
    }
    const failure = s.content.querySelector('[data-login-failure]');
    const open = failure?.closest('[data-open]')?.dataset.open === 'true';
    if (open !== s.failureOpen) {
        s.failureOpen = open;
        root.classList.add('feedback-resizing');
    }
    const settling = s.content.getAnimations({ subtree: true }).filter(animation => animation.effect?.getTiming().iterations !== Infinity);
    await Promise.allSettled(settling.map(animation => animation.finished));
    await frame();
    if (!valid()) return;
    s.viewport.style.height = `${s.content.offsetHeight}px`;
    root.classList.remove('feedback-resizing');
    if (s.focusRequested === key) {
        s.content.querySelector('h1, [data-auth-heading], input')?.focus({ preventScroll: true });
        s.focusRequested = undefined;
    }
    if (!open || reducedMotion()) { s.pulse?.cancel(); return; }
    if (s.pulse?.effect?.target === failure && s.pulse.playState === 'running') return;
    s.content.querySelector('[data-login-error]')?.focus({ preventScroll: true });
    await frame();
    if (!valid()) return;
    s.pulse?.cancel();
    s.pulse = failure.animate([
        { boxShadow: '0 0 0 0 rgba(207,73,64,0)', offset: 0 },
        { boxShadow: '0 0 0 0 rgba(207,73,64,0)', offset: .24 },
        { boxShadow: '0 0 0 0 rgba(207,73,64,.175)', offset: .25, easing: 'ease-out' },
        { boxShadow: '0 0 0 9px rgba(207,73,64,0)', offset: 1 }
    ], { duration: 1800, iterations: Infinity, easing: 'linear' });
}

// Called after Blazor has rendered the closing panel. Use its real transition duration,
// including reduced-motion styles, rather than delaying authentication by a fixed timer.
export async function waitForFeedbackExit(root) {
    const s = boxes.get(root);
    if (!s) return;
    s.pulse?.cancel();
    await frame();
    const closing = [...s.content.querySelectorAll('.auth-feedback-slot')]
        .flatMap(slot => slot.getAnimations())
        .filter(animation => animation.effect?.getTiming().iterations !== Infinity);
    await Promise.allSettled(closing.map(animation => animation.finished));
    await frame();
}

// Blazor's in-memory history entry value is empty after a full reload; this marker survives it.
export function tryHistoryBack() {
    if (!history.state?.shiftIdentityLogin) return false;
    history.back();
    return true;
}

export function dispose(root) {
    const s = boxes.get(root);
    if (!s) return;
    ++s.revision;
    s.observer.disconnect();
    s.pulse?.cancel();
    s.outgoing.replaceChildren();
    boxes.delete(root);
}
