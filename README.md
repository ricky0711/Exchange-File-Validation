# Exchange File Validator — v2 (step 3) (.NET 10 / VS 2026)

Fluent (WinUI-3 look) WPF tool to validate ISR demands against the Message List. Full pipeline: **load → level assignment → property fill → validation → export.** Left rail switches pages; each phase is usable on its own.

## Run it
1. Open `ExchangeFileValidator.csproj` in Visual Studio 2026 (or `dotnet build`). Needs the **.NET 10 SDK**.
2. Restore NuGet packages (WPF-UI 4.3.0, CommunityToolkit.Mvvm 8.4.0, ClosedXML 0.105.0).
3. F5. Click **Browse** for the Message List (.xlsx) and the Exchange File (.xlsm), then **Load**.
4. The grid fills with all signals from the Message List — type in the search box to filter (signal / frame / PDU). Virtualization keeps 27k rows smooth.


## v2 — Step 7 (Explorer + Frame-layout view)

### Feature 1 — Hierarchical Explorer (PDU → Frame → Signal → ISR)
New **Explorer** page (Workspace group): a virtualized `TreeView` built from the in-memory join indexes (`ReferenceData.IndexExplorer` → `SignalsByFrame`, `FramesByPdu`, `IsrsByParameter`) — no files re-read.
- 4 lazy levels: **PDU → Frames** (classic / `*C_FD` / `*SC_FD`, with ID/type/Tx) **→ Signals** (byte/bit + functional chip) **→ ISRs** (n°, Active/Abandoned/Refused chip, Tx→Rx). Each header shows a count; chips reuse the theme-safe colours.
- **Lazy children** (built on first expand via a placeholder), tree **virtualized** (`VirtualizingStackPanel` + recycling). **Group by** re-roots as PDU / Frame / Signal. Search filters the roots.
- A frame node → **Open frame view** (Feature 2); an ISR node → opens its frame. *Assumption: a frame's transmitter is the container master (`Tx unit`) if it's a container, else the union of its signals' T-column ECUs.*

### Feature 3 — Design pass + animations
- The Explorer and Frame-view pages use the same **card language** as the dashboard (rounded cards + shared `SoftShadow`, generous spacing, restrained typography, theme-safe chips, `#6D5DF5` accent).
- **Animations are chrome-only and code-driven** so a **single switch** (`Controls.AppAnimations.Enabled`) disables them all — nothing animates the virtualized `DataGrid`s: **page transitions** cross-fade via a `FadeContentControl` wrapping the page host; **frame-view signal blocks** stagger a fade-in on load; the **selected block** gets an accent glow. (Deferred to honour the single-toggle rule + virtualization: TreeViewItem expand animation, KPI count-up, XAML hover-scale — these would bypass the flag or touch hot/virtualized content.)

### Feature 2 — Frame structure (bit/byte layout) view
New **Frame Layout** page: `FrameLayoutService` builds a byte × bit matrix from the loaded Message-List rows; the view renders it (rows = bytes, 8 bit columns labelled **7→0**).
- Each signal is a coloured block spanning its bits from `Byte/Bit Position` for `Signal Size (Bits)`, **Motorola/MSB-first** across byte boundaries *(assumption — one-place change if Intel/LSB)*. Kinds coloured: application (per-PDU palette), **CRC**, **Clock**, padding (grey). Header strip shows name/ID/type/secured/DLC/Tx; a legend + a **used/free bits** busload hint; container **PDU bands** derived from each PDU's signal byte-range *(assumption: no explicit offset column — derived from min/max byte of the PDU's signals)*.
- Click a block → highlight + side detail (size/position, unit/min/max/res, coding, Tx/Rx, functional, its ISRs). Opened from the Explorer or via a frame search box.

## v2 — Step 6 (route correctness + L2.1 cross-check)

### 1 — Route check: frame-instance Tx/Rx + "existing routes are set"
- **(a) Resolve on the specific frame instance.** A receiver may be marked `R` on only **one** of a signal's frame instances, so `FrameMatchService` now prefers the instance whose **Rx column is `R`** (then whose emitter is `T`) — e.g. `DriverSafetyBeltBuckleState → PCM` resolves to `BCM_A13SC_FD` (the secured variant where `PCM = R`). Route resolution then includes that instance's **own T-column ECU** (the container master `PIU_MASTER`) as a candidate transmitter, alongside the original ECU and container gateway.
- **(b) Never flag existing (L0/L1) routes.** If a Network-Path row resolves, the route is **SET** (shown in the trace, no finding). For **L0/L1** the transmission already exists, so the check never reports a missing hop even when matching can't locate the row. Only a genuinely new Tx→Rx — **L2 new-Rx / L2.1 / L3** with no row — is flagged "new gateway routing required". *(Acceptance case `DriverSafetyBeltBuckleState / BCM_A13SC_FD / PIU_MASTER → PCM` now resolves as route SET.)*

### 5 — L2.1 cross-architecture bit/byte cross-check
When a 2nd architecture is loaded, L2.1 demands get a new `L2.1 layout` check: an **info** finding shows the other architecture's frame/PDU/size/byte/bit layout to replicate, and a **warning** when the demand's declared size (from Logical/Analog) disagrees with the 2nd-architecture size.

## v2 — Step 5 (FACE data-model + level/container logic)

### Part A — Signals map into MANY frames (matching correctness fix)
A FACE signal gets one Message-List row **per frame it is mapped into** (~36% map into 2–4 frames: classic CAN / `*C_FD` container / `*SC_FD` secured). The old loader indexed with `TryAdd`, **silently keeping the first row** — so fill/validation/CRC/route compared against an arbitrary frame variant.
- **New index `SignalMappingsByName` = signal → `List<SignalDef>` (all mappings)**; `SignalByName` stays as the primary (first). Filler/padding rows (`**** fixed to zero ****`) are excluded from matching; `SignalDef` gains `IsFiller`/`IsHeader`/`IsStructural`/`IsFd`/`IsContainerFrame`/`IsSecuredContainer` and the loaded `FrameContainer`/`BytePosition`/`BitPosition` columns.
- **`FrameMatchService`** centrally resolves the correct instance per demand — priority: (a) explicit frame named → (b) FD-vs-HS preference → (c) Tx/Rx node coverage → (d) single fallback — caching `MatchedDef` / `MatchedFrames` / `MatchSource` on the demand. **Every consumer** (fill, validation property checks, ISR-vs-AT compare, detail panel) now uses it, so they all agree on the frame.
- **Multiplicity surfaced** — a new `Frame multiplicity` check emits an info finding ("signal maps into N frames: … — matched →X") instead of guessing silently. The load status line reports distinct + multi-frame counts (verify nothing's lost).
- Reference grid gains Frame Container / Byte Pos / Bit Pos columns (hidden by default).

### Part B — Three-sheet trace model (content → packing → routing)
The Message List (signal layer), **Construction of Container frame** (assembly layer) and Network Path (routing layer) are one layered model. New `ContainerFrame` model + loader (`LoadContainers`, header on **row 4**, indexed by Contained I-PDU and frame name — assumed to live in the Msg-Set workbook alongside Dico/Network-Path). New **`FrameTraceService`** resolves, per demand, the chain **signal → I-PDU (+byte/bit) → container frame (original ECU vs gateway Tx, MAC/secured) → Network-Path synthesis route**, and flags whether the route crosses the CGW/PIU. It's shown in the per-ISR expand panel as a colour-coded **TRACE** strip (Signal / Container / Route chips). Route resolution already matches on both the original ECU and the container gateway transmitter (feeds Part E).

### Part C — ISR level rules (authoritative)
- **Level refinement:** L2 now distinguishes **new-Rx** ("signal + Tx applied, adds a new Rx") from **new-Tx** ("signal exists in the Message List but this Tx/Rx isn't applied yet"). A signal that exists in the AT is no longer mis-classified as L3 — **L3 is reserved for genuinely new signals**. L0 / L2.1 unchanged.
- **Same-channel rule (new Tx):** Network-Path **segment columns** are now loaded and an **ECU→home-channel** index is derived (home channel = segments common to all of an ECU's transmit routes). New check **`New Tx channel`**: a new Tx on a **different channel** than the existing Tx ⇒ **error** ("not permitted"); same channel ⇒ allowed; channel unknown ⇒ **warning**.
- **L3 frame-assignment gate (sequenced):** new check **`L3 frame`** — if the L3 signal has property **errors** → "blocked, fix properties first"; once properties pass but **no customer frame** is assigned → **error** "awaiting customer frame assignment" (the tool never auto-assigns); once the customer sets a frame → info "proceed". A per-demand **Assigned frame (customer)** input appears in the L3 detail panel; Re-run re-gates.

### Part F — Functional status (from ISR-Applied tranches) + L0 reactivation
- **Tranche-aware status fix:** `LoadAppliedIsrs` previously read the trailing **legend** cell (`x:Use A:Abandon R:Refused`) as the status — a bug. It now detects the per-tranche columns (`T1_2023 … T4_2025/2026`, regex `^T\d` with an x/A/R-only fallback) and takes each ISR's **current status = latest non-empty tranche** (`x`=Use, `A`=Abandon, `R`=Refused). `AppliedIsr.IsActive` = status `x`.
- **Per-signal functional status recomputed** (`ReferenceData.IndexFunctional`): `Functional(signal) = ANY of its ISRs is active` (a signal has many ISRs — all are inspected). Stamped onto `SignalDef.Functional`; shown as a Reference-grid column and an AT row in the per-ISR detail. Not taken from the Message List flag — and a **disagreement** between the recomputed status and the Message List `Functional` flag is raised as an **info** finding (`Functional status`, only when the flag column exists).
- **L0 reactivation link:** a demand whose target signal is **currently non-functional** (all its ISRs Abandon/Refused) is classed **L0** ("signal was deactivated before"), checked before L1/L2 so a deactivated signal isn't mis-matched as already-transmitted.

### Part D — Container-frame decision (ASIL)
- **ASIL detection** (`AsilDetector`) reads **both** `LossLinkageASIL` / `CorruptDataASIL` demand columns (now loaded): any column carrying an ASIL value ⇒ **Requested**; an empty column ⇒ **Undetermined** (flagged, never assumed); explicit QM/no-ASIL ⇒ **None**.
- **`ContainerDecisionService`** (pure): `{asilRequested, crossesGateway, fdOnly}` → `{none|normal|secure, busload|asil}` — ASIL+gateway ⇒ secure `*SC_FD`; ASIL single-channel ⇒ normal `*C_FD` sufficient; no ASIL ⇒ normal (busload, optional).
- **CRC/CLK now factors ASIL** — ASIL ⇒ E2E **CRC+Clock required** (state `new` when absent); no-ASIL absent ⇒ `not required`; undetermined ⇒ `cannot decide`. New badge states wired.
- New check **`Container/ASIL`**: undetermined ASIL ⇒ warning; ASIL+gateway but matched frame not secured ⇒ **error**; secure container where a normal one suffices ⇒ warning. The decision is also shown as a **Decision** step in the per-ISR trace.

### Part E — Network route check enhancement
`Network route` now uses the **container-aware** `FrameTraceService.ResolveRoute` (matches on both the original ECU **and** the container gateway Tx). On a miss it **diagnoses the missing hop** — frame/PDU not routed at all vs transmitter not routed for this frame vs no route to this receiver (segment pairing not gatewayed) — so the engineer knows exactly what new gateway routing is needed. The resolved **Synthesis path** is displayed in the trace's Route step.

## v2 — Step 4 (polish)

### A — Excel-grade data grids (Reference Data + Exchange File)
Reusable, page-agnostic grid toolkit under `Controls/`:
- **Per-column Excel filter** — every column header now carries a funnel button (turns accent-violet when active) opening a popup with a **searchable checkbox list of that column's distinct values**, plus Select-all / Clear / Apply / Clear-filter. Multiple column filters combine with AND, and AND with the global search + the page's status/level filters. (`ColumnFilterState`, `GridFilterController`, `Controls/ColumnFilterHeader.xaml`.)
- **Dynamic show/hide columns** — a **Columns** button → checklist of every available column. Reference Data exposes all `SignalDef` fields *including the per-ECU T/R node columns* (auto-discovered per file, hidden by default); the Exchange File grid exposes all demand fields (Level, filled Frame/PDU/Bits, CRC/CLK, codes, …).
- **Sorting** — click any column header to sort (template/chip columns sort via `SortMemberPath`).
- **Virtualization intact** — distinct values are computed **lazily** (only when a popup opens) and **capped** (2 000 per column) so the 27k-row grid stays responsive; the global search box replaces the old single search-scope dropdown.
- Columns are built programmatically (`ExcelGridBuilder`) from declarative `ColumnSpec`s the view-models own — needed because Reference Data's ECU columns vary per file.

### F — Export: explicit 212 / 270 layouts + Ready-to-import gate (PROVISIONAL)
`ExportService` now has **two distinct, explicit column layouts** built from a small `Col(Header, selector, Text)` model:
- **270** = full layout (adds Frame ID, PDU, Signal Size, Value Type, Logical/Analog data, KindOfIsr, OtherRequirements, CRC/CLK + Level + the gate);
- **212** = reduced core subset.
A **"Ready to import" gate column** is included; the gate is derived from validation — a demand is *Ready to import* unless it has a blocking **Error** finding (set on `IsrDemand.ImportStatus` by `IsrDetailBuilder`). "Export only ready" filters on it. Text formatting stays forced on Feature_Number / EmitterCode / ReceiverCode. The Export page shows column count + ready/blocked counts.
**⚠ Provisional** — the exact per-format order/headers and the real gate column still need a real Alliance 270/212 sample; send one and this locks to it exactly.

### H — Design pass (in progress)
- **Active-page highlight in the sidebar** — the current page's nav button now renders with a Secondary (filled) appearance via `ActivePageToAppearanceConverter` bound to `MainViewModel.ActivePage`, so it's always clear where you are (incl. when the checklist jumps you to Validation).
- Added a **shared soft-shadow** resource (`App.xaml` → `SoftShadow`) applied consistently to the card surfaces — dashboard KPI cards, checklist rows, and the per-ISR detail panel — for a lighter, more "instrument-like" depth.
- All new status colour is theme-safe in **both light and dark**: level/severity/CRC-CLK badges use solid chips with white text; row/mismatch tints and the filter funnel use translucent ARGB so they read on either background.
- *(I don't have the reference screenshot mentioned in the brief — this pass works from its written description; send the image and I'll tune spacing/colour to match.)*

### G — Efficiency pass (background pipeline)
The compute-heavy pipeline (level assign → property fill → validation → detail-build) now runs on a **background thread** with the busy spinner; only the bound-collection updates happen on the UI thread (the awaited continuation), so the UI never blocks. On **load** the whole pipeline runs in a *single* background pass (load + assign + fill + validate + detail) instead of two hops. **Re-run**, the **Validation/Checklist "Run"** buttons, and **Load 2nd Architecture** all go through the same async path (`MainViewModel.RunPipelineAsync`). Lookups (`SignalByName`) are precomputed once; OtherRequirements is parsed once at load. The 27k-row Reference grid is built once (not on re-validate). *(Profiling against the real ~27k file still needs the runtime + data; the structural wins are in.)*

### E — Hardened AnalogData parser
`SignalDataParser` analog key-matching is now a **configurable alias table** (`AnalogKeyAliases`) — alias-equality / prefix / substring, with more aliases (FR/EN: `valeur_min`, `min`, `minimum`, `borne_inf`; `pas`/`step`/`lsb` for resolution; etc.) and the old duplicate `"unit"` check removed. Tune labels in one place if a real Exchange File differs. (LogicalData `[Etat_N: …]` is confirmed and unchanged.) Also marked the per-ISR detail's **Coding/Meaning** rows informational, since logical state-names vs binary coding aren't directly comparable (avoids permanent false-red).

### D — Live checklist that jumps to the findings
Each checklist row is now a **button**: clicking it opens the **Validation page filtered to that check's rule + worst severity** (e.g. clicking "Duplicate ISR" with errors → Validation filtered to rule *Duplicate ISR*, severity *Error*). Each `CheckSummary` carries the `Rule` string it emits; `ChecklistViewModel.OpenCheck` → `MainViewModel.NavigateToCheck` → `ValidationViewModel.FocusOn(rule, severity)` + page switch. Manual/external/not-run checks aren't navigable. Hover highlight + a "›" affordance signal it's live.

### C — Expandable per-ISR detail (replaces the static side card)
Selecting an ISR row now expands an inline **full-detail panel** (`DataGrid.RowDetailsTemplate`, `RowDetailsVisibilityMode=VisibleWhenSelected`) showing:
- A **side-by-side ISR(online) vs AT(Message Set) comparison table** — Tx/Rx, Frame, PDU, Size, Unit, Min, Max, Resolution, Coding, Meaning, Period, Unavailable value, Network path, Change-mgmt n°. **Mismatched rows are tinted red** (blank-tolerant, numeric-aware match, mirroring `PropertyComparisonService.Same`).
- **CRC & Clock shown as distinct status badges** ("reuse existing" green / "new needed" amber / "present (coverage unknown)" blue / "n/a" grey), driven by new `IsrDemand.CrcStatus` / `ClkStatus` that `PropertyFillService` now records alongside the existing `CrcNote`.
- The **validation findings for that ISR inline** (severity-coloured rule chips) + a "clean" placeholder.
- The per-ISR **note editor**.

Built by a new pure `IsrDetailBuilder` service (parses Logical/Analog via `SignalDataParser`, matches the `SignalDef`, fills `IsrDemand.Detail`), run as part of the pipeline.

### B — Level + Property Fill merged into the Exchange File page
- The standalone **Level Assignment** and **Property Fill** pages (and their view-models) are **removed**. Their data now lives as columns on the unified Exchange File grid: the **Level chip**, filled **Frame / PDU / Bits**, and **CRC/CLK** (all already present, now the single home for them).
- Level + fill orchestration moved into `MainViewModel.RunPipeline()` (assign levels → fill → validate → refresh every page); it runs automatically on load.
- The Exchange File page gains a **Re-run level + fill** action and the relocated **Load 2nd Architecture (L2.1)** action, plus a live **level-distribution + fill-status** summary line (the old page summaries, merged into one bar).
- Sidebar is shorter: Workspace (Dashboard, Reference Data) → Validation (Exchange File, Validation, Checklist, ISR vs Message Set, Other Requirements, Export).

## v2 — Step 3 (AT validation corrected + UX)
- **AT validation reworked** = ISR-online vs Message-Set. The ISR-side properties are now parsed out of **LogicalData / AnalogData**: AnalogData → unit / min / max / resolution; LogicalData → states → size, coding, meaning. Each is compared to the Message-Set definition. (`SignalDataParser`, `PropertyComparisonService`.)
- **Two sections** in the rail: **ISR vs Message Set** (signal properties) and **Other Requirements** (Tx / Rx / UV / Network Path).
- **Per-ISR notes** — select a row on the Exchange File page → editable note in the detail panel (replaces the global notes page).
- **Light/Dark fixed** — default light; row/severity tints are now translucent so they read correctly in both themes.
- **Filtered counts** — the count line on Reference Data, Exchange File, Validation and the compare pages now reflects the *filtered* rows, not the totals.
- **Colour** — dashboard KPI cards now have coloured accents (blue / violet / teal / amber / green-red status) toward the reference look.

## v2 — Step 2

## v2 — Step 2 (OtherRequirements + ISR vs Message Set)
- **OtherRequirements parsed** from the demand cell (`Tx:`, `Rx:`, `NetworkPath:`, `UnavailableValue:`, `ChangeManagementNumber:` — tolerant of newlines / ';' / "1. 2." numbering).
- **#17 Other Requirements check:** Tx must equal Emitter, Rx must equal Receiver (ECU names normalized), and UnavailableValue format checked (hex 0x for >4-bit, binary 0b otherwise). Findings appear inline + on the checklist.
- **#16 ISR vs Message Set** (new left-rail section, under Validation): compares ISR-declared properties (Tx/Rx/size/unavailable-value from the demand + OtherRequirements) against the Message-Set definition, row per property with Match/Mismatch chips. "Show mismatches only" toggle.

### Still queued (the big UI step)
Merge Level + Fill into the Exchange File page; click an ISR → expandable side detail (ISR vs AT side by side); CRC/CLK shown distinctly; results inline; dynamic show/hide columns + Excel-style filter/sort/multi-select on Reference Data and Exchange File; live checklist that jumps to issues.

## v2 — Step 1 (foundation)
- **Three separate inputs** now: Exchange File (demands), Msg-Set/PDU (Message List + Network Path + Dico), and a standalone ISR-Applied file. Same headers, just separate files. `LoadV2` in ReferenceDataLoader.
- **Architecture selector** in the load bar (C1A / C1A-HS / C1A-HS evo / N FACE).
- **Notes** page — free-text working notes for the session.
- **ISR Status Diff removed.**
- **Cleaner dashboard** (KPI strip), **grouped sidebar** (Workspace / Validation / More), and **light theme by default** to match the requested look (toggle still there).

### Coming in the next steps (sequenced)
- Merge Level + Property Fill into the Exchange File page; click an ISR -> expandable side detail (ISR vs AT side by side), CRC/CLK shown distinctly, results inline.
- Reference Data + Exchange File: dynamic show/hide columns, Excel-style per-column filter + sort, multi-select search results.
- Live checklist that redirects to the matching issues.
- New section: ISR-online vs Msg-Set signal-property mismatch.
- Other Requirements check (Tx / Rx / Unavailable-Value matching).

## Earlier — full suite

## Earlier — full suite NEW in this build — automation of remaining manual steps + design upgrade

**Automated (was manual):**
- **ECU-name replace (FACE)** — PIU_Mst→PIU_MASTER, PIU_Hood→PIU_HOOD, PIU_Sub→PIU_SUB applied automatically at load; shown as a completed Tool step on the checklist.
- **Multisender check (L2)** — uses the Message List's per-ECU T/R node columns (now auto-detected and loaded) to flag L2 demands whose new transmitter differs from the signal's existing transmitters.
- **Network route check (FindRxandTx)** — verifies each demand's PDU/frame + Tx→Rx pair exists in the Network Path sheet; flags routes that may need creation.
- **Tx/Rx-aware CRC/CLK decision (AddISRClockAndCRC)** — an existing CRC/Clock is "reusable" only if its node columns show T at the demand's emitter and R at the receiver; otherwise "new CRC/Clock may be needed". See the CRC-CLK note per row.
- **Whitespace check** + **L3 digital sizing** (Etat_N count → required bits).
- **ISR Status Diff (Phase 5 / Process macro)** — new page: pick an external ISR-Status export; it matches by ISR number and diffs AnalogData (Valeur_min/max) and LogicalData (Etat_N) token-by-token, plus presence mismatches both ways.

**Design / UX:**
- **Dashboard** landing page with KPI cards (demands, level distribution, clean/warning/error, fill status, checks passed, reference sizes) and a one-line verdict.
- **ISR vs AT compare panel** (UF_CompareISRandAT equivalent) — select any row on the Exchange File page to see the request vs the AT definition (frame/PDU/bits/coding/meaning/period) plus all findings for that row, side by side.
- **Light/Dark theme toggle** (left rail, keeps the indigo accent).
- **Export Report** button on the Checklist page — writes a 3-sheet workbook (Checklist with status colours, Findings, Demands overview).

**Still manual (genuinely not automatable from these files):** preparing the Exchange File itself, the L2.1 frame-props/bit-byte cross-check (needs both architectures' layout columns), the external AT-validation tool run, and the 2-reviewer Other-Requirements check. These stay listed on the checklist so nothing silently disappears.


## Earlier: full ExchangeFileValidation_C1AHS + Validation Points checklist
The validation suite now covers the macro **and** the "Validation Points" checklist:

Tool checks (run automatically, shown as findings + on the Checklist dashboard):
- Empty mandatory fields · Logical/Analog mutual-exclusivity · Duplicate ISR · Signal present in AT · **Signal-name case-exact (L1/2/2.1)** · Coding/meaning vs bit-size · **Analog signal bit-size** (= ROUNDUP(LOG2((Max−Min)/Res+1))) · **Special-signal profiles** · **Update-time rule** (4 scenarios, Module7) · ECU code (Dico).
- **Special-signal profiles** = the ~770-line block of ExchangeFileValidation_C1AHS, now data-driven: DTOOL/FTOOL (64-bit diagnostic), WakeUpType (3-bit 0b111), WakeUp_Signal (16-bit 0x83C0), WakeUpSleepCommand (2-bit 0b11), SIGMA_DTC (24-bit), ReadMeReq — each checked for size/coding/transmission/period/meaning.

### NEW page — Checklist
A dashboard listing every validation step grouped by section (Basic check / Level / Main validation), each with a Pass/Warning/Error/Manual chip and an "x/y ok" result. Manual + external steps from the checklist (prepare file, ECU-name replace, L2.1 bit-byte cross-check, ISR-Online tool, Other-Requirements 2-reviewer) are listed too so the picture is complete.

### NEW page — Exchange File
A wide, Exchange-File-style grid of all demands (codes, ECUs, signal, filled frame/PDU/bits, CRC/CLK, update-time, issues) with **rows tinted red/amber by worst finding** — the clean equivalent of the macro's cell highlighting. Filter by status (Clean/Warning/Error), level, and scoped search.

### Still deferred (honest list — need external input or more column mapping)
- **`Process` — ISR Status import + Analog/Logical diff** (Phase 5): needs the *external* ISR-status export file. Not built yet.
- **`FindRxandTx`** routing resolution via Network Path (data is loaded; logic pending).
- Magic-signal **sub-column** checks (byte/bit-position cols 47–51/61/62): need your column-mapping confirmation; I check size/coding/transmission/period/meaning, which are the substantive ones.
- L3 **digital** states-vs-bits precise check (the analog one is in).

## Phase 4 — Export (212 / 270) (new)
- Generates the Alliance import sheet via ClosedXML. Pick format (270 / 212), optionally export only rows marked "Ready to import".
- Forces text format on Feature_Number / EmitterCode / ReceiverCode (leading zeros survive). Suggested filename `Preevision_ISR_Import_<fmt>_C1AHS_dd_mm_yyyy.xlsx`.
- ⚠ Confirm-later: we don't yet carry the macro's "Import 270 = Ready to import" gate column, and the exact 212-vs-270 column difference. The current column set is a sensible superset — send me a real Alliance export and I'll lock the exact layout per format. `Services/ExportService.cs`.

## Phase 3 — Property Fill + CLK/CRC (new)
- For each demand, looks up its signal in the Message List (or the 2nd architecture for L2.1) and fills Frame, Frame ID, PDU, Bits, Value Type, Unit, Min, Max.
- Reports whether the target frame already carries a **CRC** / **Clock** signal, and notes when one may be newly required.
- Status filter (Filled / Pending L2.1 / New), scoped search, live counts.
- The **Load 2nd Architecture** button on the Level page now loads *full defs* and automatically re-runs level + fill + validation.
- ⚠ Confirm-later: the macro's full CRC/CLK *need* decision is Tx/Rx-aware and uses the Message List's per-ECU T/R columns, which we don't load yet. Phase 3 reports frame-level CRC/CLK presence; wiring the ECU columns upgrades it to the full decision. `Services/PropertyFillService.cs`.

## Phase 2 — Validation Suite (new)
Runs automatically after level assignment on load (re-run with **Run Validation**). Findings show as a filterable report — one row per finding with a severity chip, the rule, ISR, signal and message.

Rules implemented:
- **Duplicate ISR** — same ISR n° on more than one demand row (Error).
- **Signal in AT** — L0/L1/L2 signals must exist in the Message List; an L3 (new) signal that already exists is flagged (Error / Warning).
- **ECU code (Dico)** — Emitter/Receiver name must resolve to the code on the row via the Dico sheet (Error if mismatch, Warning if name missing).
- **Coding / bit-size** — an N-bit signal should have 2^N meaning lines (Warning). *Note: can be chatty if your Message List only documents used values rather than all codes — filter it out or we can tune the rule.*

Filters: severity, rule, plus scoped search (All / Signal / ISR / Rule / Message). Summary shows error/warning counts and how many demands are clean.

The validation engine is pure passes over the models, so each rule is unit-testable. More rules (update-time, name/definition consistency, Rx/Tx routing) slot into `Services/ValidationService.cs`.

## Phase 1 — Level Assignment (new)
- Loads the incoming ISR demands from the main **ExchangeFile** sheet.
- Auto-classifies every demand **L0 / L1 / L2 / L2.1 / L3** on load (re-run with **Assign Levels**):
  - L0 = ISR n° already in ISR-applied · L1 = Signal+Tx+Rx match · L2 = Signal+Tx match · L2.1 = signal in a 2nd architecture's Message List · L3 = new.
- **Load 2nd Architecture (L2.1)** button: pick another arch's Message List to resolve 2.1 vs 3.
- Colour-coded level chips, per-row "Basis" note, level filter + search, live counts summary.
- Left-rail now switches pages (Reference Data ↔ Level Assignment) via a ContentControl + DataTemplates — phases 2–5 slot in the same way.

### ⚠ Verify the match fields (only you can confirm this)
Level matching maps `demand.ParameterProposal → applied.Parameter`, `demand.Emitter → applied.Transmitter`, `demand.Receiver → applied.Receiver`. If your ISR-applied sheet actually keys off the PREEvision Tx/Rx columns or ECU **codes** (via Dico) instead of names, fix it in one place: `Services/LevelAssignmentService.cs` (the `Key(...)` calls). Test against a handful of ISRs whose level you already know.

## What's working
- **Header-mapped loaders** (`Services/ReferenceDataLoader.cs`) — read by column *name*, not index, so they survive column reordering. Handles multi-line headers like `Signal Size\n(Bits)`. Loads: Message List signals, ISR-applied, Dico, Network Path.
- **Async load** off the UI thread with progress text; `.xlsm` opens fine (macros ignored).
- **Models** (`Models/Models.cs`) — `SignalDef`, `IsrDemand`, `AppliedIsr`, `EcuDicoEntry`, `NetworkRoute`, plus the `IsrLevel` enum and `ValidationResult` record the later phases consume.
- **Virtualized DataGrid** + live `ICollectionView` filter.
- Indigo/violet accent (`#6D5DF5`) + dark theme.

## Next phases (already modelled for)
- **Phase 1 — Level assignment:** `AppliedIsr.SignalTxRxKey` / `SignalTxKey` are the match keys for L1/L2; L0 = ISR-number hit; L2.1 = second-architecture lookup; L3 = none.
- **Phase 2 — Validation suite:** results land in `IsrDemand.Results` as `{Rule, Severity, Message}`.
- Phases 3–5 per the spec doc.

## Caveats (I couldn't compile here — no Windows/.NET runtime in my sandbox)
- **Targets `net10.0-windows`** with current stable packages (WPF-UI 4.3.0). The accent call uses positional args for the WPF-UI 4.x signature (already applied).
- **Icon names** (`Database24`, `Layer24`, etc.) are Fluent `SymbolRegular` values — if one doesn't resolve, swap for a neighbour in IntelliSense.
- Remove the `<ApplicationIcon>` line in the csproj until you add `Assets/app.ico`.
- ClosedXML loads the whole workbook into memory; the 35 MB .xlsm + 21 MB .xlsx are fine, but first load takes a few seconds — that's why it's async with progress.
