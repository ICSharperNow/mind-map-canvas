<div align="center">

<img src="Assets/icon-1024.png" width="110" alt="MindMap Canvas icon"/>

# MindMap Canvas

**A fast, themeable mind-mapping board for Windows** - built with C# / WPF.
Sketch ideas on an infinite gridded canvas, snap shapes together with anchored
connectors, and export the result anywhere.

[![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6)]()
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)]()
[![WPF](https://img.shields.io/badge/UI-WPF-68217A)]()
[![License](https://img.shields.io/badge/license-MIT-green)]()

</div>

---

## Preview

| Light theme | Dark theme |
|:---:|:---:|
| ![Light theme](docs/preview-light.png) | ![Dark theme](docs/preview-dark.png) |

*(Eight built-in themes - Light, Dark, Slate, Sepia, Midnight, Ocean, Forest, Rose -
plus a custom theme builder. Switch live in **Settings → Settings…**.)*

## Install

**Installer (recommended)** - grab `MindMapCanvas-Setup-x.y.z.msi` from the
[latest release](https://github.com/ICSharperNow/mind-map-canvas/releases/latest)
and run it. It installs to Program Files and adds Start-menu and desktop shortcuts.

**Portable** - download `MindMapCanvas.exe` from the same release page and run it
from anywhere. Fully self-contained: no .NET installation required.

**Updating** - just run a newer installer over an existing install; it upgrades
in place (each release carries a higher version number, which triggers the
upgrade automatically). No need to uninstall first.

**Uninstalling** - remove it like any Windows app: Settings > Apps > Installed apps >
MindMap Canvas > Uninstall (or via Control Panel / `winget uninstall`).

**From source**

```bash
git clone https://github.com/ICSharperNow/mind-map-canvas.git
cd mind-map-canvas
dotnet run
```

Requires the .NET 8 SDK. Opening a board from the command line works too:
`MindMapCanvas.exe myboard.mindmap`.

## Features

### Canvas
- **Infinite board** with a layered grid (subtle minor lines, stronger major lines)
- **Pan** by simply dragging empty canvas - or middle-drag / Space+drag
- **Zoom** 10%-400% with the wheel (anchored at the cursor) or the bottom-right zoom
  dock: slider, +/-, Fit, and 1:1
- Window resizing keeps the view centered on the same spot
- **Snap to grid** toggle for tidy layouts, plus **smart alignment guides**: while
  dragging, shapes snap to the edges and centers of nearby shapes on both axes with
  dashed guide lines showing the alignment
- **Zones** (`P` or the ▧ Zone button): drag out a background area that snaps to grid
  cells - a movable, resizable object behind your shapes for grouping regions of the board
- **Per-object opacity**: the toolbar slider fades any selected object live (shapes,
  text, images, links, zones) and sets the default for new zones
- **Layering**: Bring to front / Send to back on every object's right-click menu,
  preserved in the save file

### Shapes
- Ten shapes: **rectangle, pill, ellipse, perfect circle, diamond, hexagon,
  parallelogram, trapezoid, triangle, octagon**, chosen from a visual gallery with live previews; text stays
  neatly inside each shape's outline
- **Double-click** the canvas to place a shape, or **Alt+drag** to draw one at exactly
  the size you want
- Move by dragging, resize via the corner grip, nudge with arrow keys, and **rotate**
  with the ⟳ handle above a selected shape (Shift snaps to 15° steps) - connections and
  exports follow the rotation
- **16-color palette plus a full HSV custom color picker** (hue strip,
  saturation/value area, hex input); custom colors are remembered and appear
  in the palette dropdown for reuse

### Connections
- Hover a shape to reveal **eight connector dots** (four sides + four corners)
- Drag a dot onto another shape to draw an arrow - while dragging, a ring previews the
  exact dot it will snap to, and the connection **stays pinned to those dots** on both
  ends as the shapes move
- **Multiple connections** between the same two objects, as long as they use different
  dot pairs
- While dragging, the target shape lights up **all of its connector dots** so every
  snap point is visible before you release
- Right-click an arrow to **recolor it** (pick a color, then apply to this connector or
  all of them), reverse its direction, or delete it; new connections reuse your last
  connector color
- Arrows and connector dots hug the true outline of ellipses and diamonds - no floating
  gaps on curved shapes
- Connector dots, grips, and rotation handles **auto-scale with zoom** so they stay
  easy to grab when zoomed out
- Selected shapes get a clear accent glow and thicker outline
- Click an arrow to select it; `Del` removes it

### Images & links
- **Import images** (🖼 or File > Import image) straight onto the board - embedded in the
  save file, resizable, rotatable, connectable like any shape
- Image fit modes via right-click: **Fit, Fill, Stretch, Center**
- **Import links** (🔗 or File > Import link): the page is loaded off-screen and a real
  **preview screenshot** becomes the shape's content, with a domain banner; double-click
  opens the link in your browser, right-click offers Open, Change address, and
  Refresh preview (falls back to a simple link card if the page can't load)

### Clipart & templates
- **Clipart gallery** (😀 Clipart): 120 symbols across ten categories, in matching
  **Color** and **Black & white** tabs - color clipart inserts as crisp scalable images,
  monochrome clipart stays recolorable text that scales with its box
- **Start from a template** (File > New > From template): mind map, flowchart, SWOT,
  kanban, org chart, or timeline starter boards, each with a rendered layout preview

### Text
- Double-click a shape to type; Enter commits, Shift+Enter adds a newline
- Per-shape **font family, size, bold, italic, alignment, and text color** via the
  `Aa Format text` dropdown (eight fonts included) - every option **live-previews on
  hover** before you click
- **Scale text with size** (right-click toggle): the font grows and shrinks with the
  object when resizing
- **Text boxes**: lightweight single-line-by-default text objects with optional or
  transparent backgrounds - same formatting, rotation, resize, and connector support

### Workflow
- **Copy / Cut / Paste** (Ctrl+C/X/V) - pastes land under the cursor and keep the
  connections between copied shapes
- **Duplicate** (Ctrl+D), box-select (Shift+drag), select-all, multi-drag
- **Save / Open** boards as human-readable JSON with an unsaved-changes guard
- **Export** the whole board as **PNG, JPEG, PDF, BMP, or TIFF**
- **Eight themes** (Light, Dark, Slate, Sepia, Midnight, Ocean, Forest, Rose) plus a
  **custom theme**: pick your own panel, canvas, and accent colors and the rest of the
  palette is derived automatically - applied live and remembered between runs
- **Settings panel**: theme, grid visibility, default snap-to-grid, and whether the app
  remembers your last-used shape and color
- **Grouped toolbar** (Insert / Format / Board / Edit) with captions, Office-style
- **Right-click menus everywhere**: objects (edit / duplicate / copy / cut / layering /
  delete), connections (color / reverse / delete), and empty canvas (paste, add any
  shape or a text box at the click point, select all, zoom to fit)

## Controls

| Action | Input |
|---|---|
| Add shape | Double-click canvas, or ＋ Note button |
| Draw shape to size | Alt+drag on empty canvas |
| Edit shape text | Double-click shape |
| Format text | `Aa` dropdown with shapes selected |
| Move shape(s) | Drag |
| Resize shape | Drag the corner resize grip |
| Rotate shape | Drag the handle above a selected shape (Shift = 15° steps) |
| Import image / link | 🖼 / 🔗 toolbar buttons or File menu |
| Connect shapes | Hover a shape, drag any of its 8 dots onto another shape |
| Pan | Drag empty canvas, middle-drag, or Space+drag |
| Add zone | `P` (or ▧ Zone), drag an area; snaps to grid cells |
| Object opacity | Toolbar slider, live for any selected object |
| Insert clipart | 😀 Clipart, Color or Black & white tab |
| Add text box | T Text button, or right-click canvas |
| Zoom | Mouse wheel, Ctrl +/−, Fit, 100% |
| Box select | Shift+drag (Ctrl+drag adds to selection) |
| Toggle select | Ctrl+click |
| Copy / Cut / Paste | Ctrl+C / Ctrl+X / Ctrl+V |
| Duplicate | Ctrl+D |
| Select all | Ctrl+A |
| Nudge | Arrow keys (Shift = 1 px) |
| Delete | `Del` / `Backspace` |
| Cancel / clear | `Esc` |

## File format

Boards are saved as **`.mindmap`** files - plain JSON inside, friendly to diffs and
scripts. The installer registers the extension with its own document icon, so
double-clicking a board opens it in MindMap Canvas. Plain `.json` boards from older
versions still open via File > Open:

```json
{
  "Version": 1,
  "Nodes": [
    { "Id": "…", "X": 100, "Y": 80, "W": 168, "H": 96,
      "Text": "Idea", "Color": "#FFF9B1", "Shape": "Hexagon",
      "FontSize": 14, "TextColor": "#2D333A", "Align": "Center",
      "Bold": false, "Italic": false }
  ],
  "Connections": [
    { "From": "…", "To": "…", "FromAnchor": "Right", "ToAnchor": "Left" }
  ]
}
```

Nodes also carry optional fields for kind (shape / text / image / link / zone),
rotation, opacity, layer order, font, and embedded image data. A ready-made example
lives at [`docs/demo-board.json`](docs/demo-board.json) - open it with **File → Open**
or pass it on the command line.

## Building the installer

The MSI is authored with [WiX v5](https://wixtoolset.org/) (`installer/Product.wxs`):

```bash
dotnet publish -c Release -r win-x64 --self-contained true
dotnet tool install --global wix --version 5.0.2
wix build -arch x64 -d PublishDir=bin/Release/net8.0-windows/win-x64/publish \
    -d AssetsDir=Assets -o dist/MindMapCanvas-Setup.msi installer/Product.wxs
```

The installer registers the `.mindmap` file type and installs Start-menu and desktop
shortcuts; the portable exe is a separate single-file publish
(`-p:PublishSingleFile=true`).
