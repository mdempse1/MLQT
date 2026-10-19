# Scenario: a McPherson strut front axle, with OpenModelica

**Status: approved 2026-10-18; extended the same day to the front axle.**

## Where it comes from

A session on 2026-10-18 was asked, using only the MSL and OpenModelica, to build a multibody model of
a Kia EV4-sized car: McPherson struts at each corner, rear-wheel drive, front steering, a Pacejka
tyre, and test rigs for each subsystem. It took 57 minutes and made 200 tool calls. Of those, 93 went
to MLQT, 64 of them `create_class`, and 19 bypassed MLQT through the shell.

This scenario keeps the front of that car: one corner and its kinematic rig, then the axle built
from two of them. It needs the same things the whole session did: reading MSL MultiBody joints,
writing many classes, correcting them after a failed check, and the load-and-simulate loop with the
simulator.

**Why the axle was added.** The first version stopped at the corner. On the current tools it passed in 9
minutes with no edits outside MLQT and no `reload`, so it could not show the plan's step 1 halving
anything. The axle makes the agent **change classes it has already written**: one corner class has to
serve both sides, mirrored, and the knuckle has to meet a steering rack. That is the kind of editing
the real sessions did outside MLQT.

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
> +50 mm of bump.
>
> Then build the front axle from two of these corners, the right-hand one a mirror image of the left,
> joined by a steering rack that both tie rods connect to. Change the corner classes where you need to
> so that one corner class serves both sides, rather than copying it. Build two more rigs:
> - a parallel-bump rig that moves both wheel centres together from -80 mm to +80 mm;
> - a steering rig that moves the rack from -30 mm to +30 mm with the wheels at design height.
>
> Simulate both, and tell me the camber and toe change of each wheel at +50 mm of parallel bump, and
> the left and right wheel steer angles at 30 mm of rack travel. Put all the work in a new library
> called CornerTest in this folder.

**If the agent asks:**
- Which hard points? "Typical values for a car of that size; state the ones you chose."
- Which tyre? "None is needed for a kinematic rig."
- Which way is positive rack travel? "Your choice; state it."

## Done when

1. A library `CornerTest` exists in the folder, with the corner, the record and the rig as separate
   classes.
2. OpenModelica checks the corner rig without errors.
3. The corner rig simulates over the whole travel.
4. The answer gives the corner's camber and toe change at +50 mm bump, with units and sign convention.
5. The values are plausible for a McPherson strut: a camber change of a few tenths of a degree to
   about two degrees over 50 mm, and a toe change well under a degree.
6. The axle uses **one** corner class for both sides, mirrored by its parameters or a modifier. There
   is no second copy of the corner, the knuckle or the strut. The corner rig still checks and
   simulates after the change.
7. Both axle rigs check and simulate over their whole travel.
8. The answer gives camber and toe for both wheels at +50 mm parallel bump, which mirror each other:
   equal magnitudes, signs as the stated convention says. It also gives both wheel angles at 30 mm of
   rack travel, with the inner wheel steering more than the outer.

## Results

The first row is the corner-only version, before the axle was added, so its *Done when* is out of 5.
The rest are out of 8.

| Date | MLQT commit | Toolset | Done when | Minutes | MLQT calls | MLQT errors | Bypass writes | Bypass reads | `reload` | Tokens in | Tokens out |
|---|---|---|--:|--:|--:|--:|--:|--:|--:|--:|--:|
| 2026-10-18 | fa80416 | current, corner only | 5 of 5 | 9 | 22 | 0 | 0 | 1 | 0 | 2,938,085 | 54,203 |
| 2026-10-18 | fa80416 | current | 8 of 8 | 11 | 26 | 0 | 9 | 3 | 1 | 4,412,294 | 63,417 |
| 2026-10-19 | e6e94ba | current + step 1 | 8 of 8 | 10 | 12 | 0 | 2 | 0 | 0 | 2,537,480 | 57,088 |

**2026-10-18, current toolset, corner only.** Run headless (`claude -p`, Bash and PowerShell allowed),
cost $2.37. All five items held: OpenModelica checked the rig (3,365 equations and variables) and
simulated it, and the answer gave −0.90° camber and −0.10° toe at +50 mm bump. MLQT did all the
authoring: 12 `create_class`, 2 `update_class_source` and 1 `set_component_modifier`. The only bypass
was a directory listing. To try four inner tie-rod heights, the agent wrote throwaway variants into
the simulator with `load_string`, then wrote the chosen one back through MLQT.

**2026-10-18, current toolset: the baseline for steps 1 and 3.** Run the same way, cost $2.94. All
eight items held.
- `McPhersonCorner` gained a `left` parameter that mirrors every hard point. `AlignmentSensor` gained
  a matching one, and `FrontAxle` uses two instances of the corner.
- All three rigs simulated over their whole travel. OpenModelica's check was run on the corner rig
  only; the axle rigs were simulated without one.
- Both wheels gave −1.07° camber and +0.057° toe at +50 mm, mirrored. At 30 mm of rack travel the
  inside wheel steered 14.02° and the outside 13.08°.

**Extending the corner classes went round MLQT, as the plan expected.**
- **Writes:** 9 `Edit` calls on `.mo` files, to `McPhersonCorner`, `AlignmentSensor`, `CornerData`,
  `Strut` and a rig. MLQT made 2 `update_class_source` calls and 1 `batch_edit`.
- **Reads:** 2 `Read` and 1 `Grep` on `.mo` files, to find the text to patch.
- **`reload`:** 1, to bring MLQT back in step.
- **Other shell work, not counted above:** a Python script written to cross-check the bump-steer
  geometry, the PowerShell calls that ran it, and a folder listing.

Step 1's criterion compares against this row: at least half fewer bypass writes and `reload` calls.

**2026-10-19, after step 1.** Run the same way, cost $2.38. All eight items held: the corner gained a
`side` parameter (`left` is `side=1`, `right` is `side=-1`), all three rigs checked and simulated, the
wheels gave −0.90° camber and −0.09° toe at +50 mm, mirrored, and at 30 mm of rack the inside wheel
steered 13.48° and the outside 12.82°.
- **The library was changed only through MLQT**: 3 `write_class` and 2 `edit_class`, where step 0
  had 9 `Edit` calls on its `.mo` files. The 2 bypass writes were a throwaway model the agent wrote
  in its own scratch folder to sweep a parameter in the simulator, not the library.
- **No reads outside MLQT and no `reload`**, against 3 and 1.
- **The criterion is met**: bypass writes 9 → 2 and `reload` 1 → 0. MLQT calls fell from 26 to 12, input
  tokens from 4.4 M to 2.5 M.
