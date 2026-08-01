---
name: unity-cs-compile-check
description: "This skill should be used after writing or modifying C# files in a Unity project. It identifies the Assembly Definition (.asmdef) that the file belongs to, maps it to the corresponding .csproj file, and runs dotnet build with -nologo to verify the code compiles without errors. Trigger when C# code is written or edited in a Unity project context, compile verification is needed, or the user asks to check if their C# code builds."
---

# Unity C# Compile Check

## Overview

Verify that C# code changes compile successfully within a Unity project by identifying the correct assembly and running `dotnet build -nologo` against the corresponding `.csproj` file.

## When to Use

- After writing or editing any `.cs` file in a Unity project
- When the user requests a compile/build check
- Before committing C# changes to verify correctness
- After refactoring C# code across files

## Workflow

### Step 1: Identify the Unity Project Root

Locate the project root by finding the directory containing `Assets/`, `Packages/`, and `ProjectSettings/` folders. The `.csproj` files live at this level.

### Step 2: Find the Assembly for the C# File

Walk up from the `.cs` file's directory toward the project root, looking for the nearest `.asmdef` file:

```
Assets/
  MyGame/
    Core/
      MyGame.Core.asmdef    <-- This assembly owns...
      Systems/
        PlayerSystem.cs     <-- ...this file
      Utils/
        Helper.cs           <-- ...and this file
    Editor/
      MyGame.Editor.asmdef  <-- Different assembly for editor code
```

**If no `.asmdef` is found**, the file belongs to the default Unity assembly:
- `Assets/` scripts -> `Assembly-CSharp`
- `Assets/Editor/` scripts -> `Assembly-CSharp-Editor`

For detailed mapping rules, refer to `references/asmdef_csproj_mapping.md`.

### Step 3: Map to .csproj

Extract the `"name"` field from the `.asmdef` JSON. The matching `.csproj` is at the project root with that exact name:

```
.asmdef "name": "MyGame.Core" -> {ProjectRoot}/MyGame.Core.csproj
```

### Step 4: Run Compile Check

Execute the build using the bundled script or directly:

**Using the script:**
```bash
bash scripts/compile_check.sh <path-to-cs-file> <project-root>
```

**Direct command:**
```bash
dotnet build <path-to-csproj> -nologo
```

### Step 5: Interpret Results

- **BUILD SUCCEEDED**: Code compiles. Report success.
- **BUILD FAILED**: Parse error output for file paths, line numbers, and error codes. Report the specific errors with actionable fixes.

## Common Error Patterns

| Error Code | Meaning | Action |
|------------|---------|--------|
| CS0246 | Type or namespace not found | Check `.asmdef` references, add missing assembly reference |
| CS0103 | Name does not exist in current context | Check using directives, verify namespace |
| CS0119 | Cannot use type as expression | Usually wrong generic usage or missing parentheses |
| CS1061 | Type does not contain definition | Check API version, wrong method name |
| CS0234 | Namespace does not contain type | Missing package or assembly reference |

## Important Notes

- Unity must have generated the `.csproj` files at least once (open project in Unity Editor first)
- If `.csproj` files are missing, instruct the user to open the project in Unity and enable "Preferences > External Tools > Generate .csproj files"
- The `dotnet build` approach validates syntax and type resolution but does NOT execute Unity-specific compilation (e.g., no ScriptableObject validation)
- On Windows, use forward slashes or properly escaped backslashes in paths
