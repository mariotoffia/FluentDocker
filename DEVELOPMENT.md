# Development Guide

## Testing GitHub Actions Locally

You can test the GitHub Actions workflow locally using [nektos/act](https://github.com/nektos/act).

### Installation

```bash
# Install act with Homebrew
brew install act
```

### Usage

To run the full workflow locally:

```bash
act -j build
```

### Notes for Apple Silicon (M1/M2/M3) Macs

If you're using an Apple Silicon Mac, you might need to specify the container architecture:

```bash
act -j build --container-architecture linux/amd64
```

### Limitations

When running with `act`:

1. Container-based tests are skipped (they don't work reliably in a Docker-in-Docker setup)
2. SonarCloud scanning is skipped
3. NuGet package publishing won't actually publish (but will run the commands)

For the full test suite including container tests, it's recommended to run:

```bash
dotnet test
```

directly on your development machine with Docker installed.
