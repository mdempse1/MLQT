window.spellCheck = (function () {
    let _ref = null;
    let _handler = null;
    // Bounding rect (viewport coords) of the most recently right-clicked misspelled word. Used to
    // anchor the correction menu below the word and to clamp it within the viewport.
    let _anchorRect = null;

    function dispose() {
        if (_handler) {
            document.removeEventListener('contextmenu', _handler, true);
        }
        _handler = null;
        _ref = null;
        _anchorRect = null;
    }

    // Registers a delegated contextmenu listener. Because the highlighted words are rendered as
    // raw HTML (MarkupString) inside the code viewer, Blazor's @oncontextmenu cannot bind to them,
    // so we listen on the document and dispatch to .NET when a .code-misspell span is right-clicked.
    function init(dotNetRef) {
        dispose();
        _ref = dotNetRef;
        _handler = function (e) {
            const span = e.target && e.target.closest ? e.target.closest('.code-misspell') : null;
            if (!span) return;
            e.preventDefault();
            const word = span.getAttribute('data-word') || '';
            // Anchor to the word itself (not the mouse) so the menu can sit below the word instead
            // of covering it. The rect is clamped to the viewport once the menu has rendered.
            const r = span.getBoundingClientRect();
            _anchorRect = { left: r.left, top: r.top, bottom: r.bottom, right: r.right };
            _ref.invokeMethodAsync('OnMisspelledWordRightClick', word, r.left, r.bottom);
        };
        document.addEventListener('contextmenu', _handler, true);
    }

    // Computes the correction menu's position: anchored below the right-clicked word, aligned to
    // its left edge, then clamped so the menu never runs off any screen edge. If the menu would
    // overflow the bottom, it flips to sit above the word when there is room. Called after the menu
    // has rendered (so its real width/height are known) and returns [left, top] in CSS pixels for
    // .NET to apply — making .NET the source of truth so later re-renders keep the clamped spot.
    function positionContextMenu(menuSelector, gap) {
        const menu = document.querySelector(menuSelector);
        if (!menu || !_anchorRect) return null;
        const m = menu.getBoundingClientRect();
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const margin = 8;
        gap = gap || 4;

        // Horizontal: align with the word's left edge, then keep within [margin, vw - margin].
        let left = _anchorRect.left;
        if (left + m.width + margin > vw) left = vw - m.width - margin;
        if (left < margin) left = margin;

        // Vertical: prefer below the word; flip above if it would overflow the bottom and there is
        // room above; otherwise clamp to the bottom margin.
        let top = _anchorRect.bottom + gap;
        if (top + m.height + margin > vh) {
            const above = _anchorRect.top - gap - m.height;
            top = above >= margin ? above : Math.max(margin, vh - m.height - margin);
        }

        return [left, top];
    }

    // Reads the current scroll offsets of the code viewer so they can be restored after a
    // correction reloads and re-renders the file (otherwise the view jumps back to the top-left).
    // Returns [scrollTop, scrollLeft, scrollHeight, clientHeight] — the last two let the caller
    // log whether the element was actually scrollable when captured.
    function getScroll(selector) {
        const el = document.querySelector(selector);
        if (!el) return [0, 0, 0, 0];
        return [el.scrollTop, el.scrollLeft, el.scrollHeight, el.clientHeight];
    }

    // Restores previously captured scroll offsets on the code viewer. When called straight from
    // Blazor's OnAfterRender, the freshly-remounted content is usually not laid out yet, so a
    // synchronous assignment clamps against a stale (too-small) scrollHeight and does not stick.
    // We therefore retry across animation frames until the offset takes (within 1px), the content
    // genuinely can't scroll that far, or we exhaust the frame budget (~0.5s). Re-querying each
    // frame also tolerates a later remount of the element. No-ops if the element is absent.
    function setScroll(selector, top, left) {
        let attempts = 0;
        const tick = function () {
            const el = document.querySelector(selector);
            if (el) {
                el.scrollTop = top;
                el.scrollLeft = left;
                const stuck = Math.abs(el.scrollTop - top) <= 1 && Math.abs(el.scrollLeft - left) <= 1;
                const maxedVert = (el.scrollHeight - el.clientHeight) <= el.scrollTop + 1;
                const maxedHorz = (el.scrollWidth - el.clientWidth) <= el.scrollLeft + 1;
                if (stuck || (maxedVert && maxedHorz)) return;
            }
            if (++attempts < 30) requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    }

    // Scrolls the first highlighted misspelled-word span matching `word` into the centre of the
    // code viewer. Retries across animation frames because, when a spelling issue selects a
    // different model, the highlight spans are added a beat after the code lines (the misspelled-
    // word set is recomputed just after the render). Re-querying each frame also tolerates the
    // viewer being remounted. Matches data-word by value (rather than an attribute selector) so
    // words containing quotes or apostrophes need no escaping. No-ops if nothing matches in budget.
    function scrollWordIntoView(viewerSelector, word) {
        let attempts = 0;
        const tick = function () {
            const viewer = document.querySelector(viewerSelector);
            if (viewer) {
                const spans = viewer.querySelectorAll('.code-misspell');
                for (let i = 0; i < spans.length; i++) {
                    if (spans[i].getAttribute('data-word') === word) {
                        spans[i].scrollIntoView({ block: 'center', inline: 'nearest' });
                        return;
                    }
                }
            }
            if (++attempts < 30) requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
    }

    return { init, dispose, positionContextMenu, getScroll, setScroll, scrollWordIntoView };
})();
