window.diffViewer = (function () {
    let _leftEl = null, _rightEl = null;
    let _leftHandler = null, _rightHandler = null;

    function dispose() {
        if (_leftEl && _leftHandler) _leftEl.removeEventListener('scroll', _leftHandler);
        if (_rightEl && _rightHandler) _rightEl.removeEventListener('scroll', _rightHandler);
        _leftEl = null; _rightEl = null;
        _leftHandler = null; _rightHandler = null;
    }

    function initSyncScroll(leftEl, rightEl) {
        dispose();
        if (!leftEl || !rightEl) return;
        let syncing = false;
        _leftHandler = () => {
            if (!syncing) { syncing = true; rightEl.scrollTop = leftEl.scrollTop; rightEl.scrollLeft = leftEl.scrollLeft; syncing = false; }
        };
        _rightHandler = () => {
            if (!syncing) { syncing = true; leftEl.scrollTop = rightEl.scrollTop; leftEl.scrollLeft = rightEl.scrollLeft; syncing = false; }
        };
        leftEl.addEventListener('scroll', _leftHandler);
        rightEl.addEventListener('scroll', _rightHandler);
        _leftEl = leftEl;
        _rightEl = rightEl;
    }

    return { initSyncScroll, dispose };
})();
