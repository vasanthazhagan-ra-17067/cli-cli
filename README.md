# cli-cli — GitHub CLI Fork

This is a fork of [GitHub's official CLI tool (`gh`)](https://github.com/cli/cli). `gh` is GitHub on the command line. It brings pull requests, issues, and other GitHub concepts to the terminal next to where you are already working with `git` and your code.

## What does this project do?

GitHub CLI (`gh`) is the command-line interface for GitHub. It allows developers to interact with GitHub directly from their terminal, without needing to open a web browser. With `gh`, you can:

- **Manage pull requests** – create, review, merge, and list pull requests
- **Work with issues** – create, close, comment on, and list issues
- **Manage repositories** – clone, fork, create, and view repositories
- **Run GitHub Actions workflows** – trigger, list, and view workflow runs
- **Manage releases** – create and list releases
- **Interact with GitHub APIs** – make authenticated API calls to GitHub's REST and GraphQL APIs
- **Use GitHub Codespaces** – create, list, and connect to codespaces
- **Authenticate securely** – log in and manage credentials for GitHub.com and GitHub Enterprise

GitHub CLI is supported for users on GitHub.com, GitHub Enterprise Cloud, and GitHub Enterprise Server 2.20+, with support for macOS, Windows, and Linux.

## Documentation

For usage instructions, see the [manual](https://cli.github.com/manual/).

## Installation

### macOS

```sh
brew install gh
```

### Linux & Unix (Debian/Ubuntu)

```sh
sudo apt install gh
```

### Windows

```sh
winget install --id GitHub.cli
```

For all installation options, see the [installation documentation](docs/install_linux.md).

## Contributing

If anything feels off or if you feel that some functionality is missing, please check out the [contributing page](.github/CONTRIBUTING.md). There you will find instructions for sharing your feedback, building the tool locally, and submitting pull requests to the project.

## Comparison with hub

For many years, [hub](https://github.com/github/hub) was the unofficial GitHub CLI tool. The upstream `gh` project is a newer tool that explores what an official GitHub CLI tool can look like with a fundamentally different design. While both tools bring GitHub to the terminal, `hub` behaves as a proxy to `git`, and `gh` is a standalone tool.