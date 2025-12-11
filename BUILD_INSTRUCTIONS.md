# Build Instructions for PlayerActivities Plugin

## Prerequisites
- .NET Framework 4.6.2 SDK or higher
- MSBuild (comes with Visual Studio or .NET Framework Developer Pack)
- Visual Studio 2017 or later (optional, for IDE experience)

## Build Methods

### Method 1: Using MSBuild from Command Line

**From the repository root:**
```cmd
msbuild source\PlayerActivities.sln /p:Configuration=Release
```

Or target the project file directly:
```cmd
msbuild source\PlayerActivities.csproj /p:Configuration=Release
```

**From the source directory:**
```cmd
cd source
msbuild PlayerActivities.sln /p:Configuration=Release
```

Or:
```cmd
cd source
msbuild PlayerActivities.csproj /p:Configuration=Release
```

### Method 2: Using Visual Studio

1. Open `source\PlayerActivities.sln` in Visual Studio
2. Select the desired configuration (Debug, Release, or Debug-Release)
3. Build > Build Solution (or press Ctrl+Shift+B)

### Method 3: Using dotnet CLI (if available)

**From the repository root:**
```cmd
dotnet build source\PlayerActivities.csproj -c Release
```

**From the source directory:**
```cmd
cd source
dotnet build PlayerActivities.csproj -c Release
```

## Build Configurations

- **Debug**: Development build with debug symbols
- **Release**: Production build (optimized)
- **Debug-Release**: Special configuration for Playnite Toolbox packaging

## Output Location

Built files will be in:
- Debug builds: `source\bin\Debug\`
- Release builds: `source\bin\Release\`
- Debug-Release builds: `source\bin\Debug-Release\`

## Common Issues

### Error: MSB1003 - Project or solution file not found

**Problem:** MSBuild cannot find the project/solution file in the current directory.

**Solutions:**
1. Make sure you're running MSBuild from the correct directory (either repo root or `source` directory)
2. Specify the full path to the .sln or .csproj file:
   ```cmd
   msbuild "C:\path\to\repo\source\PlayerActivities.sln" /p:Configuration=Release
   ```
3. Change to the source directory first:
   ```cmd
   cd source
   msbuild PlayerActivities.sln /p:Configuration=Release
   ```

### Error: Reference assemblies for .NETFramework,Version=v4.6.2 not found

**Problem:** .NET Framework 4.6.2 SDK is not installed.

**Solution:** Download and install the [.NET Framework 4.6.2 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net462)

### Missing Submodules

If you see errors about missing dependencies in `playnite-plugincommon`:

```cmd
git submodule update --init --recursive
```

## Post-Build

The build script (`build\build.ps1`) automatically runs after build if you're using Visual Studio or MSBuild directly. This script:
- Packages the plugin using Playnite Toolbox (if available)
- Creates a compressed archive for distribution
- Verifies the installer manifest (for Release builds)

## Quick Start

The fastest way to build:

```cmd
cd source
msbuild PlayerActivities.sln /p:Configuration=Release
```

Or if you have Visual Studio installed, just open `source\PlayerActivities.sln` and press F6 to build.
