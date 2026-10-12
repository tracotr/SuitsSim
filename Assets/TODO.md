# SUITS VR Sim — Build Order

Organised by the 10-week timeline in Assignment 2 (Development Timeline). Each week has a **Done when** test and ends with a progress report: what was planned, a video / live demo, how it works, and what blocked anything unfinished.

Reference: FY27 Mission Description, `TSS2026/documents/procedures/ev-team-procedure-timeline.pdf` (egress / ingress / error procedures), `TSS2026/documents/telemetry_ranges/eva-telemetry-ranges.pdf`.

Key scripts (`Assets/Scripts/`):
- `TSS/TssSim.cs` — the simulated TSS (singleton, no JSON). `GetSwitch` / `SetSwitch(TssSwitch, bool)`, `Eva1.Get(TssValue)`, error flags, `Ticked` / `SwitchChanged` / `ErrorChanged` events, `Advance(seconds)`
- `TSS/EvaData.cs` — telemetry fields as a ScriptableObject (`Assets/Resources/EvaData.asset`), `TssValue` enum
- `TSS/Procedure.cs` + `TSS/ProcedureRunner.cs` — procedure assets and the step runner
- `TSS/TssToggle.cs` — connects an `XRLever` (grab and drag) to its TSS switch
- `Imports/XRContent/XRLever.cs` — lever interactable copied unchanged from Unity's VR Template
- `Hands/LeverHandSnap.cs` — keeps the drawn hand on a grabbed lever
- `DCU/ChestMount.cs` — keeps the DCU on the player's chest
- `Suit/HeadLamp.cs` — helmet-side lamp that follows the head

Rule for new views (task list, C&W, telemetry, minimap): subscribe to events from the start, never read `TssSim` in `Update`.

---

## Done before Week 1 — foundation

### Scene + movement
- [x] XR Origin Hands rig (XRI Hands Interaction Demo sample)
  - [x] Install XR Hands package
  - [x] OpenXR features: Hand Tracking Subsystem, Meta Hand Tracking Aim (no Hand Interaction Profile: double aim source over Link)
  - [x] Tracking Origin Mode = Floor, XR Origin scale = 1
- [x] `TSS` GameObject with `TssSim` + `ProcedureRunner` (replaces the old JSON `SimTss`)
  - [x] `Default Eva Data` = `Assets/Resources/EvaData.asset` (⋮ → Reset fills the real TSS values)
  - [x] Runs before other scripts via `[DefaultExecutionOrder(-100)]` (no manual Script Execution Order)
- [x] `ImuFeed` with `head` = Main Camera → `TssSim.SetImu`
- [x] Graybox lander at Unity origin, with airlock door and a wall for the UIA
- [x] Movement (no controllers)
  - [x] Room-scale walking
  - [x] Hand-gesture teleport (HandTeleportInteractor from the Hands demo): palm-up pose activates it, pinch to lock, release to teleport, open hand cancels
    - **Changed:** the ray is aimed by the head, not the hand (Ray Origin Transform = Main Camera), and the line is hidden (Line Width 0, Cast Shadows = Shadows Only); only the reticle shows
    - Line renderers need a URP material (Single Pass Instanced draws legacy shaders in one eye only)
    - Rig teleport Release Threshold 0.2 (stock 0.01 lags over Link)
  - [x] Cap teleport distance so breadcrumbs and EVA time stay meaningful
    - **Changed:** 4 m → 10 m (Max Raycast Distance), because a 4 m ray from head height only reaches ~3.6 m of floor
  - [x] ~~Teleport ray origin out of a straight index finger~~ **Changed:** replaced by head aim (above), so the pinch can't move the ray
    - Palm-up activation tolerance is 50°, and the gesture has a 0.35 s lose-shape grace
- [x] Desktop testing: XR Interaction Simulator sample imported (disable it before using the headset)
- [x] Suit headlamp: `Head Lamp` spot light in MainScene, `Suit/HeadLamp.cs` keeps it on the right side of the helmet aimed where the head looks (offset / aim in the Inspector)

### Boards (UIA + DCU)
- [x] Reusable switch component: `TssToggle` (UIAToggle / DCUToggle prefabs)
  - [x] `sw` field = `TssSwitch` enum, grouped dropdown (`UIA/EVA 1/O2 (Open)`, `DCU/EVA 1/Fan (PRI)`…; the word in brackets is what On means)
  - [x] Levers are `XRLever` on the Pivot (Pivot → Handle → Capsule; Handle rotates around Pivot X, Min/Max Angle ±30, Lock To Value); grab or pinch and drag past the middle to flip, no physics
    - **Changed:** was a finger-poke push on a sprung HingeJoint. Poke (with and without a poke filter), physics finger colliders and hinge physics were tried and dropped: brushes flipped switches, a finger behind the lever dragged it back, the base could be pushed, and DCU motion flipped levers
  - Rule: everything above a lever's Handle must have uniform scale (non-uniform scale above a rotating object shears the mesh but not the collider). Shape the lever with the Capsule / Handle scale. The UIA panel is (0.5, 1, 1), so each UIA toggle is scaled X ×2 and its Capsule / Base / Pivot X halved to look the same
  - [x] Grabbed lever holds the drawn hand: `Hands/LeverHandSnap` on Left / Right Hand in the rig slides both hand visuals so the pinch point stays on the lever (fingers stay tracked); pulling the real hand > 12 cm away lets go
  - [x] Controller / simulator: grip grabs the lever and drags it, same as a hand
    - **Changed:** was "Select flips it"; there's no click-to-flip any more
  - [x] Board always shows TSS truth (`SwitchChanged` read-back, starts from `GetSwitch`)
  - [x] Click sound
  - [x] Controller can reach toggles: physics layer **Toggle** is on the Near-Far **near** caster mask only (far ray can't grab levers), and the near grab radius is 3 cm (sample: 10 cm, which grabbed levers from their labels) in `Left_NearFarInteractor.prefab` (XRI sample — redo if the sample is re-imported)
- [x] UIA panel on lander wall: EVA 1 EMU PWR (2001), O2 (2002), SUPPLY (2003), WASTE (2004); O2 VENT (2009), DEPRESS PUMP (2010)
- [x] DCU on the player's chest (`DCU.prefab`, `ChestMount`: follows head position + yaw only, kinematic Rigidbody)
  - [x] All six toggles assigned; On tilts toward the outer label (PRI / UMB / OPEN), off = SEC / LOCAL / CLOSE (TSS default)

  | Toggle | Switch | On | Off |
  |---|---|---|---|
  | BATT (top) | `DcuEva1Batt` 2011 | UMB | LOCAL |
  | OXY | `DcuEva1Oxy` 2012 | PRI | SEC |
  | BATT (bottom) | `DcuEva1BattSource` 2013 | PRI | SEC |
  | FAN (top) | `DcuEva1Fan` 2014 | PRI | SEC |
  | PUMP | `DcuEva1Pump` 2015 | OPEN | CLOSE |
  | CO2 (bottom) | `DcuEva1Co2` 2016 | PRI | SEC |

  - Left side is a mirrored *layout* (label strip moved to the other side, XRLever Max/Min Angle +30/−30 instead of DCUToggle's −30/+30), not negative scale
- [x] DCU refit: grab the DCU body (pinch / grip) and drag it up/down to fit the user's chest height
  - [x] Grab moves `ChestMount.offset.y` / `.z` (clamped by `heightRange` -0.7…-0.25 m, `depthRange` -0.15…0.2 m), not the transform directly
- [X] Switch visual feedback
  - [x] Hover highlight when a hand or controller is near a lever (`TssToggle.hoverColor`, URP `_BaseColor` tint)
    - **Changed:** far rays no longer hover or grab levers (Toggle layer is near caster only)
  - [x] Snap feel: XRLever snaps to the nearest end on release; click on flip
  - [x] Blink red when the ProcedureRunner flags that switch as unexpected (`UnexpectedSwitch` event, `TssToggle.warningColor` / `warningTime`)
- [X] Emissive labels readable in low light

Notes:
- BATT: TSS README says `batt.lu` true = suit battery, but the server code treats true = umbilical. Follow the code (On = UMB).
- DEPRESS PUMP has no sim effect (same as real TSS); suit pressurizes on its own after O2 prep.
- COMMS: dropped. The real TSS has no EV1 COMMS command and it affects nothing; add a `TssSwitch` entry only if a procedure needs it.

### TSS rewrite ✅
- [x] Rewrite as `TssSim` + `EvaData` ScriptableObject (no JSON), EV1 only, real TSS quirks kept
- [x] `TssSwitch` enum (EV1 DCU only, like the TSS), `TssValue` enum + `EvaData.Get`
  - **Changed:** the enum has no numbers any more; the 2001–2016 numbers in this file are just the real TSS command numbers for reference
- [x] Fix: formula fields (tank pressure, suit total…) computed in `Awake` / `ResetEva`, not 0 until the first tick
- [x] Coolant fill/drain verified (starts 0%, fills > 95% in ~17 s with PUMP + SUPPLY)
- [x] Battery primary/secondary (2013) kept — power fault needs it
- [x] Self Test passes (vent, both tank fills, coolant, suit O2 = 4, fan + O2 faults, reset)
- [x] Stop polling the TSS every tick: `TssSim.ErrorChanged` starts error procedures; wait steps re-check on their fields' `TssField.OnValueChanged`
- [x] Refactor the code for readability (style: references → settings → runtime values, `[SerializeField] private` + tooltips, OnEnable/OnDisable subscriptions, events over reading values)

### Procedure runner + egress
- [x] `Procedure` ScriptableObject (Create → TSS → Procedure) instead of JSON
  - Step = header + text + `SwitchReq` (On / Off / Flip) + `ValueReq` (`TssValue` Below / Above threshold); no requirements = manual Confirm
- [x] `ProcedureRunner`
  - [x] Auto-advance on `SwitchChanged`, and wait steps on their own fields' `OnValueChanged`; steps already met pass straight through ("Verify OXY – PRI")
    - **Changed:** no `Ticked` polling
  - [x] "Unexpected: …" warning on out-of-order flips (warn only, never blocks)
  - [x] Urgent error procedures interrupt and resume the paused one at the same step
  - [x] Progress kept per procedure: switch away and back without losing your place
  - [x] Self Test: plays egress, throws a fan fault mid-run, checks resume, zero warnings
  - [x] Tester sign-off: manual steps (verify / announce / return to PR) and steps with `requireConfirm` (e.g. Verify OXY – PRI) wait for Confirm — Confirm / Skip / Back buttons in the ProcedureRunner Inspector
  - [x] Complete Egress (Inspector button): plays egress live on the boards, 0.3 s per step
  - [x] Custom Inspectors (`Assets/Scripts/Editor/`): live step list, TSS status / switches (click to flip) / telemetry, test + fault buttons — test code lives there, not in the runtime scripts
- [x] `Eva1Egress.asset` — 31 steps, cross-checked against the PDF and replayed against the sim (~3 min)
  - [x] Step 21 text fixed; re-checked against the PDF (3/2/2026 version): all 31 steps match
- [x] EVA telemetry starts before egress (CAPCOM "START EVA Telemetry"): ProcedureRunner's CAPCOM setup calls `StartEva()` — vent/fill/coolant need it running
- [x] Error procedures (Urgent, auto-started by the runner on the TSS error flag)

  | Fault | Trigger | Procedure asset | Fix |
  |---|---|---|---|
  | Fan | Throw Fan Low | `Eva1OffNominalFan` | FAN → SEC, return to PR |
  | O2 low / high | Throw O2 Low / High | `Eva1OffNominalO2` | OXY → SEC, return to PR |
  | Scrubber | scrubber > 60% | `Eva1OffNominalScrubber` | flip CO2 |
  | Power | primary battery < 20% | `Eva1OffNominalBattery` | BATT → SEC |

Notes:
- Tank pressure caps at exactly 3000, so thresholds use > 2950 (as in the PDF), never > 3000.
- Sim timings: vent ~24 s, each tank fill ~40 s, coolant fill ~17 s; suit O2 reaches 4 during the coolant fill.

---

## Week 0 — cleanup before the timeline starts
- [ ] Test the `XRLever` levers in the headset with hands and controllers (UIA + DCU), including `LeverHandSnap`
- [ ] Re-run TssSim › Self Test and ProcedureRunner › Self Test after the lever rework
- [ ] UIA EVA 2: EMU PWR, O2, SUPPLY, WASTE (2005–2008), no sim effect yet
- [ ] Debug: Time Scale (Project Settings → Time) to fast-forward waits
  - [x] Flip any `TssSwitch` without VR: TssSim Inspector switch list (click to flip)

**Done when:** play mode loads clean, every switch changes `TssSim.GetSwitch` with hands and controllers, Reset EVA doesn't desync the boards, both Self Tests log "passed".

---

## Week 1 — Task list HUD + wrist panel
Show the tasks, progress and previous tasks; the wrist panel confirms steps when needed. (`TaskListHud`)

- [ ] HUD top-right of view: procedure title, step N/M, current step text, red warning line, `URGENT` banner + "Paused: Egress 12/31"
- [ ] Wrist panel (left or right wrist, shown when raised toward the head): scrollable step list, headers only when they change, done = dim, current = highlighted; Confirm / Back / Skip buttons
- [ ] Subscribe to `ProcedureRunner.Changed` / `Warning` / `Completed`; no live values in the task list (those come in Week 3)

**Done when:** a new player finishes egress in VR with only HUD guidance, with telemetry running from the start.

---

## Week 2 — Caution & warning + alerts (shall)
Alerts from the TSS are shown and take over the current procedure with the fix; errors like low oxygen are flagged. (`CautionWarning`, `TelemetryRange`, `AlertBanner`)

- [ ] Range table (data file): `{ TssValue, min, max, warnMargin, message }` from `eva-telemetry-ranges.pdf`
- [ ] Two levels: caution (nearing limit / resource trending down), warning (out of range or error flag)
- [ ] Banner + audio tone + message, persists until resolved, tone can be silenced
- [ ] Verbal-only procedures from the PDF (heart rate, temperature, suit pressure other) as Normal-priority procedures
- [ ] Special cases
  - [ ] Ignore suit pressure other until depress is done
  - [ ] Only the active fan should read ~30k rpm; inactive fan at 0 is normal
- [ ] Test every fault in VR

**Done when:** every fault raises the right alert, shows its procedure, and clears after the DCU action.

---

## Week 3 — Telemetry display (shall)
Telemetry on the HUD (O2 %, …) plus a more in-depth view on the wrist display. (`TelemetryDisplay`)

- [ ] Glance strip: O2 pri/sec %, battery %, suit pressure, heart rate, CO2, EVA time
- [ ] Detail panel (wrist tap): all `TssSim.Eva1` fields grouped — consumables, atmosphere, fans/scrubbers, thermal, biomed
- [ ] Bind with `TssField.OnValueChanged` (fires only on real changes)
- [ ] Values colored by the Week 2 ranges
- [ ] Dim, high-contrast, non-occluding

**Done when:** every `Eva1` value is reachable in one interaction without blocking walking.

---

## Week 4 — Rock yard, POIs + minimap (shall)
A minimap of the world with the POIs the procedures travel to. (`Poi`, `Minimap`)

- [ ] Terrain: the real rock yard from `TSS2026/documents/3d-scans` (to scale), rocks, slopes, hazard zones
- [ ] 3–5 POIs in a loop from the lander: `{ id, position, payloadType }`, known ahead of time
- [ ] Teleportation Anchors at each POI and the lander
- [ ] Align the Unity world with the TSS rock-yard frame (X −5765…−5545, Y −10075…−9940): `ImuFeed` sends rock-yard coordinates (Unity origin + offset), not raw Unity metres, so map and pathfinding work on the same numbers the real TSS sends
- [ ] 2D minimap (top-down), heading-up or north-up
- [ ] Live assets: player (`TssSim.PosX / PosY / Heading`), EV2
  - [ ] EV2 IMU in TssSim (currently EV1 only) + scripted EV2 walker on its own POI loop
- [ ] Drop pins at any time (gesture / menu), labelled
- [ ] Breadcrumbs: record every few metres, draw trail, "retrace to lander"
- [ ] POI pins + route order

**Done when:** you can walk the loop to every POI and back, see yourself and the POIs on the minimap, drop pins and breadcrumb home.

---

## Week 5 — Hazard grid + A* pathfinding (should; may run over)
Best path around hazards without Unity's NavMesh: it has to work like the real system, which only has NASA's maps and the TSS position. A rudimentary version is enough if it runs over. (`HazardGrid`, `NavigationAid`)

- [ ] Offline bake (editor script, run once like a team would before test week) → grid asset in rock-yard coordinates, ~0.5 m cells (~440 × 270)
  - [ ] Keep-out mask from `TSS2026/documents/maps/annotated/rock-yard-keep-out-zones.tiff`: crop the black frame, detect the red hatching, close the gaps between hatch lines so each zone becomes solid, map pixels → rock-yard coordinates from the map's corner coordinates
  - [ ] Heightmap from `TSS2026/documents/3d-scans/lunar.obj`: sample the mesh into the same grid → slope per cell; slopes over a limit are blocked, gentler slopes cost more
  - [ ] Inflate blocked cells by a safety margin (~1 m)
- [ ] `HazardGrid` (plain C#): loads the baked grid; dropped hazard pins add blocked circles at runtime
- [ ] `PathPlanner` (plain C#, no UnityEngine physics): A*, 8-connected, octile heuristic, step cost = distance × cell cost; smooth the result with line-of-sight shortcuts; returns waypoints in rock-yard coordinates
- [ ] `NavigationAid`: start = `TssSim.PosX / PosY`; replan on `Ticked` only when the player is > 3 m off the route, plus when hazards or the destination change; minimap and ground line just draw the waypoints
- [ ] Nav aids to next POI (bearing, distance, ground line)
- [ ] Self test: route never crosses a blocked cell; dropping a hazard pin on the route makes it detour

**Done when:** you can reach every POI using only the map and its path.

---

## Week 6 — Ingress, CSV log + summary
Close the loop, and log procedure data. (`EvaLogger`)

- [ ] Trigger: all POIs done or EVA time limit → navigate home
- [ ] `Eva1Ingress.asset` (steps from the PDF, page 5)
  - [ ] Connect umbilical (manual) → EMU-1 PWR ON → BATT UMB
  - [ ] O2 VENT OPEN → wait both tanks < 10 psi → VENT CLOSE
  - [ ] PUMP OPEN → WASTE OPEN → wait coolant < 5% → WASTE CLOSE
  - [ ] EMU-1 PWR OFF → disconnect umbilical (manual)
- [ ] `EvaLogger`: subscribes to runner / TSS events and writes switch flips, step times, faults and response times to CSV
- [ ] End-of-EVA summary: time, POIs completed, faults and response times

**Done when:** egress → EVA → ingress works and produces a CSV log and a summary.

---

## Week 7 — Predictive max range (shall)
A max-range circle on the minimap. (`RangePredictor`)

- [ ] Time remaining per consumable = level ÷ drain rate (rolling ~60 s average): O2 pri+sec, battery, coolant; take the minimum
- [ ] Range = time remaining × walking speed − distance home (breadcrumb / best path) − safety margin
- [ ] Range ring on map + "turn back" caution when range ≈ distance home
- [ ] Optional ML projection, with a rule-based limit as guardrail (mission description warns about hallucination)

**Done when:** an O2 fault visibly shrinks the range ring and the turn-back alert fires in time.

---

## Week 8 — Geology task + XRF scan
Rocks to scan for this year's proposal. Build flows now; swap in real telemetry when NASA publishes FY27 TSS.

- [ ] Terrain photos (RenderTexture capture camera)
- [ ] Pick up rock sample → carry pack
- [ ] PSMD procedure → place sample marker
- [ ] Photograph sample site
- [ ] XRF scan: point at tagged rock → composition from a local rock database, shown on the HUD
- [ ] Placeholder spectrometer values as new `TssValue` entries (use numbers outside 2001–2016; renumber when FY27 TSS lands)

**Done when:** a geology station has a runner-checked task sequence and XRF readings show on the HUD.

---

## Week 9 — Camera + POI payloads
A camera for procedures, and bringing certain rocks to POI sites.

- [ ] PLCD: deploy camera prop on tripod via procedure
- [ ] PLCT: tear down and stow camera
- [ ] Unique payload per POI (prop + procedure asset each): seismometer, regolith charge sensor, retroreflector alignment, environmental canister
- [ ] Bring specific rocks to POI sites
- [ ] Placeholder camera / payload values as new `TssValue` / `TssSwitch` entries

**Done when:** every POI has a complete, runner-checked task sequence.

---

## Week 10 — Full mission run + polish
Every component working together; a more realistic environment; fix anything janky.

- [ ] Full run end to end: egress → POIs → ingress → summary, with faults thrown along the way
- [ ] Lighting (done: directional light on, soft shadows, URP main light per-pixel)
  - [ ] Daytime HDRI sky (clear, flat open ground; Poly Haven) → Skybox/Cubemap material → Lighting › Environment, Generate Lighting
  - [ ] Directional Light 1.5–2.5, sun direction matched to the HDRI; set it as Sun Source
  - [ ] Check the real test-session time of day (evening / work lights → different sky)
  - [ ] Skybox rotates realistically: one turn per sidereal day (~0.0042°/s) via the skybox material's `_Rotation`, with the directional light (sun) rotated to match
- [ ] Label readability under the headlamp: URP tonemapping (ACES / Neutral), matte panels (Smoothness ~0.15), printed (non-emissive or faint) white-on-dark TMP labels with a thin outline, headlamp intensity ~1
- [ ] Comfort: vignette, seated mode
- [ ] Fix janky interactions found during the full run

**Done when:** a full mission runs end to end in VR without hitches.

---

## Not on the timeline
- [ ] Voice assistant (**shall** in the mission description, but no week covers it — fit into Week 10 or descope)
  - [ ] Speech-to-text (push-to-talk or wake word)
  - [ ] Queries: "O2 levels" → "Primary O2 47%, Secondary 99%." (short, per mission description)
  - [ ] Commands: next / repeat step (`ProcedureRunner.Confirm`), drop pin, route to POI N, route home, go to POI N (teleport), return to lander
  - [ ] Guardrail: telemetry numbers always read directly from `TssSim`, never generated by an LLM
  - [ ] Text-to-speech for replies and alerts
- [ ] May / stretch
  - [ ] 3D map of the environment
  - [ ] Computer-vision assist (highlight the next switch to flip)
  - [ ] Conductor panel (desktop window): inject faults, jump procedures, time scale

---

## Assignment 2 doc — out of date
- [ ] Sequence diagram: "pushes the FAN switch with a finger" → grabs and drags the FAN lever (`XRLever`)
- [ ] Class responsibility table: `TssToggle` "hinge + XR interactable" → connects an `XRLever` to `SetSwitch`; `NavigationAid` "NavMesh path" → grid A* (no NavMesh)
- [ ] Class + event-flow diagrams: `ProcedureRunner` no longer listens to `Ticked` (uses `SwitchChanged`, `ErrorChanged` and each wait step's `OnValueChanged`)
