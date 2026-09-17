window.diffViewer = (function () {
    let _leftEl = null, _rightEl = null;
    let _leftHandler = null, _rightHandler = null;

    // How long one pane keeps the right to drive the other, refreshed on every scroll event it
    // produces. Long enough to cover a smooth-scroll animation's gaps between frames, short enough
    // that moving the mouse to the other pane and scrolling it feels immediate.
    const OWNERSHIP_MS = 150;

    let _owner = null, _ownerAt = -Infinity;

    function dispose() {
        if (_leftEl && _leftHandler) _leftEl.removeEventListener('scroll', _leftHandler);
        if (_rightEl && _rightHandler) _rightEl.removeEventListener('scroll', _rightHandler);
        _leftEl = null; _rightEl = null;
        _leftHandler = null; _rightHandler = null;
        _owner = null; _ownerAt = -Infinity;
    }

    // Keeps the two panes level. The difficulty is entirely in telling a scroll the user caused from
    // the echo of one we caused ourselves, and two obvious ways of doing it both fail:
    //
    //   1. A flag set and cleared around the assignment suppresses nothing. Scroll events are
    //      dispatched asynchronously, during the frame's rendering step, never synchronously from the
    //      assignment - so the flag is false again long before the echo arrives.
    //   2. Clearing that flag on the next animation frame does not fix it either, and this is the
    //      part that is easy to get wrong: within a frame the scroll steps run BEFORE animation-frame
    //      callbacks, so an echo caused in frame N is delivered in frame N+1, after the callback has
    //      already released the flag.
    //
    // Comparing values does not save it. While a pane is scrolling smoothly its scrollTop moves every
    // frame, so the echo arrives carrying a position that is now STALE, the values differ, and it is
    // written - dragging the pane backwards and, because assigning scrollTop to a scrolling element
    // cancels its animation, killing the scroll the user just started.
    //
    // That is the whole of B139: on WebView2, where the wheel moves instantly, the echo carries the
    // same value and is skipped harmlessly. On WebKitGTK the wheel scrolls over several frames and
    // every tick was cut short. Simulated against a model that advances an animation frame by frame:
    // five wheel ticks asking for 265px travelled 93px with five cancellations, and travel the full
    // 265px with none once ownership decides it.
    //
    // So: whichever pane the user is scrolling owns the pair for a moment, and only the owner drives.
    // An echo is by definition not from the owner, so it is ignored without having to recognise it.
    function follow(from, to) {
        const now = performance.now();

        if (_owner !== from && now - _ownerAt < OWNERSHIP_MS)
            return;

        _owner = from;
        _ownerAt = now;

        if (to.scrollTop !== from.scrollTop) to.scrollTop = from.scrollTop;
        if (to.scrollLeft !== from.scrollLeft) to.scrollLeft = from.scrollLeft;
    }

    function initSyncScroll(leftEl, rightEl) {
        dispose();
        if (!leftEl || !rightEl) return;

        _leftHandler = () => follow(leftEl, rightEl);
        _rightHandler = () => follow(rightEl, leftEl);

        leftEl.addEventListener('scroll', _leftHandler, { passive: true });
        rightEl.addEventListener('scroll', _rightHandler, { passive: true });
        _leftEl = leftEl;
        _rightEl = rightEl;
    }

    return { initSyncScroll, dispose };
})();
