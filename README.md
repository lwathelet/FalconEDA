# FalconEDA

An open-source schematic and PCB editor built with .NET and Avalonia.

Status: early scaffolding.

## Layout

| Project | Purpose |
|---|---|
| `src/FalconCAD.Core` | Data model and geometry |
| `src/FalconCAD.IO` | File reading and writing |
| `src/FalconCAD.Commands` | Command-line parser and command set |
| `src/FalconCAD.App` | Avalonia desktop application |
| `tests/FalconCAD.Tests` | Unit tests |

## Build

```
dotnet build
dotnet test
dotnet run --project src/FalconEDA.App
```
