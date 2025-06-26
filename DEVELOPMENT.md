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

### Using Secrets with act

The workflow is designed to work with both GitHub secrets and local `.env` files:

1. **For local testing with act**:
   - Secrets come from your local `.env` file
   - The same variable names are used as in GitHub
   
2. **For GitHub runs**:
   - Secrets come from the GitHub repository secrets

Here's how to set up your local environment:

```bash
# First, create a .env file based on .env.example
cp .env.example .env

# Edit the .env file with your secret values
vim .env  # or use any editor

# Run act with the .env file
act -j build --env-file .env
```

The `.env` file supports the following variables:

- `SONAR_TOKEN`: For SonarCloud analysis
- `NUGET_API_KEY`: For publishing NuGet packages
- `GITHUB_TOKEN`: For GitHub API access
- `ACT_BRANCH`: To simulate specific branch (optional)
- `ACT_EVENT`: To simulate specific event type (optional)

When using `--env-file .env`, these variables will be available to the workflow just like GitHub secrets.

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

## NuGet Packaging

The project uses modern NuGet packaging with the following features:

1. Package icon: The icon is stored in the `icon/fluent-docker.png` file and included in the packages.
2. README files: Each project includes its README.md in the package.
3. Source Link: Source code is linked to GitHub repositories for debugging.
4. Versioning: Uses GitVersion for automatic versioning.
