# Effects, landscape crop, cards, deck speed

One-sentence outcome: Orton glows in the bright parts of each photo (not a bleach over the whole frame); grain is real texture, not a gray wash; both stay off the borders; landscape stays 3:2; cards and the color menu stop fighting the UI; deck thumbs load fast.

## Scope

In:
- Fix Orton and noise (correct recipes, visible on stage, not glacial).
- Do not paint effects on gutters / frame / canvas fill — only the photos.
- Advanced menu with numeric knobs for both effects (values on screen, not mood names).
- Color dropdown readable on dark UI (no giant empty white panel).
- Compact layout cards: no repeated AxB; in/out still obvious.
- Faster low-quality deck thumbs.
- Landscape output uses 3:2 (normal still photo), not Instagram 5:4. Stop cutting horizontal photos into 5:4.

Out:
- Changing combo pack job list.
- Native camera-pixel exports (long edge still capped).
- Per-slot different effect amounts.
- High-pass / “clarity” Orton (glow from sharp edges). That is a different look.
- Scanned film-grain overlays.

## Why the current effects bleach

`PostComposeEffects` today:

- **Orton:** blur the **entire** collage at 3% of min side, **Screen** it, mix **50%**. Screen only lightens. There is no mask, so shadows and midtones lift too → milky, faded, “bleached.” Photographers who still use Orton confine it to **Lights** (highlights / upper mids) with a **feathered luminosity mask**. The original Orton sandwich is a sharp slide plus an **overexposed, out-of-focus** slide — the bloom lives where the scene is **bright**, not as fog on every pixel.
- **Noise:** `Gaussnoise` with mean 128, then `out = 0.88·photo + 0.12·noise`. That **blends the picture toward mid-gray**, which is haze, not grain. Real grain is a **zero-mean** wobble on luminance, stronger or weaker by tone.

“Clearest parts” in this product means **brightest tones** (sky, skin highlights, sunlit patches), with a **soft falloff** into the mids. It does **not** mean the sharpest / in-focus pixels.

## Decisions (locked)

- **Landscape canvas is 3:2, 4800×3200.** Add `Constants.LandscapeCanvasWidth = 4800` and `LandscapeCanvasHeight = 3200`. Portrait stays `CanvasWidth/Height` 3840×4800. `ResolveCanvasSize` uses those constants (stop swapping portrait W/H). `grid-2x2-h` and **stack-1 landscape** are 4800×3200. Stack-1 portrait stays 3840×4800. Cover into those cells: a 3:2 landscape fills stack-1 landscape with no side chop.
- **Effects apply to photo cells only**, after cover/pan, before paste onto the colored canvas. Delete the post-compose whole-canvas pass (`PostComposeEffects` on the finished collage). Same path for preview and export.
- **Orton (highlight glow, not whole-frame haze):**
  1. Keep a sharp base.
  2. Duplicate; mild lift on the copy (1.12); Gaussian blur using **Blur %**.
  3. Blend that glow with **Soft Light** onto the base (not unmasked Screen).
  4. Restrict with a **feathered luma mask**: `M = smoothstep(Mask low, Mask high, luma)` then blur `M` by **Feather %** of cell min side. If Mask low > Mask high, swap.
  5. Mix: `out = base + Amount · M · (glowBlend − base)`. Where the mask is 0, the pixel is untouched.
  - Defaults: Amount **0.22**, Blur **0.70** (% of cell min side), Mask low **0.48**, Mask high **0.82**, Feather **0.25** (% of cell min side). Ranges: Amount 0–1, Blur 0.30–1.80, Mask low/high 0–1, Feather 0–1.00.
- **Noise (luma grain, not gray mix):**
  1. Build **zero-mean** monochrome noise. **Do not** lerp toward mean-128 noise.
  2. Spatial blur on the noise using **Size** (pixels at 1000 px long edge; scale with image long edge so export matches).
  3. Add to **luminance**, then scale RGB so hue stays (`L' = clamp(L + Amount · W · k · n)`, then `RGB *= L'/L`). Gain `k` ≈ 0.15.
  4. Tone weight `W`: `W = Shadows · (1 − t) + Highlights · t` where `t = smoothstep(0.25, 0.75, luma)`. Default Shadows **1.00**, Highlights **0.15** (grain mostly in darks). Set Shadows 0 / Highlights 1 for lights only; both 1 for everywhere.
  - Defaults: Amount **0.08**, Size **0.90**. Ranges: Amount 0–1, Size 0.40–2.00, Shadows 0–1, Highlights 0–1.
- **Sidebar:** Noise and Orton stay checkboxes (on/off only). Numeric knobs live in a collapsed **Advanced** expander under those checkboxes — not mood names (no Glow, Softness, Bright areas, Dark/Light). Each row is: parameter name, slider, number (two decimals, typed value allowed). Two groups inside Advanced: **Orton** and **Noise**. Knobs stay visible in Advanced even if the matching box is off (so you can set numbers then tick); they are enabled whenever the matching box is on. Values live on `EditableCollage` with the flags; undo snapshots them. Live preview uses the same debounce as pan, not a full export.
  - Orton rows: `Amount`, `Blur %`, `Mask low`, `Mask high`, `Feather %`.
  - Noise rows: `Amount`, `Size`, `Shadows`, `Highlights`.
- **Stage speed:** cache the last **uneffected** preview compose (cells already pasted, no grain/Orton). Toggling or dragging effect knobs re-applies only the cheap effect stack on that cached bitmap. Turning both off shows the cache. Changing photos/pan/layout/bleed/color invalidates the cache.
- **Deck thumbs:** long edge **240**, skip Orton/noise on deck (stage shows the real finish). Two concurrent renders stay.
- **Color combo:** dark popup (`#2A2A2A` background, white item text). `MaxDropDownHeight="240"`. Keep showing the selected name in the closed box (do not blank `Text` on preset pick). Editable custom hex still allowed.
- **Layout cards (two lines, compact):**
  - Line 1: short name only (`Stack 3`, `Grid 2×4`, `Split 1×2 V`, `Row 1×3 V`, `Grid 2×2 H`, `Stack 1`).
  - Line 2: `in H → out V` (or `in any → out photo` for Stack 1). Never a third AxB line. Names that already contain 2×4 / 1×3 do not repeat the grid on line 2.

## Phase 1 — Color dropdown

Restyle ComboBox popup in `App.xaml` (dark list, white items, selected highlight). `MainWindow` ColorCombo: MaxDropDownHeight; stop clearing Text when a preset is chosen; keep Width 160.

Done when: open Color, see named colors (lime, red, …) on a dark list; closed box shows White or the chosen name, not a blank/huge white hole.

How to verify: build App, open the dropdown. No full tests.

## Phase 2 — Compact layout cards

Change `LayoutCardCopy.BuildCardText` to the two-line rule. Update `LayoutCardCopyTests`. Slightly tighter padding in `BuildLayoutCards` if cards still overflow.

Done when: Stack 3 is two lines (`Stack 3` / `in H → out V`); Grid 2×4 does not also say `2×4` on a third line.

How to verify: those tests + look at the layout grid.

## Phase 3 — 3:2 landscape canvas

`Constants`: landscape 4800×3200. `ResolveCanvasSize` uses them. Portrait unchanged. Rename/update stack-1 landscape tests and `ExportGrid2x2H…` from 4800×3840 to 4800×3200. Add a test that a 3:2 synthetic into stack-1 landscape is not side-cropped (cover window uses full source width). Architecture/README one line each.

Done when: stack-1 landscape JPEG is 4800×3200; a 3:2 synthetic is not side-cropped. grid-2x2-h canvas 4800×3200. Portrait stack-3 still 3840×4800.

How to verify: geometry/export tests named above. No full suite.

## Phase 4 — Real Orton/noise on cells, not borders

Replace canvas-wide `PostComposeEffects.Apply` after `ComposeCanvas` with cell-local apply in `CollageExporter` / `CollagePreviewRenderer` after `ProcessImageForCell` (and preview cell), only if that slot has a photo. Empty/gray slots stay dry. Gutters stay the chosen border color.

Rewrite the helper (rename is fine; **do not** leave a second unused full-canvas path) to the locked recipes:

- Orton = Soft Light glow × feathered luma mask (Mask low / Mask high / Feather %) × Amount. Never unmasked Screen at 50%.
- Noise = zero-mean **luma** grain × `W` from Shadows/Highlights. Never lerp toward mean-128 Gaussnoise.

Pass a small settings record through export/preview (defaults as locked when flags are on). Call sites that only have bools use the defaults.

Tests:

- Effects-on vs dry: photo pixels change; a known gutter pixel (framed layout corner) stays the canvas color.
- Orton-off + noise-off matches dry.
- **Orton does not lift a near-black patch** (synthetic cell with a black square: mean of that square stays within ~2/255 of dry). A bright square **does** gain a measurable glow.
- **Noise Shadows=1 Highlights=0** changes a dark gray patch more than a near-white patch; **Shadows=0 Highlights=1** does the reverse. **Both 1** changes both.
- Two Amount values produce different bright-region (Orton) or grain (Noise) pixels.

How to verify: those Core tests + build. No full suite.

## Phase 5 — Fast stage effects + cheap deck

PreviewStageService: after a successful uneffected compose, keep that RGB/buffer. Effect_Changed and knob moves call a short path: copy cache → apply cell-equivalent effects at preview scale.

Simplest correct cache: cache dry compose (cells without effects, already pasted). For the fast path, run Orton/noise on that bitmap but **skip pixels whose RGB equals the canvas color** (exact match on the known parse of SelectedColor). Borderless layouts have no gutter — whole frame is photo, full-frame apply is OK.

Because Orton/noise are now **luma-masked**, a white or black **border** that matches the mask target must still be skipped by the **canvas-color skip**, not by the luma mask alone (a white gutter would otherwise get Highlights grain and highlight glow).

Deck: PreviewLongEdge 240; pass noise/orton false for deck thumbs.

Done when: toggling Orton on a filled stack-3 updates the stage in a short moment, gutters unchanged; deck cards appear faster and do not wait on Orton.

How to verify: App smoke. No full suite.

## Phase 6 — Advanced numeric knobs

`EditableCollage` fields (with the existing `Noise` / `Orton` flags), defaults as locked:

- Orton: `OrtonAmount`, `OrtonBlurPercent`, `OrtonMaskLow`, `OrtonMaskHigh`, `OrtonFeatherPercent`
- Noise: `NoiseAmount`, `NoiseSize`, `NoiseShadows`, `NoiseHighlights`

WPF **Advanced** expander in Mode & style, under the two checkboxes, **collapsed by default**. Inside, two headers **Orton** and **Noise**, then the rows above. Each row: name, slider, number box showing the live value (two decimals). No Glow / Softness / Bright areas / Where. Undo includes every number. Debounce knob preview like pan. Export uses the same numbers.

Done when: open Advanced, type Orton Amount 0.40, bright regions bloom and blacks stay put; set Noise Shadows 0 and Highlights 1, grain jumps to the lights; numbers persist on that card when you click another deck card and back.

How to verify: App on one collage. Core tests from phase 4 already cover two amounts and Shadows vs Highlights.

## Phase 7 — Audit

Read effects helper (no canvas-wide leftover, no lerp-to-gray noise, no unmasked Screen), exporter/preview, geometry 4800×3200, ColorCombo template, LayoutCardCopy, deck 240, cache invalidation on pan, canvas-color skip so white borders never glow, Advanced expander with numeric values. Confirm combo specs untouched.

Done when: gaps fixed here or named to the human.

How to verify: code read + launch App.

## Order

Phases 1, 2, 3 in parallel (different files). Then 4, then 5, then 6, then 7. One implementer per phase. Phase 5 needs phase 4 recipes. Phase 6 needs the numeric fields from 4’s helper (pass defaults in 4, wire the Advanced expander in 6).

## Estimated duration

- Phase 1: 1–2 agent-hours
- Phase 2: 1–2 agent-hours
- Phase 3: 2–3 agent-hours
- Phase 4: 5–7 agent-hours
- Phase 5: 3–5 agent-hours
- Phase 6: 3–4 agent-hours
- Phase 7: 2–3 agent-hours
