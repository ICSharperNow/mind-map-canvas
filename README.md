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

- **Infinite canvas** with a snap grid, smooth zoom (10%–400%) and panning
- **Sticky notes**: double-click the canvas to create, double-click a note to edit
  (Enter commits, Shift+Enter inserts a newline, Esc cancels)
- **Move & resize**: drag notes (grid snapping optional), resize via the corner grip
- **Connections**: hover a note and drag one of the four side connector dots onto another
  note to draw an arrow; click an arrow to select it, `Del` to remove
- **Multi-select**: drag a box on empty canvas, Ctrl+click to toggle, Ctrl+A for all
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
| Add note | Double-click canvas, or ＋ Note button |
| Edit note | Double-click note |
| Move note(s) | Drag |
| Resize note | Drag bottom-right grip (visible when selected) |
| Box select | Drag on empty canvas |
| Toggle select | Ctrl+click |
| Connect notes | Hover note, drag a side dot onto another note |
| Pan | Middle-drag, or hold Space + drag |
| Zoom | Mouse wheel (anchored at cursor), Ctrl +/−, Fit, 100% |
| Nudge | Arrow keys (Shift = 1px steps) |
| Delete | `Del` / `Backspace` |
| Duplicate | Ctrl+D |
| Select all | Ctrl+A |
| Cancel / clear | `Esc` |

## File format

Boards are plain JSON: a list of nodes (`id, x, y, w, h, text, color`) and a list of
directed connections (`from, to` node ids).
