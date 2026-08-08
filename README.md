# MindMap Canvas

A Windows WPF (C# / XAML) mind-mapping board inspired by Mural: an infinite gridded canvas
with sticky notes, arrow connections, pan/zoom, and save/load.

## Requirements

- Windows 10/11
- .NET 8 SDK (or newer)

## Run

```bash
dotnet run
```

Or open the folder in Visual Studio 2022+ and press F5.

## Features

- **Infinite canvas** with a snap grid, smooth zoom (10%–400%), and direct panning —
  just drag the empty canvas in any direction (middle-drag and Space+drag also work)
- **Shapes**: rectangle, ellipse, diamond, hexagon, parallelogram — pick from the visual
  Shape gallery; double-click the canvas to place one at default size, or Alt+drag to draw
  one at exactly the size you want, then double-click it to type
  (Enter commits, Shift+Enter newline, Esc cancels)
- **Text formatting** (Aa dropdown): font size, bold, italic, left/center/right alignment,
  and text color (8 presets plus the custom picker) — applies to every selected shape
- **Move & resize**: drag shapes (grid snapping optional), resize via the corner
  resize-icon grip
- **Connections**: hover a shape and drag any of its eight connector dots (side midpoints
  and corners) onto another shape to draw an arrow (arrows hug the true outline of
  ellipses and diamonds); click an arrow to select it, `Del` to remove
- **Stable view**: resizing the window keeps the board centered on the same spot
- **Copy / paste**: Ctrl+C / Ctrl+X / Ctrl+V (pastes under the cursor), plus context menu
- **Multi-select**: Shift+drag a box on empty canvas (Ctrl+drag keeps existing selection),
  Ctrl+click to toggle, Ctrl+A for all
- **Colors**: 16-swatch palette plus a full custom color picker (hue strip,
  saturation/value area, hex input); applies to all selected notes and is reused for new ones
- **Duplicate** (Ctrl+D) clones selected notes and the connections between them
- **Save / Open** boards as JSON (Ctrl+S / Ctrl+O), unsaved-changes prompt on close
- **Export PNG** of the whole board
- **Context menu** on notes: edit, duplicate, delete
- **Menu bar** (File / Edit / View / Settings) mirroring all commands
- **Themes**: Light, Dark, Slate, Sepia — pick in Settings → Settings…, applied live and
  remembered between sessions (`%APPDATA%\MindMapCanvas\settings.json`)

## Controls

| Action | Input |
|---|---|
| Add shape | Double-click canvas, or ＋ Note button |
| Draw shape to size | Alt+drag on empty canvas |
| Format text | Aa dropdown with shapes selected |
| Edit shape text | Double-click shape |
| Move shape(s) | Drag |
| Resize shape | Drag bottom-right grip (visible when selected) |
| Box select | Shift+drag on empty canvas |
| Toggle select | Ctrl+click |
| Connect shapes | Hover shape, drag a side dot onto another shape |
| Pan | Drag empty canvas, middle-drag, or Space + drag |
| Copy / Cut / Paste | Ctrl+C / Ctrl+X / Ctrl+V |
| Zoom | Mouse wheel (anchored at cursor), Ctrl +/−, Fit, 100% |
| Nudge | Arrow keys (Shift = 1px steps) |
| Delete | `Del` / `Backspace` |
| Duplicate | Ctrl+D |
| Select all | Ctrl+A |
| Cancel / clear | `Esc` |

## File format

Boards are plain JSON: a list of nodes (`id, x, y, w, h, text, color`) and a list of
directed connections (`from, to` node ids).
