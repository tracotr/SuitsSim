# SUITS VR Sim — Build Order

Ordered so each phase unblocks the next. **Shall** requirements come before should/may.
Each phase has a **Done when** test.

Reference: FY27 Mission Description, `TSS2026/documents/telemetry_ranges/eva-telemetry-ranges.pdf`.

---

## Phase 1 — Scene foundation
Goal: stand at the lander in VR with the TSS running.

- [x] XR Origin Hands rig (XRI Hands Interaction Demo sample)
  - [x] Install XR Hands package
  - [x] OpenXR features: Hand Tracking Subsystem, Meta Hand Tracking Aim (no Hand Interaction Profile: double aim source over Link)
  - [x] Tracking Origin Mode = Floor, XR Origin scale = 1
- [x] `TSS` GameObject with `SimTss` (`startOnPlay` off — EVA starts after egress)
- [x] `ImuFeed` with `head` = Main Camera
- [x] Graybox lander at Unity origin, with airlock door and a wall for the UIA
- [x] Script Execution Order: `SimTss` before default
- [x] Movement (no controllers)
  - [x] Room-scale walking
  - [x] Hand-gesture teleport: Teleportation Area on terrain, projectile ray on one hand, shown only while holding a pose (XR Hands Gestures sample), pinch to teleport
    - Line renderers need a URP material (Single Pass Instanced draws legacy shaders in one eye only)
    - Rig teleport Release Threshold 0.2 (stock 0.01 lags over Link)
  - [X] Cap teleport distance to a few metres so breadcrumbs and EVA time stay meaningful

**Done when:** play mode loads clean, hands are visible, you can move around, SimTss › Self Test logs "passed".

---

## Phase 2 — Boards (UIA + DCU)
Goal: every physical switch exists in VR and talks to the TSS.

- [ ] Reusable switch component
  - [ ] Fields: command number, on/off labels, visual (lever transform / light)
  - [ ] Poke/select → `Post(cmd, on ? 1 : 0)`
  - [ ] On `Ticked`, read state back from `Get(1)` and update visual (board always shows TSS truth)
  - [ ] Click sound + haptic/visual feedback
- [ ] UIA panel on lander wall (layout per mission description image)

  | Switch | Cmd |
  |---|---|
  | EMU-1 PWR / EMU-2 PWR | 2001 / 2005 |
  | EV-1 SUPPLY / WASTE | 2003 / 2004 |
  | EV-2 SUPPLY / WASTE | 2007 / 2008 |
  | OXYGEN EMU-1 / EMU-2 | 2002 / 2006 |
  | O2 VENT | 2009 |
  | DEPRESS PUMP PWR | 2010 |

- [ ] DCU mounted on player chest or forearm

  | Switch | Cmd | 1 = | 0 = |
  |---|---|---|---|
  | BATT | 2011 | UMB | LOCAL |
  | OXY | 2012 | PRI | SEC |
  | FAN | 2014 | PRI | SEC |
  | PUMP | 2015 | OPEN | CLOSE |
  | CO2 | 2016 | A | B |
  | COMMS | 2023 (Phase 3) | A | B |

- [ ] Umbilical: grabbable plug + XR Socket on the UIA (local "connected" state, no TSS command)
- [ ] Emissive labels readable in low light
- [ ] Optional: guard cover on O2 VENT

Notes:
- BATT: TSS README says `batt.lu` true = suit battery, but the server code treats true = umbilical. Follow the code.
- DEPRESS PUMP has no sim effect (same as real TSS); suit pressurizes on its own after O2 prep.

**Done when:** every switch changes its value in `Get(1)`, Reset EVA doesn't desync the boards, all switches work with hands.

---

## Phase 3 — TSS fixes before egress
Goal: egress completes with no false alarms.

- [ ] Add COMMS: `"comm": false` under `dcu.eva1` in `EvaTemplate`, placeholder command `2023` in `PostPaths`
- [ ] Verify coolant fill/drain via PUMP + SUPPLY/WASTE (starts at 0%, nominal 80–100%)
- [ ] Decide on battery primary/secondary (2013) — keep it, power fault needs it to be fixable
- [ ] Rerun Self Test

**Done when:** a manual egress ends with tanks at 3000 psi, suit O2 at 4 psi, coolant ≥ 95%, no error flags.

---

## Phase 4 — Procedure runner + egress
Goal: first complete playable slice.

- [ ] Procedure JSON format in `Assets/Procedures/`
  - `{ "name", "sections": [ { "title", "steps": [ { "device", "text", "check" } ] } ] }`
  - `check` = simple comparison on a JSON path (`==`, `<`, `>`, `>=`); no check = manual confirm
- [ ] Runner
  - [ ] Evaluate current step on `Ticked`, auto-advance when it passes
  - [ ] Show current step + device tag, next step, live value for waits ("Primary O2 1840 / 3000 psi")
  - [ ] Flag out-of-order switch flips
- [ ] `egress.json`
  - [ ] Connect: umbilical, EMU-1 PWR ON, BATT UMB, DEPRESS PWR ON
  - [ ] Water prep (not in Appendix A, needed for coolant): PUMP OPEN → WASTE OPEN → wait coolant < 5% → WASTE CLOSE → SUPPLY OPEN → wait coolant ≥ 95% → SUPPLY CLOSE → PUMP CLOSE
  - [ ] Prep O2: VENT OPEN → wait both tanks < 10 psi → VENT CLOSE → OXY PRI → EMU-1 OPEN → wait pri ≥ 2999 → EMU-1 CLOSE → OXY SEC → EMU-1 OPEN → wait sec ≥ 2999 → EMU-1 CLOSE → OXY PRI
  - [ ] End: wait suit O2 ≈ 4 → DEPRESS OFF → BATT LOCAL → EMU-1 PWR OFF → verify OXY PRI, COMMS A, FAN PRI, PUMP CLOSE, CO2 A → unplug
- [ ] Last step calls `StartEva()`
- [ ] Debug key: `Time.timeScale = 10`

Notes:
- Tank pressure caps at exactly 3000, so use `>= 2999`, never `> 3000`.
- Sim timings: vent ~24 s, each tank fill ~40 s, suit pressurize ~16 s, coolant fill ~17 s.

**Done when:** a new player finishes egress in VR with only HUD guidance, and the EVA clock starts on unplug.

---

## Phase 5 — Caution & Warning (shall)
Goal: off-nominal telemetry is impossible to miss and each fault is fixable.

- [ ] Range table (data file): `{ field, min, max, warnMargin, message, fix }` from `eva-telemetry-ranges.pdf`
- [ ] Two levels: caution (nearing limit / resource trending down), warning (out of range or `error.*` true)
- [ ] Banner + audio tone + message with the fix, persists until resolved, tone can be silenced
- [ ] Special cases
  - [ ] Ignore `suit_pressure_other` until depress is done
  - [ ] Only the active fan should read ~30k rpm; inactive fan at 0 is normal
- [ ] Test every fault

  | Fault | Trigger | Fix |
  |---|---|---|
  | Fan | Throw Fan Low | FAN → SEC |
  | O2 low / high | Throw O2 Low / High | OXY → SEC |
  | Scrubber | scrubber > 60% | flip CO2 |
  | Power | primary battery < 20% | BATT → secondary |

**Done when:** every fault raises the right alert with the right fix and clears after the DCU action.

---

## Phase 6 — Suit telemetry display (shall)
Goal: own suit + biomed data visible at any time.

- [ ] Glance strip: O2 pri/sec %, battery %, suit pressure, heart rate, CO2, EVA time
- [ ] Detail panel (voice / wrist tap): all `telemetry.eva1` values grouped — consumables, atmosphere, fans/scrubbers, thermal, biomed
- [ ] Values colored by Phase 5 ranges
- [ ] Dim, high-contrast, non-occluding (test runs in near darkness)

**Done when:** every `telemetry.eva1` value is reachable in one interaction without blocking walking.

---

## Phase 7 — World, lighting, POIs
Goal: a lunar surface worth navigating.

- [ ] Terrain ≥ 60 × 60 m: regolith, rocks, craters, slopes, a few hazard zones (steep / boulder fields)
- [ ] South-pole lighting: sun a few degrees above horizon, long hard shadows, black sky, near-black shade
- [ ] Suit headlamp / helmet lights
- [ ] 3–5 POIs in a loop from the lander: `{ id, position, payloadType }`, known ahead of time
- [ ] Teleportation Anchors at each POI and the lander (for voice teleport later)

**Done when:** you can walk the loop to every POI and back, and shadows make it hard without the HUD.

---

## Phase 8 — Map & navigation (shall)
Goal: reach POIs and get home safely.

- [ ] 2D minimap (top-down), heading-up or north-up
- [ ] Live assets: player (`imu.eva1`), EV2 (`imu.eva2`)
  - [ ] Scripted EV2 walker on its own POI loop, posting 2020–2022
- [ ] Drop pins at any time (voice / gesture / menu), labelled
- [ ] Breadcrumbs: record every few metres, draw trail, "retrace to lander"
- [ ] POI pins + route order
- [ ] Should: best path around hazards (NavMesh on terrain)
- [ ] Should: nav aids to next POI (bearing, distance, ground line)

**Done when:** you can reach every POI using only the map, drop pins anywhere, and breadcrumb home in the dark.

---

## Phase 9 — Ingress
Goal: close the loop.

- [ ] Trigger: all POIs done or EVA time limit → navigate home
- [ ] `ingress.json` (placeholder — last year's version from memory, replace when NASA publishes FY27)
  - [ ] Connect umbilical → EMU-1 PWR ON → BATT UMB
  - [ ] O2 VENT OPEN → wait both tanks < 10 psi → VENT CLOSE
  - [ ] PUMP OPEN → WASTE OPEN → wait coolant < 5% → WASTE CLOSE → PUMP CLOSE
  - [ ] EMU-1 PWR OFF → disconnect
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
- [ ] Commands: next / repeat step, drop pin, route to POI N, route home, go to POI N (teleport), return to lander
- [ ] Guardrail: telemetry numbers always read directly from JSON, never generated by an LLM
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
- [ ] Unique payload per POI (prop + procedure each): seismometer, regolith charge sensor, retroreflector alignment, environmental canister
- [ ] PLCT: tear down and stow camera
- [ ] Placeholder JSON sections for spectrometer / camera / payload, commands 2100+ (renumber when FY27 TSS lands)

**Done when:** every POI has a complete, runner-checked task sequence and XRF readings show on the HUD.

---

## Phase 13 — May / polish
- [ ] 3D map of the environment
- [ ] Computer-vision assist (highlight the next switch to flip)
- [ ] Logging: switch flips, step times, faults, response times → CSV
- [ ] Conductor panel (desktop window): inject faults, jump phases, time scale
- [ ] Comfort: vignette, seated mode

---

## Milestones
- **Phases 1–5** — playable egress with faults (first target)
- **Phases 6–9** — full mission loop
- **Phases 10–12** — remaining shall requirements + POI work
- **Phase 13** — stretch goals
