// The error panels and forms of the security screens (AuthError, AuthForm).

// A hidden page does not paint, so its frames and transitions can wait until it is shown again. The timers let an
// attempt go on meanwhile.
const frame = () => new Promise(resolve => {
    const timer = setTimeout(resolve, 100);
    requestAnimationFrame(() => { clearTimeout(timer); resolve(); });
});
const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
const longestTransition = 1000;

// The shortest time a closed error panel stays away before the attempt goes on. With reduced motion a panel closes at
// once, and a failure that came back a few milliseconds later would look as if nothing had happened.
export const minimumAbsence = 250;

// Called after Blazor has rendered the panel closed: waits for its real closing transition, and at least the minimum
// absence, so that a repeated failure is seen to come back.
export async function waitForClose(slot) {
    const started = performance.now();
    await frame();
    const closing = slot.getAnimations().filter(animation => animation.effect?.getTiming().iterations !== Infinity);
    await Promise.race([Promise.allSettled(closing.map(animation => animation.finished)), pause(longestTransition)]);
    const left = minimumAbsence - (performance.now() - started);
    if (left > 0) await pause(left);
    await frame();
}

// After a press that the form's own checks refused, the first field that says what to fix gets the focus.
export async function focusInvalid(formId) {
    await frame();
    document.getElementById(formId)?.querySelector('[aria-invalid="true"]')?.focus();
}
