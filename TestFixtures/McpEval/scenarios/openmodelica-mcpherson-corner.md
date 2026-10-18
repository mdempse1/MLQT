# Scenario: a McPherson strut corner, with OpenModelica

**Status: draft for review.**

## Where it comes from

A session on 2026-10-18 was asked, using only the MSL and OpenModelica, to build a multibody model of
a Kia EV4-sized car: McPherson struts at each corner, rear-wheel drive, front steering, a Pacejka
tyre, and test rigs for each subsystem. It took 57 minutes and made 200 tool calls. Of those, 93 went
to MLQT, 64 of them `create_class`, and 19 bypassed MLQT through the shell. This scenario keeps one
part of that session: one corner and its kinematic test rig. That part needs the same things the whole
did: reading MSL MultiBody joints, writing many classes, correcting them after a failed check, and
the load-and-simulate loop with the simulator.

## Needs

- OpenModelica 1.27, with the MSL 4.1.0 installed in its library path
  (`%AppData%\.openmodelica\libraries`).
- The OpenModelica MCP server, and the MLQT MCP server. No other server that touches Modelica.

## Prompt

> Using only the Modelica Standard Library and OpenModelica, build a multibody model of one front
> corner of a car about the size of a Kia EV4, with a McPherson strut. It needs a lower control arm, a
> strut with a coil spring and damper, a steering knuckle with a tie rod, and the wheel carrier. Put
> the corner's hard points, stiffnesses and damping in a record. Then build a kinematic test rig that
> moves the wheel centre vertically from -80 mm to +80 mm, and reports the camber and toe change
> against wheel travel. Simulate the rig in OpenModelica, and tell me the camber and toe change at
> +50 mm of bump. Put the work in a new library called CornerTest in this folder.

**If the agent asks:**
- Which hard points? "Typical values for a car of that size; state the ones you chose."
- Which tyre? "None is needed for a kinematic rig."

## Done when

1. A library `CornerTest` exists in the folder, with the corner, the record and the rig as separate
   classes.
2. OpenModelica checks the rig without errors.
3. The rig simulates over the whole travel.
4. The answer gives camber and toe change at +50 mm bump, with units and sign convention.
5. The values are plausible for a McPherson strut: a camber change of a few tenths of a degree to
   about two degrees over 50 mm, and a toe change well under a degree.

## Results

| Date | MLQT commit | Toolset | Done when (of 5) | Minutes | MLQT calls | MLQT errors | Bypass writes | Bypass reads | `reload` | Tokens in | Tokens out |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| | | current | | | | | | | | | |
