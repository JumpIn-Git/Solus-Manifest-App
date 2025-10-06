# GitHub Actions Workflows

This directory contains automated workflows for building and releasing Solus Manifest App.

## Workflows

### 1. Build (`build.yml`)
- **Triggers:** Push or Pull Request to main/master branch
- **Purpose:** Continuous Integration - validates that code builds successfully
- **Output:** Build artifacts uploaded to GitHub (retained for 7 days)

### 2. Release (`release.yml`)
- **Triggers:**
  - Push a tag matching `v*.*.*` (e.g., `v1.0.0`, `v1.2.3`)
  - Manual trigger via GitHub Actions UI
- **Purpose:** Creates official releases with packaged executables
- **Output:**
  - GitHub Release created automatically
  - Windows x64 zip file attached to release

## Creating a Release

### Method 1: Using Git Tags (Recommended)

```bash
# Navigate to your repository
cd "C:\Morrenus Stuff\Solus Manifest App"

# Create and push a version tag
git tag v1.0.0
git push origin v1.0.0
```

The workflow will automatically:
1. Build the application
2. Package it as a zip file
3. Create a GitHub release
4. Attach the zip to the release

### Method 2: Manual Trigger

1. Go to your repository on GitHub
2. Click "Actions" tab
3. Select "Build and Release" workflow
4. Click "Run workflow"
5. Choose the branch and click "Run workflow"

## Version Naming

Follow semantic versioning:
- `v1.0.0` - Major release
- `v1.1.0` - Minor release (new features)
- `v1.0.1` - Patch release (bug fixes)

## Build Artifacts

The release workflow produces:
- **SolusManifestApp-v{version}-win-x64.zip** - Windows 64-bit standalone executable
  - Self-contained (includes .NET runtime)
  - Single-file deployment
  - No installation required

## Requirements

- Repository must be on GitHub
- Windows runners are used for building
- .NET 8 SDK is automatically installed
