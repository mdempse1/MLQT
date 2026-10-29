// Changes to MudBlazor's stylesheet that MLQT cannot make any other way. Each one says what it
// works around and what would let it go.
window.mudBlazorWorkarounds = (function () {
    // A selector of the form `tr:not(:has(>*))`, which MudBlazor 9.9.0 added to its sticky-header
    // table rules to place the loading bar under an empty header row. Under WebKit - Playwright's,
    // and WebKitGTK, which is the Linux desktop host - the rule's mere presence in a stylesheet makes
    // every child inserted anywhere in the document re-check it: with no sticky table on the page,
    // and the code viewer nowhere near one, recolouring a 5,000-line class (about 230,000
    // insertions) took 75-110 s, against 3-4 s with the rule gone. Chromium is unaffected.
    // LargeClassLineLimitJourney under the nightly WebKit run is what found it.
    //
    // Only the selectors carrying it are dropped; the rest of each rule stays. What is lost is the
    // loading bar's position in a sticky-header table whose header row is empty, which MLQT has none
    // of. Remove this when a MudBlazor release no longer has the selector - `removed` reading 0 on a
    // loaded page says so, and MudBlazorWorkaroundsJourney fails then to make sure somebody looks.
    const SLOW_SELECTOR = /:has\(\s*>\s*\*\s*\)/;

    let removed = 0;

    // A selector list split at its top-level commas: a comma inside :not(...) or :has(...) belongs
    // to the selector around it.
    function splitSelectors(list) {
        const parts = [];
        let depth = 0, start = 0;
        for (let i = 0; i < list.length; i++) {
            const c = list[i];
            if (c === '(') depth++;
            else if (c === ')') depth--;
            else if (c === ',' && depth === 0) { parts.push(list.slice(start, i).trim()); start = i + 1; }
        }
        parts.push(list.slice(start).trim());
        return parts;
    }

    function rewrite(rules) {
        for (let i = rules.length - 1; i >= 0; i--) {
            const rule = rules[i];
            if (rule.selectorText === undefined) {
                // @media, @supports and the like hold rules of their own.
                if (rule.cssRules) rewrite(rule.cssRules);
                continue;
            }
            if (!SLOW_SELECTOR.test(rule.selectorText)) continue;

            const kept = splitSelectors(rule.selectorText).filter(s => !SLOW_SELECTOR.test(s));
            const body = rule.style.cssText;
            const owner = rule.parentRule || rule.parentStyleSheet;
            owner.deleteRule(i);
            if (kept.length > 0) owner.insertRule(`${kept.join(', ')} { ${body} }`, i);
            removed++;
        }
    }

    function apply(sheet) {
        try {
            rewrite(sheet.cssRules);
        } catch (e) {
            // A sheet from another origin cannot be read. MudBlazor's is served by the host.
        }
    }

    function isMudBlazor(href) {
        return !!href && /\/MudBlazor\.min\.css(\?|$)/i.test(href);
    }

    // A script after the stylesheets waits for them, so the sheet is normally here already; the
    // load listener covers a host page that orders them differently.
    for (const link of document.querySelectorAll('link[rel="stylesheet"]')) {
        if (!isMudBlazor(link.href)) continue;
        if (link.sheet) apply(link.sheet);
        else link.addEventListener('load', () => apply(link.sheet), { once: true });
    }

    return {
        /** How many rules had a selector taken out - 0 once MudBlazor no longer ships it. */
        get removed() { return removed; }
    };
})();
