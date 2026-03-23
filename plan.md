# Rename: SupabaseProxy → FlatPlanet.Platform

## New naming

- Solution: `FlatPlanet.Platform.slnx`
- Projects: `FlatPlanet.Platform.API`, `FlatPlanet.Platform.Application`, `FlatPlanet.Platform.Domain`, `FlatPlanet.Platform.Infrastructure`, `FlatPlanet.Platform.Tests`
- Namespaces: `FlatPlanet.Platform.API.Controllers`, `FlatPlanet.Platform.Application.Services`, etc.
- OpenAPI title: "FlatPlanet Platform API"

## Steps

1. **Rename 5 project folders** — `SupabaseProxy.*` → `FlatPlanet.Platform.*`
2. **Rename .csproj files** inside each folder
3. **Rename solution file** — `SupabaseProxy.slnx` → `FlatPlanet.Platform.slnx`
4. **Update .slnx contents** — fix project paths
5. **Update all .csproj `<ProjectReference>` paths**
6. **Find-and-replace `SupabaseProxy` → `FlatPlanet.Platform`** in all C# files (namespaces + usings) — ~120 files
7. **Update Program.cs** — OpenAPI title/description
8. **Rename .http file** and update variable names
9. **Update README.md and CHANGELOG.md**
10. **Clean build artifacts** (`bin/`, `obj/`, `.vs/`) and rebuild
11. **Verify** — `dotnet build` passes
