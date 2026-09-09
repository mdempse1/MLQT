// Does the diff view's pane synchronisation cut short a smooth scroll?
//
// A model of two scrolling panes, faithful in the three respects that decide the answer:
//   * a wheel tick starts an ANIMATION that advances over several frames, as WebKitGTK does and
//     WebView2 does not - which is the whole reason the defect was Linux-only;
//   * assigning scrollTop to an element that is animating CANCELS the animation;
//   * scroll events are dispatched during the frame's rendering step, and the scroll steps run
//     BEFORE animation-frame callbacks - so an echo caused in frame N arrives in frame N+1.
//
// Written because reasoning about it produced a fix that changed nothing (B139). The first model of
// this had the panes jump instantly, where the echo carries the same value and is harmlessly skipped
// by a value check - so it reported the broken and the unbroken version as equally good. Nothing
// that leaves the animation out can tell the two apart.
//
// Run it against any version of the file, including an old one:
//
//   node build/diff-scroll-simulation.js MLQT.Shared/wwwroot/diffViewer.js
//   git show <rev>:MLQT.Shared/wwwroot/diffViewer.js > /tmp/old.js
//   node build/diff-scroll-simulation.js /tmp/old.js
//
// The shipped version travels the full 265px asked for, with no cancellations, and the panes stay in
// sync when either one is scrolled. The version before the fix travels 93px and is cancelled 5 times.
//
// There is no node in this repository's toolchain; Playwright ships one, at
// MLQT.Journeys/bin/<config>/net10.0/.playwright/node/linux-x64/node. This is a diagnostic to re-run
// when the file is touched, not a gate - see B135 for why WebKit cannot be driven on this machine.

// Resolved, so a plain relative path works: require() would otherwise read it as a module name.
const path = require('path').resolve(process.argv[2]);
global.window = {};
let rafs = [];
global.requestAnimationFrame = cb => rafs.push(cb);
global.performance = { now: () => clock };
let clock = 0;
require(path);

function makePane(name) {
  const listeners = [];
  return {
    name, _top: 0, target: 0, animating: false, cancelled: 0, delivered: [],
    addEventListener: (_, h) => listeners.push(h), removeEventListener: () => {},
    get scrollTop() { return this._top; },
    set scrollTop(v) {
      if (this.animating) { this.animating = false; this.cancelled++; }  // an outside write kills it
      if (v !== this._top) { this._top = v; pending.push({ pane: this, listeners }); }
    },
    get scrollLeft() { return 0; }, set scrollLeft(v) {},
    wheel(px) { this.target = this._top + px; this.animating = true; },
    step() {                                    // advance the animation by one frame
      if (!this.animating) return;
      const remaining = this.target - this._top;
      const move = Math.abs(remaining) <= 8 ? remaining : remaining * 0.35;
      this._top += move;
      if (Math.abs(this.target - this._top) < 0.5) { this._top = this.target; this.animating = false; }
      pending.push({ pane: this, listeners });
    },
  };
}

let pending = [];
const left = makePane('left'), right = makePane('right');
window.diffViewer.initSyncScroll(left, right);

// Five wheel ticks, one every three frames, 60 frames in all.
for (let f = 0; f < 60; f++) {
  clock += 16;
  if (f % 3 === 0 && f < 15) left.wheel(53);
  left.step(); right.step();                    // animations advance
  const events = pending; pending = [];         // "run the scroll steps"
  events.forEach(e => e.listeners.forEach(h => h()));
  const cbs = rafs; rafs = [];                  // then animation-frame callbacks
  cbs.forEach(cb => cb());
}
console.log(`left ${Math.round(left._top)} / right ${Math.round(right._top)} - ${Math.round(left._top) === Math.round(right._top) ? 'IN SYNC' : 'OUT OF SYNC'}; cancelled ${left.cancelled}`);

// Now the other direction: the user scrolls the RIGHT pane once left has settled.
for (let f = 0; f < 30; f++) {
  clock += 16;
  if (f === 2) right.wheel(53);
  left.step(); right.step();
  const ev = pending; pending = [];
  ev.forEach(e => e.listeners.forEach(h => h()));
  const cbs2 = rafs; rafs = [];
  cbs2.forEach(cb => cb());
}
console.log(`  after scrolling the right pane: left ${Math.round(left._top)} / right ${Math.round(right._top)} - ${Math.round(left._top) === Math.round(right._top) ? 'IN SYNC' : 'OUT OF SYNC'}; right cancelled ${right.cancelled}`);
