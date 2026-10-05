# SUITS VR Sim — Build Order

Ordered so each phase unblocks the next. **Shall** requirements come before should/may.
Each phase has a **Done when** test.

Reference: FY27 Mission Description, `TSS2026/documents/procedures/ev-team-procedure-timeline.pdf` (egress / ingress / error procedures), `TSS2026/documents/telemetry_ranges/eva-telemetry-ranges.pdf`.

Key scripts (`Assets/Scripts/`):
- `TSS/TssSim.cs` — the simulated TSS (singleton, no JSON). `GetSwitch` / `SetSwitch(TssSwitch, bool)`, `Eva1.Get(TssValue)`, error flags, `Ticked` / `SwitchChanged` events, `Advance(seconds)`
- `TSS/EvaData.cs` — telemetry fields as a ScriptableObject (`Assets/Resources/EvaData.asset`), `TssValue` enum
- `TSS/Procedure.cs` + `TSS/ProcedureRunner.cs` — procedure assets and the step runner
- `TSS/TssToggle.cs` — physical switch (poke or controller select)
- `DCU/ChestMount.cs` — keeps the DCU on the player's chest

---

## Phase 1 — Scene foundation
Goal: stand at the lander in VR with the TSS running.

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
  - [x] Hand-gesture teleport (HandTeleportInteractor from the Hands demo): "finger gun" pose shows the ray, pinch to lock, release to teleport, open hand cancels
    - Line renderers need a URP material (Single Pass Instanced draws legacy shaders in one eye only)
    - Rig teleport Release Threshold 0.2 (stock 0.01 lags over Link)
  - [x] Cap teleport distance (Max Raycast Distance 4 m) so breadcrumbs and EVA time stay meaningful
  - [ ] Teleport ray origin: point it out of a straight index finger that doesn't move when pinching
    - Empty `Teleport Ray Origin` under the hand's palm / index-metacarpal joint (rigid with the hand, not the curling finger joints), blue Z along a straight finger → set as the XR Ray Interactor's **Ray Origin Transform** (currently `Aim Pose`)
    - Move HandTeleportInteractor from `Right Hand` to `Camera Offset` (its own Tracked Pose Driver stacks on Right Hand's tracking)
    - Palm-up activation condition is 25° (sample: 50°): loosen or remove if the pose is awkward
- [x] Desktop testing: XR Interaction Simulator sample imported (disable it before using the headset)

**Done when:** play mode loads clean, hands are visible, you can move around, TssSim › Self Test logs "passed".

---

## Phase 2 — Boards (UIA + DCU)
Goal: every physical switch exists in VR and talks to the TSS.

- [x] Reusable switch component: `TssToggle` (UIAToggle / DCUToggle prefabs)
  - [x] `sw` field = `TssSwitch` enum, grouped dropdown (`UIA/EVA 1/O2 (Open)`, `DCU/EVA 1/Fan (PRI)`…; the word in brackets is what On means)
  - [x] Finger poke flips the sprung hinge lever
  - [x] Controller / simulator: Select (grip) flips it
  - [x] Board always shows TSS truth (`SwitchChanged` read-back, starts from `GetSwitch`)
  - [x] Click sound
  - [x] Controller can reach toggles: physics layer **Toggle** added to the Near-Far near/far caster masks in `Left_NearFarInteractor.prefab` (XRI sample — redo if the sample is re-imported)
- [ ] UIA panel on lander wall
  - [x] EVA 1: EMU PWR (2001), O2 (2002), SUPPLY (2003), WASTE (2004)
  - [x] O2 VENT (2009), DEPRESS PUMP (2010)
  - [ ] EVA 2: EMU PWR, O2, SUPPLY, WASTE (2005–2008), no sim effect yet
- [x] DCU on the player's chest (`DCU.prefab`, `ChestMount`: follows head position + yaw only, kinematic Rigidbody, hinges connected to it)
  - [x] All six toggles assigned; On tilts toward the outer label (PRI / UMB / OPEN), off = SEC / LOCAL / CLOSE (TSS default)

  | Toggle | Switch | On | Off |
  |---|---|---|---|
  | BATT (top) | `DcuEva1Batt` 2011 | UMB | LOCAL |
  | OXY | `DcuEva1Oxy` 2012 | PRI | SEC |
  | BATT (bottom) | `DcuEva1BattSource` 2013 | PRI | SEC |
  | FAN (top) | `DcuEva1Fan` 2014 | PRI | SEC |
  | PUMP | `DcuEva1Pump` 2015 | OPEN | CLOSE |
  | CO2 (bottom) | `DcuEva1Co2` 2016 | PRI | SEC |

  - Left side is a mirrored *layout* (label strip moved to the other side, hinge axis +1), not negative scale (breaks hinges)
- [x] DCU refit: grab the DCU body (pinch / grip) and drag it up/down to fit the user's chest height
  - [x] Grab moves `ChestMount.offset.y` / `.z` (clamped by `heightRange` -0.7…-0.25 m, `depthRange` -0.15…0.2 m), not the transform directly
- [X] Switch visual feedback
  - [x] Hover highlight when a fingertip / ray is on a lever (`TssToggle.hoverColor`, URP `_BaseColor` tint)
  - [x] Detent feel (hands, no haptics): lever resists the push (`give`), snaps over once past center; click on flip
  - [x] Blink red when the ProcedureRunner flags that switch as unexpected (`UnexpectedSwitch` event, `TssToggle.warningColor` / `warningTime`)
- [X] Emissive labels readable in low light

Notes:
- BATT: TSS README says `batt.lu` true = suit battery, but the server code treats true = umbilical. Follow the code (On = UMB).
- DEPRESS PUMP has no sim effect (same as real TSS); suit pressurizes on its own after O2 prep.
- COMMS: dropped. The real TSS has no EV1 COMMS command and it affects nothing; add a `TssSwitch` entry only if a procedure needs it.

**Done when:** every switch changes `TssSim.GetSwitch`, Reset EVA doesn't desync the boards, all switches work with hands and controllers.

---

## Phase 3 — TSS rewrite + fixes before egress ✅
Goal: egress completes with no false alarms.

- [x] Rewrite as `TssSim` + `EvaData` ScriptableObject (no JSON), EV1 only, real TSS quirks kept
- [x] `TssSwitch` enum (values = real command numbers 2001–2016; EV1 DCU only, like the TSS), `TssValue` enum + `EvaData.Get`
- [x] Fix: formula fields (tank pressure, suit total…) computed in `Awake` / `ResetEva`, not 0 until the first tick
- [x] Coolant fill/drain verified (starts 0%, fills > 95% in ~17 s with PUMP + SUPPLY)
- [x] Battery primary/secondary (2013) kept — power fault needs it
- [x] Self Test passes (vent, both tank fills, coolant, suit O2 = 4, fan + O2 faults, reset)

**Done when:** a manual egress ends with tanks at 3000 psi, suit O2 at 4 psi, coolant ≥ 95%, no error flags. ✔ (harness run)

---

## Phase 4 — Procedure runner + egress
Goal: first complete playable slice.

- [x] `Procedure` ScriptableObject (Create → TSS → Procedure) instead of JSON
  - Step = header + text + `SwitchReq` (On / Off / Flip) + `ValueReq` (`TssValue` Below / Above threshold); no requirements = manual Confirm
- [x] `ProcedureRunner`
  - [x] Auto-advance on `SwitchChanged` and `Ticked`; steps already met pass straight through ("Verify OXY – PRI")
  - [x] "Unexpected: …" warning on out-of-order flips (warn only, never blocks)
  - [x] Urgent error procedures interrupt and resume the paused one at the same step
  - [x] Progress kept per procedure: switch away and back without losing your place
  - [x] Self Test: plays egress, throws a fan fault mid-run, checks resume, zero warnings
  - [x] Tester sign-off: manual steps (verify / announce / return to PR) and steps with `requireConfirm` (e.g. Verify OXY – PRI) wait for Confirm — ProcedureRunner ⋮ → Confirm / Skip / Back Step
  - [x] Complete Egress (⋮): plays egress live on the boards, 0.3 s per step
- [x] `Eva1Egress.asset` — 31 steps, cross-checked against the PDF and replayed against the sim (~3 min)
  - [x] Step 21 text fixed; re-checked against the PDF (3/2/2026 version): all 31 steps match
- [ ] Task list UI
  - [ ] HUD top-right of view: procedure title, step N/M, current step text, red warning line, `URGENT` banner + "Paused: Egress 12/31"
  - [ ] Wrist panel (left or right wrist, shown when raised toward the head): scrollable step list, headers only when they change, done = dim, current = highlighted; Confirm / Back / Skip buttons
  - No live values in the task list — those come from the telemetry display (Phase 6)
- [x] EVA telemetry starts before egress (CAPCOM "START EVA Telemetry"): ProcedureRunner's CAPCOM setup calls `StartEva()` — vent/fill/coolant need it running
- [ ] Debug: Time Scale (Project Settings → Time) to fast-forward waits; optional debug panel to flip any `TssSwitch` without VR

Notes:
- Tank pressure caps at exactly 3000, so thresholds use > 2950 (as in the PDF), never > 3000.
- Sim timings: vent ~24 s, each tank fill ~40 s, coolant fill ~17 s; suit O2 reaches 4 during the coolant fill.

**Done when:** a new player finishes egress in VR with only HUD guidance, with telemetry running from the start.

---

## Phase 5 — Caution & Warning (shall)
Goal: off-nominal telemetry is impossible to miss and each fault is fixable.

- [x] Error procedures (Urgent, auto-started by the runner on the TSS error flag)

  | Fault | Trigger | Procedure asset | Fix |
  |---|---|---|---|
  | Fan | Throw Fan Low | `Eva1OffNominalFan` | FAN → SEC, return to PR |
  | O2 low / high | Throw O2 Low / High | `Eva1OffNominalO2` | OXY → SEC, return to PR |
  | Scrubber | scrubber > 60% | `Eva1OffNominalScrubber` | flip CO2 |
  | Power | primary battery < 20% | `Eva1OffNominalBattery` | BATT → SEC |

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

## Phase 6 — Suit telemetry display (shall)
Goal: own suit + biomed data visible at any time.

- [ ] Glance strip: O2 pri/sec %, battery %, suit pressure, heart rate, CO2, EVA time
- [ ] Detail panel (voice / wrist tap): all `TssSim.Eva1` fields grouped — consumables, atmosphere, fans/scrubbers, thermal, biomed
- [ ] Bind with `TssField.OnValueChanged` (fires only on real changes)
- [ ] Values colored by Phase 5 ranges
- [ ] Dim, high-contrast, non-occluding

**Done when:** every `Eva1` value is reachable in one interaction without blocking walking.

---

## Phase 7 — World, lighting, POIs
Goal: the JSC rock yard, worth navigating.

- [ ] Terrain: the real rock yard from `TSS2026/documents/3d-scans` (to scale), rocks, slopes, hazard zones
- [ ] Lighting (done: directional light on, soft shadows, URP main light per-pixel)
  - [ ] Daytime HDRI sky (clear, flat open ground; Poly Haven) → Skybox/Cubemap material → Lighting › Environment, Generate Lighting
  - [ ] Directional Light 1.5–2.5, sun direction matched to the HDRI; set it as Sun Source
  - [ ] Check the real test-session time of day (evening / work lights → different sky)
- [ ] Suit headlamp / helmet lights (if testing in low light)
- [ ] 3–5 POIs in a loop from the lander: `{ id, position, payloadType }`, known ahead of time
- [ ] Teleportation Anchors at each POI and the lander (for voice teleport later)

**Done when:** you can walk the loop to every POI and back.

---

## Phase 8 — Map & navigation (shall)
Goal: reach POIs and get home safely.

- [ ] 2D minimap (top-down), heading-up or north-up
- [ ] Live assets: player (`TssSim.PosX / PosY / Heading`), EV2
  - [ ] EV2 IMU in TssSim (currently EV1 only) + scripted EV2 walker on its own POI loop
- [ ] Drop pins at any time (voice / gesture / menu), labelled
- [ ] Breadcrumbs: record every few metres, draw trail, "retrace to lander"
- [ ] POI pins + route order
- [ ] Should: best path around hazards (NavMesh on terrain)
- [ ] Should: nav aids to next POI (bearing, distance, ground line)

**Done when:** you can reach every POI using only the map, drop pins anywhere, and breadcrumb home.

---

## Phase 9 — Ingress
Goal: close the loop.

- [ ] Trigger: all POIs done or EVA time limit → navigate home
- [ ] `Eva1Ingress.asset` (steps from the PDF, page 5)
  - [ ] Connect umbilical (manual) → EMU-1 PWR ON → BATT UMB
  - [ ] O2 VENT OPEN → wait both tanks < 10 psi → VENT CLOSE
  - [ ] PUMP OPEN → WASTE OPEN → wait coolant < 5% → WASTE CLOSE
  - [ ] EMU-1 PWR OFF → disconnect umbilical (manual)
- [ ] End-of-EVA summary: time, POIs completed, faults and response times

**Done when:** a full run works end to end: egress → EVA → ingress → summary.

---

## Phase 10 — Predictive max range (shall)
Goal: know how far you can go and still get home.

- [ ] Time remaining per consumable = level ÷ drain rate (rolling ~60 s average): O2 pri+sec, battery, coolant; take the minimum
- [ ] Range = time remaining × walking speed − distance home (breadcrumb / best path) − safety margin
- [ ] Range ring on map + "turn back" caution when range ≈ distance home
- [ ] Optional ML projection, with a rule-based limit as guardrail (mission description warns about hallucination)

**Done when:** an O2 fault visibly shrinks the range ring and the turn-back alert fires in time.

---

## Phase 11 — Voice assistant (shall)
Goal: hands-free queries and procedure help.

- [ ] Speech-to-text (push-to-talk or wake word)
- [ ] Queries: "O2 levels" → "Primary O2 47%, Secondary 99%." (short, per mission description)
- [ ] Commands: next / repeat step (`ProcedureRunner.Confirm`), drop pin, route to POI N, route home, go to POI N (teleport), return to lander
- [ ] Guardrail: telemetry numbers always read directly from `TssSim`, never generated by an LLM
- [ ] Text-to-speech for replies and alerts

**Done when:** egress can be completed and any telemetry value read back by voice alone.

---

## Phase 12 — POI tasks
Goal: the work at each station. Build flows now; swap in real telemetry when NASA publishes FY27 TSS.

- [ ] PLCD: deploy camera prop on tripod via procedure
- [ ] Geology
  - [ ] Terrain photos (RenderTexture capture camera)
  - [ ] Pick up rock sample → carry pack
  - [ ] PSMD procedure → place sample marker
  - [ ] Photograph sample site
  - [ ] XRF scan: point at tagged rock → composition from a local rock database
- [ ] Unique payload per POI (prop + procedure asset each): seismometer, regolith charge sensor, retroreflector alignment, environmental canister
- [ ] PLCT: tear down and stow camera
- [ ] Placeholder spectrometer / camera / payload values as new `TssValue` / `TssSwitch` entries (use numbers outside 2001–2016; renumber when FY27 TSS lands)

**Done when:** every POI has a complete, runner-checked task sequence and XRF readings show on the HUD.

---

## Phase 13 — May / polish
- [ ] 3D map of the environment
- [ ] Computer-vision assist (highlight the next switch to flip)
- [ ] Logging: switch flips, step times, faults, response times → CSV
- [ ] Conductor panel (desktop window): inject faults, jump procedures, time scale
- [ ] Comfort: vignette, seated mode

---

## Milestones
- **Phases 1–5** — playable egress with faults (first target) — TSS, runner, egress and error procedures done; next: task list UI, teleport ray origin, DCU refit, switch feedback
- **Phases 6–9** — full mission loop
- **Phases 10–12** — remaining shall requirements + POI work
- **Phase 13** — stretch goals
