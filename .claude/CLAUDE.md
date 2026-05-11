# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**TOR XML Editor** (codename: TORTools) — a cross-platform C# desktop application for editing XML data files of "The Old Realms" Mount & Blade II: Bannerlord mod.

- **Stack:** Avalonia UI 11+ on .NET 8+, CommunityToolkit.Mvvm, System.Xml.Linq
- **Platforms:** Windows (primary), Linux (secondary)
- **Target users:** Semi-technical content creators who understand the mod's data model

## Build Commands

```bash
# Build the app (development)
dotnet build src/TORTools.App/TORTools.App.csproj

# Run the app (development - requires .NET SDK)
dotnet run --project src/TORTools.App/TORTools.App.csproj

# Build self-contained release (outputs to release/ folder)
# Use build-release.bat on Windows, or:
dotnet publish src/TORTools.App/TORTools.App.csproj -c Release -r win-x64 --self-contained true -o release

# Run the pre-built release (no SDK needed)
# Double-click TOR_Tools.bat or release/TORTools.App.exe

# Run tests
dotnet test tests/TORTools.Core.Tests
```

## Claude Code Integration

When running the app via `dotnet run`, use the **KillShell** tool to terminate the application when needed. The app runs as a background task and can be stopped using the shell ID provided when launched.

## Solution Structure

```
TOR_Tools/
├── release/                  # Pre-built exe (tracked, for non-programmers)
├── src/
│   ├── TORTools.Core/        # Shared logic (no UI dependency)
│   │   ├── Models/           # Data models for XML entities
│   │   ├── Services/         # XML parsing, validation, cross-ref
│   │   ├── Schema/           # Schema definition loading & registry
│   │   ├── Validation/       # Validation service
│   │   └── Workspace/        # Workspace management, repo discovery
│   └── TORTools.App/         # Avalonia desktop application
│       ├── ViewModels/       # MVVM ViewModels
│       ├── Views/            # AXAML Views
│       └── Converters/       # Value converters
├── schemas/                  # JSON schema definitions per XML file type
├── tests/
│   └── TORTools.Core.Tests/
├── TOR_Tools.bat             # Launcher for pre-built exe
└── build-release.bat         # Builds to release/ folder
```

## Neighboring Repositories

This repo lives inside the Bannerlord Modules directory alongside the TOR mod repos:

- `../TOR_Core/ModuleData/` — Characters, cultures, abilities, effects XML
- `../TOR_Armory/ModuleData/` — Items, weapons, armor XML
- `../TOR_Armory/GUI/` — Sprite definitions, icons
- `../TOR_Armory/XmlGenerator/` — RETIRED CSV→XML tool (schema reference only)
- `../TOR_Environment/ModuleData/` — Settlement XML, scene data

## Planning Documents

Full specifications are in the TORTasks knowledge base at `C:\Users\linus\Documents\TORTasks\`:

| Document | Purpose |
|---|---|
| `TOR_Editor_Requirements.md` | Complete requirements (12 sections, 8 phases) |
| `TOR_Editor_XmlGenerator_Analysis.md` | Schema knowledge from retired XmlGenerator |
| `TOR_Editor_Architecture.mermaid` | Layered architecture diagram |
| `TOR_Editor_DataFlow.mermaid` | Data flow diagrams |
| `settlement_editor.html` | Settlement editor prototype for Phase 6 |

**Read these before making architectural decisions.**

## Critical Rules

1. **XML formatting preservation:** When saving, preserve original indentation, attribute order, comments, and whitespace. Diffs must show only actual value changes.

2. **Schemas are source of truth:** All validation rules, enum values, and field definitions come from JSON schema files in `schemas/`. Never hardcode schema knowledge in C# code.

3. **MCP server uses stdio transport:** Compatible with Claude Code, Claude Desktop, and Cowork.

4. **Cross-platform:** Must build and run on both Windows and Linux. Use .NET 8+ cross-platform APIs only.

5. **tor_strings.xml:** MCP-only interface, no direct table editing. Indexed in-memory for efficient queries.

6. **tor_skins.xml:** Excluded for now (6.6MB, needs rework).

## XML Schema Patterns

These patterns from the XmlGenerator apply across all file types:

- **Null/empty handling:** Values of `"-"`, `"none"`, `""`, null, whitespace are all absent. Omit attribute when writing.
- **Percentage conversion:** Display values (0-100) → XML values (0.00-1.00)
- **Localization keys:** `{=str_[id]}[display_text]` — unwrap for display, re-wrap on save
- **Vector3D format:** Comma-separated `"x,y,z"` with default `"0"` for null components
- **Colon-delimited multi-values:** Abilities, Attributes, ItemTraits use `:` delimiter
- **Type-dependent structure:** Shields, ranged weapons, characters have different nested structures based on Type attribute

## Development Phases

| Phase | Goal |
|---|---|
| 1. Foundation (MVP) | Workspace, file tree, DataGrid, inline editing, undo/redo, save |
| 2. Items & Validation | JSON schemas, enum dropdowns, validation panel |
| 3. Cross-References | Clickable IDs, "find references", broken ref highlighting |
| 4. Characters & Abilities | Nested elements, equipment set editor |
| 5. MCP Server | stdio transport, CRUD tools, validation, strings interface |
| 6. Settlement Editor | Map view, batch scene assignment, port HTML prototype |
| 7. 3D Preview | Silk.NET viewport, FBX loading, attachment sliders |
| 8. Polish | Git indicators, batch ops, CSV import/export, themes |
