window.diffViewer = (function () {
    let _leftEl = null, _rightEl = null;
    let _leftHandler = null, _rightHandler = null;

    function dispose() {
        if (_leftEl && _leftHandler) _leftEl.removeEventListener('scroll', _leftHandler);
        if (_rightEl && _rightHandler) _rightEl.removeEventListener('scroll', _rightHandler);
        _leftEl = null; _rightEl = null;
        _leftHandler = null; _rightHandler = null;
    }

    // Keeps the two panes level. The guard is the whole of the difficulty, and the obvious way to
    // write it does not work:
    //
    //     syncing = true; other.scrollTop = this.scrollTop; syncing = false;
    //
    // Scroll events are dispatched asynchronously - queued and delivered during the rendering step,
    // never synchronously from the assignment - so `syncing` is already false again by the time the
    // other pane's handler runs. It suppresses nothing, and the two panes scroll each other back and
    // forth on every tick.
    //
    // On WebView2 that was invisible. On WebKitGTK the wheel scrolls smoothly, over several frames,
    // and assigning scrollTop to a scrolling element CANCELS that animation - so each wheel tick
    // started an animation, echoed back off the other pane, and was cancelled a frame or two in.
    // The pane crawled: a few pixels per tick, in a view that is used by scrolling through a whole
    // file. Reported against the Linux build by comparing it with the Windows one (B139).
    //
    // Releasing the flag on the next frame is what makes it a guard: the echoed event has been
    // delivered and ignored by then. Assigning only when the value actually differs matters for the
    // same reason - a redundant write is still a write, and still cancels an animation.
    function follow(from, to, isSyncing, setSyncing) {
        if (isSyncing()) return;
        setSyncing(true);
        if (to.scrollTop !== from.scrollTop) to.scrollTop = from.scrollTop;
        if (to.scrollLeft !== from.scrollLeft) to.scrollLeft = from.scrollLeft;
        requestAnimationFrame(() => setSyncing(false));
    }

    function initSyncScroll(leftEl, rightEl) {
        dispose();
        if (!leftEl || !rightEl) return;

        let syncing = false;
        const is = () => syncing;
        const set = v => { syncing = v; };

        _leftHandler = () => follow(leftEl, rightEl, is, set);
        _rightHandler = () => follow(rightEl, leftEl, is, set);

        leftEl.addEventListener('scroll', _leftHandler, { passive: true });
        rightEl.addEventListener('scroll', _rightHandler, { passive: true });
        _leftEl = leftEl;
        _rightEl = rightEl;
    }

    return { initSyncScroll, dispose };
})();
