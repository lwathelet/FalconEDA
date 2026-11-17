# FalconEDA

An open-source schematic and PCB editor built with .NET and Avalonia.

Status: early scaffolding.

## Layout

| Project | Purpose |
|---|---|
| `src/FalconEDA.Core` | Data model and geometry |
| `src/FalconEDA.IO` | File reading and writing |
| `src/FalconEDA.Commands` | Command-line parser and command set |
| `src/FalconEDA.App` | Avalonia desktop application |
| `tests/FalconEDA.Tests` | Unit tests |

## Build

```
dotnet build
dotnet test
dotnet run --project src/FalconEDA.App
```
