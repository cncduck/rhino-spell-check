# Rhino Spell Check

A spell checker for Rhino 8 text, leaders, dimensions and text dots. Two commands
do the checking; a dockable **Spell Check** panel is where the misspellings are
reviewed, one word at a time, the way a word processor's spell checker works.
Each word is shown by selecting its object and zooming to it.

Everything runs locally. The dictionary ships with the plug-in; nothing is sent
anywhere.

## Download

Grab `RhinoSpellCheck-0.1.0.zip` from the
[Releases](https://github.com/cncduck/rhino-spell-check/releases) page, unzip it
somewhere permanent, and drag `RhinoSpellCheck.rhp` onto a running Rhino 8
window. Keep the unzipped folder together — the plug-in loads its dictionary from
the `Dictionaries` folder next to the `.rhp`. Then run `CheckSpellSelected` or
`CheckSpellLayout`.

Windows may block files downloaded from the internet: if Rhino refuses to load
the plug-in, right-click the zip → **Properties** → tick **Unblock** before
unzipping.

## Requirements

- Rhino 8 for Windows
- .NET SDK 10 (only to build from source)

The plug-in targets `net48`. Rhino 8 for Windows runs on .NET Core by default and
loads `net48` plug-ins there, and it also loads under the .NET Framework runtime
(`SetDotNetRuntime`), so there is nothing to switch.

## Build

```powershell
.\build.ps1
```

**Close Rhino first.** While the plug-in is loaded Rhino holds `RhinoSpellCheck.rhp`
open, and the build fails with `MSB3027: ... being used by another process`.

That produces `src\RhinoSpellCheck\bin\Release\`:

```
RhinoSpellCheck.rhp          the plug-in
WeCantSpell.Hunspell.dll     managed Hunspell engine (no native binaries)
System.*.dll                 netstandard polyfills the engine needs
Dictionaries\en_US.aff       affix rules
Dictionaries\en_US.dic       ~49,500 word stems
```

## Install

Drag `RhinoSpellCheck.rhp` from that folder onto a running Rhino 8 window, or use
**Tools → Options → Plug-ins → Install**.

Rhino loads the plug-in from wherever it sits, and the plug-in looks for its
dictionaries in a `Dictionaries` folder **next to the .rhp** — so keep the build
folder intact rather than copying the `.rhp` out on its own.

## Using it

**Commands**

| Command | What it does |
| --- | --- |
| `CheckSpellSelected` | Checks the selected text, then opens the panel on the first misspelling |
| `CheckSpellLayout` | Checks one layout sheet or all of them, then walks the misspellings sheet by sheet |

Both start with `CheckSpell`, so typing that much offers both. The panel does not
scan; these commands are the one way in.

**`CheckSpellSelected`** takes a preselection or asks for one. Only text, leaders,
dimensions and text dots can be picked, so a window over a whole drawing selects
just the things that have words in them.

```
Spell Check: 3 misspelling(s) in 14 text item(s) - review them in the Spell Check panel.
  Checked 9 of 14 text items (64.3%), 41 words.
    5 skipped as ALL CAPS - untick 'Ignore ALL CAPS words' to include them.
```

A clean selection prints one line and leaves the panel closed.

**`CheckSpellLayout`** takes `_Current`, `_All` or `_Named`. Pressing Enter takes
the obvious default: the sheet you are on, or the whole set if a modelling view is
active. `_Named` prints the layouts and takes a name, because layout names contain
spaces and command options cannot. Sheets are checked in tab order.

```
  A-101: 2 misspelling(s): drawign x2
  A-102: no misspellings in 38 text item(s)
  A-103: 1 misspelling(s): mispelt (+1 shown on an earlier sheet)
Spell Check: 3 misspelling(s) on 2 of 3 layout(s) - review them in the Spell Check panel.
  Checked 61 of 233 text items (26.2%), 287 words.
    163 skipped as ALL CAPS - untick 'Ignore ALL CAPS words' to include them.
```

Then the panel walks the set. It switches to the first sheet with anything to fix
and says where you are:

```
3 layouts: 3 left to review (0 changed, 0 ignored).
Sheet 'A-101': word 1 of 2 - sheet 1 of 2 to fix
```

**Next** past a sheet's last word moves on to the next sheet with anything to fix;
**Previous** goes back across sheets the same way. Fixing the last word on a sheet
moves on too.

A dimension or note in model space that shows through details on two sheets is
reviewed once, on the first sheet it appears on — "+1 shown on an earlier sheet"
above. Otherwise fixing it on one sheet would leave a twin on the next that could
only go stale.

**Panel workflow**

1. The current word appears with its object type, layer and surrounding text, and
   its object is selected and zoomed to.
2. Pick a suggestion from the drop-down, or type your own into **Replace with**.
3. **Change** applies it; **Change All** applies it to every remaining occurrence
   in the check — across every sheet, on a layout walk.
4. **Next / Previous** step through the remaining words.
5. **Ignore** / **Ignore All** skip a word for this session; **Add to Dictionary**
   accepts it permanently.

Every edit goes into a Rhino undo record, so `Ctrl+Z` reverses it.

The **What to check** options at the bottom of the panel apply to the next check.

Closing the panel discards the check, and so does closing the document it came
from.

**What a sheet contains**

A layout check collects two different populations, because both are things you
can see on the page:

- the sheet's own annotations — the title block, sheet notes, anything placed in
  page space on that layout;
- **model-space annotations framed by the sheet's details** — the dimensions and
  callouts on the drawing itself, which are usually not page space at all.

See *Layouts and details* below for how "framed by a detail" is decided.

The personal dictionary lives at
`%APPDATA%\RhinoSpellCheck\user.dic` — one word per line, editable by hand.

## What gets checked

Checked: text objects, leaders, dimensions (the user-text override), and both
labels of text dots.

Deliberately skipped, because CAD annotation is full of things a prose dictionary
would flag on every line:

- Rhino text fields — `%<ModelUnits>%` and friends
- URLs, e-mail addresses and file paths
- Words shorter than the minimum length (default 3)
- `ALL CAPS` words — `TYP`, `NTS`, `SIM` (toggleable)
- `MixedCase` words — `RhinoCommon`, `McNeel` (toggleable)
- Letter runs touching a digit — `12mm`, `3Dprinted` (toggleable)

The last four are options in the panel and persist between sessions.

### Coverage reporting

Those defaults are aggressive enough to be misleading, so every check reports how
much of the drawing it actually read, naming the option that would widen each
bucket. The panel shows the short form when nothing is found:

```
No misspellings found. Checked 251 of 4241 text items (5.9%) - 1277 skipped by the current options.
```

This exists because of a measurement on a real production drawing: 3,990 of its
4,241 annotations contained no word the tokenizer would emit, and 1,277 of those
purely because they are `ALL CAPS` — which drawing notes almost always are. With
the defaults the checker examined **5.9%** of the drawing and reported nothing
wrong. Silently checking a fraction is a bad failure, because the user has no way
to tell it happened.

Text containing nothing word-like (`1/2" = 1'-0"`) is reported separately and is
deliberately *not* counted as a blind spot — there is nothing in it to check, and
blaming an option would send the user to change a setting that changes nothing.

## How corrections are applied

Rhino stores annotation text as RTF. Writing the corrected string back through
`PlainText` is a one-liner but discards every bold/italic/font run in the
annotation, so instead the plug-in parses the RTF, maps the misspelled word back
to the byte range that produced it, and splices the replacement in place. A word
split across two formatting runs is handled by preserving the markup between the
characters being replaced.

Dimensions are a separate case: they leave `PlainText`/`RichText` empty and keep
the user's override in `PlainUserText`/`TextFormula`, where a `<>` token stands in
for the measured value. Writing to the wrong pair would silently destroy the
measurement, so all access goes through `AnnotationText`.

Two safeguards:

- Before writing, the word must still be exactly where the check found it. If the
  object changed in the meantime the issue is marked *stale* and nothing is
  written.
- If the RTF splice cannot be performed safely, the plug-in falls back to a plain
  write and reports on the command line that formatting was simplified.

## Layouts and details

### Which model text is on a sheet

Model text belongs to a sheet when, mapped through one of the sheet's switched-on
details onto the page (`DetailViewObject.WorldToPageTransform`), it lands inside
that detail's frame. That is the definition of "appears in this detail", and it
is what decides.

It replaced `detail.Viewport.IsVisible`, which measured headless is wrong in both
directions. A detail framing model space from -20 to 20 called text at x = 206
visible — it maps to x = 24 on an 11-inch sheet whose detail spans 1 to 6 — and
after re-framing the same detail elsewhere it called text squarely in its middle
invisible, with a page-to-model ratio of 0. Its frustum evidently does not track
the window it draws through. Perspective details still also require `IsVisible`,
because a projective mapping cannot tell what is in front of the camera from what
is behind it; drawing sheets are parallel projections, so that path is the
exception.

The harvest and navigation share this test (`DocumentScanner.FindDetailShowing`),
so a word the walk puts on a sheet can always be framed on it.

### Where navigation goes

With no underlines, selecting the word's object and zooming to it is how the user
sees which word the panel means. `IssueNavigator` decides the view:

- a word on a layout sheet activates that sheet and zooms the sheet view to it;
- a model-space word on a layout walk — a dimension seen through a detail — is
  framed **on the sheet**, where it appears through the detail, so reviewing a
  sheet never bounces you out to a modelling view;
- a model-space word found by `CheckSpellSelected` is framed on the sheet if you
  are on one and a detail shows it, otherwise in a modelling view.

Two rules hold throughout. **A detail viewport is never zoomed**: zooming a detail
changes its scale, which edits the drawing rather than moving the camera. Every
zoom on a sheet moves the sheet's own view, after stepping out of any active
detail. And the integration suite checks the detail's world-to-page mapping is
unchanged after navigating — that mapping *is* its scale and framing.

## Layout

```
src/RhinoSpellCheck/
  Core/          no Rhino dependency — tokenizer, Hunspell wrapper,
                 RTF splicer, session and sheet-walk bookkeeping
  Documents/     document scanning, the layout sweep, navigation and the
                 annotation read/write layer
  UI/            the Eto.Forms panel
  Commands/      CheckSpellSelected, CheckSpellLayout
tests/
  RhinoSpellCheck.Tests/     xUnit, compiles Core sources directly on net8
  RhinoSpellCheck.Harness/   Rhino.Inside integration harness
  RhinoSpellCheck.Panel/     the panel, driven in a hidden Rhino
```

`Core` is kept free of RhinoCommon on purpose: the tokenizer, the RTF splicer and
the offset-shifting logic are where the subtle bugs live, and this lets them be
tested on a normal test runner.

## Tests

```powershell
.\tools\test.ps1              # everything, with one set of totals
.\tools\test.ps1 -Skip panel  # everything except the panel
```

Three suites, each covering something the others cannot:

| Suite | Covers | Cannot see |
| --- | --- | --- |
| unit | Core logic, no Rhino | anything touching a document |
| integration | real geometry, headless | no viewport, no UI |
| panel | the Eto panel, driven in a hidden Rhino | real objects behind the words |

Individually:

```powershell
dotnet test tests\RhinoSpellCheck.Tests
.\tests\RhinoSpellCheck.Harness\bin\Debug\net48\RhinoSpellCheck.Harness.exe
.\tests\RhinoSpellCheck.Panel\bin\Debug\net48\RhinoSpellCheck.Panel.exe
```

The integration harness boots Rhino in-process (needs a licensed Rhino 8) and
checks plain and RTF text, leaders, dimensions, text dots, multi-line offsets and
context, model/page space tagging, which text belongs to which sheet, the layout
sweep (set order, one review per shared dimension), detail-safe navigation that
frames model text on its sheet, selection harvesting, stale-edit rejection and
Change All.

The sheet-membership checks were run against the old `IsVisible`-only rule: 6 of
them fail there, including text beside a detail being harvested onto the sheet.

**Both Rhino-hosted suites load every plug-in you have installed**, because they
boot a real Rhino. They run on .NET Framework 4.8, so a plug-in built only for
.NET 7/8 fails to initialise in them and Rhino reports it — that is the test
process, not your own Rhino.

### The panel suite

`RhinoSpellCheck.Panel.exe` builds the actual Eto panel inside a hidden Rhino and
drives it — publishing checks, calling the `IPanel` lifecycle methods, pressing
Next and Previous, reading the labels back. It covers adoption whether the panel
exists yet or not, the idle message, the sheet walk line and moving across
sheets, clean checks replacing old ones, and the options.

It exists because every bug found by hand on this project, before the suite
existed, was panel behaviour that the other suites passed straight through.

What makes it work, measured rather than assumed:

- Rhino started with `WindowStyle.Hidden` initialises Eto on the Wpf platform, so
  panel controls construct normally.
- `new SpellCheckPlugin()` throws — *"this is the job of the plug-in manager"* —
  but Rhino has already loaded the plug-in, so `SpellCheckPlugin.Instance` is a
  properly constructed one. With the assembly preload, it is **this build's**
  instance. The suite prints where it loaded from and says so if the plug-in is
  missing, rather than reporting a vacuous pass.
- The panel can be constructed directly and its `IPanel` methods called, which is
  exactly how Rhino drives it.
- Adoption happens on the Eto main loop, so the suite turns it over with
  `Application.Instance.RunIteration()` before asserting.

Private state is read by reflection, deliberately: these assert behaviour the
user sees, and widening the panel's public surface just to observe it would be
worse than the reflection.

`tools\test.ps1` runs it with a five-minute limit, because it has been seen to
stall once.

## Known limitations

- **Block definitions are not checked.** Text inside an instance definition is
  shared by every instance, so correcting it would silently change other parts of
  the model. `IdefObjects` is off in the scanner.
- **The word is shown by its object, not underlined.** The panel names the word
  and shows the text around it; the viewport selects the whole annotation.
  Earlier builds drew underlines, which needed a measurement-based guess at where
  each word sits and were wrong often enough on leaders to be removed.
- **Only `en_US` ships.** Any Hunspell `.aff`/`.dic` pair dropped into the
  `Dictionaries` folder will be picked up by `SpellEngine.AvailableLanguages`,
  but the panel does not yet expose a language selector.
- **A large `_All` check runs on the main thread.** Rhino is unresponsive until
  it finishes; the prompt shows which sheet it is on.

## License

MIT — see [LICENSE](LICENSE). The bundled dictionary has its own terms, below.

## Dictionary licensing

`Resources/Dictionaries/en_US.*` comes from the
[LibreOffice dictionaries](https://github.com/LibreOffice/dictionaries) project.
See `en_US-LICENSE.txt` in that folder for the terms before redistributing.

The engine is [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell),
a managed port of Hunspell (MIT), chosen so the plug-in has no native
dependencies to deploy per-architecture.
