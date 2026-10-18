# Scenario: an overtake mode for a Formula 1 car's energy recovery, with Dymola

**Status: draft for review.**

## Where it comes from

A session on 2026-10-17 built a 2026 Formula 1 car in Dymola, as a library `Formula1` beside the three
commercial vehicle libraries it builds on. It made 416 tool calls in 86 minutes:
107 to MLQT, 61 of them `create_class`, and 74 that bypassed MLQT through the shell, mostly grep, sed
and Python edits. That `Formula1` library is about 60 classes:
- an ERS controller and energy manager, the MGU-K and its power limit, and the energy store;
- engine, gearbox, brakes, active aero, tyres, a driver with a scripted steering wheel;
- the car, a demo circuit, a flying-lap experiment and component tests.

**This scenario starts from that library rather than from nothing.** An energy recovery system written
from nothing would lack the car, the driver and the lap that give it meaning. A change to the one that
exists keeps that context. It also aims at the largest gap the plan found, **editing existing classes**:
most of the session's bypassing was patching classes it had already written. One of those patches added
an `overtakeMode` parameter to `ERSController` with a Python find-and-replace.

## Needs

- **The machine where Dymola is licensed.** It is not licensed on the machine the harness runs on, so
  this scenario runs there. Run `analyse` there too, or copy the session's transcript (under
  `~/.claude/projects/`) and the MLQT log back.
- **The Dymola release and the versions of the commercial libraries the original session used.** Every run of this
  scenario must use the same ones, or its results cannot be compared. They are recorded in the run
  notes on the test machine, not in this repository.
- **A snapshot of the `Formula1` library** as the 2026-10-17 session left it. It **must never be
  committed to this repository**, because it contains classes copied from a commercial Modelica
  library and modified. It stays on the test machine, and so do the commercial libraries. Each run copies
  the snapshot into the run folder, beside the three commercial libraries, as the original
  session had them. Where the snapshot is kept is in the run notes too.
- **A startup script** that opens the four libraries, with the Dymola MCP server configured to run it.
  The original session had none. It is added because loading what a startup script opens is a step
  the plan adds for Dymola, and this is the scenario that can test it. The script, anonymised, can also
  be one of the script-reader fixtures.
- The MLQT MCP server, and the Dymola MCP server. No other server that touches Modelica.

## Prompt

> Dymola is started with the startup script at `<startup script>`, which opens the libraries this
> project uses, including the library Formula1 in this folder. Formula1 models a 2026 Formula 1 car.
> Add a driver-selectable overtake mode to its energy recovery system. When the driver selects it, the
> MGU-K may deploy an extra 0.5 MJ per lap. Every other limit stays as it is, including the power limit
> and how the power tapers with speed. The extra energy lets the car keep accelerating harder for
> longer. Expose the mode as a parameter of the driver profile, wire it through to the ERS controller,
> and add a test of the controller that shows the difference. Then simulate the flying-lap experiment
> with and without overtake mode, and tell me the lap time, the energy deployed and the final state of
> charge for each.

**If the agent asks:**
- Should any other limit change? "No. Only the energy that may be deployed per lap, by 0.5 MJ."
- May it change other classes? "Yes, wherever the mode has to reach."

## Done when

1. The driver profile has an overtake-mode parameter, and it reaches `ERSController` through the
   classes in between. No class is duplicated to do it.
2. A new test of the controller shows its deployment allowance with the mode on and off, differing
   by 0.5 MJ per lap. The power limit and its taper are the same in both.
3. MLQT has the four libraries the startup script opens loaded, from the run folder.
4. Dymola checks the flying-lap experiment without errors, and simulates it both ways.
5. The answer gives lap time, energy deployed and final state of charge for both runs. With overtake
   mode, the energy deployed is higher by no more than 0.5 MJ, and the lap is no slower.

## Results

| Date | MLQT commit | Toolset | Done when (of 5) | Minutes | MLQT calls | MLQT errors | Bypass writes | Bypass reads | `reload` | Tokens in | Tokens out |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| | | current | | | | | | | | | |
