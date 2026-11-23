# Contributing to TickerNet

Thanks for helping. This page covers the two rules every contribution must follow, then how to build, test and
open a pull request. The [docs site](https://tickernet.github.io/TickerNet/) has a longer contributor guide that
walks through the code.

## Rule 1: sign off every commit (DCO)

TickerNet has no contributor license agreement. You keep the copyright in your work and license it to everyone
under the project license, MIT OR Apache-2.0. To confirm you have the right to do that, sign off each commit under
the [Developer Certificate of Origin 1.1](https://developercertificate.org/):

```bash
git commit -s -m "Fix cron parsing for leap years"
```

This adds a `Signed-off-by: Your Name <you@example.com>` line. The name and email must match the commit author. A
check on every pull request fails if a commit is missing its sign-off. To fix one, run
`git commit --amend -s --no-edit` (last commit) or `git rebase --signoff main` (all commits on your branch), then
force-push.

## Rule 2: clean room for TickerQ's commercial versions

TickerNet was forked from the last open-source TickerQ commit (`3acd0f31`, see [NOTICE](NOTICE)). Later TickerQ
versions are under a commercial license. Their source is public on GitHub, but it is not open source, and code
derived from it cannot be part of TickerNet.

- **Never copy, adapt or translate** code, tests, documentation, examples, UI or other material from TickerQ 10.5.0
  or later, or from TickerQ's main branch at or after commit `13a9b544` (19 October 2026). The same applies to the
  matching 8.5.0, 9.5.0 and 11.5.0 builds and every later version.
- **Don't read that code to implement a feature here.** Build features from a description of what they do: an issue,
  a public changelog entry, or your own design. Don't decompile TickerQ's commercial packages.
- **Write your own words.** Don't copy text from TickerQ's documentation site, even when describing the same feature.
- **Tell us if you can't stay clean.** If you have worked with TickerQ's commercial source, say so in your pull
  request and leave features you saw there to someone else.
- **Coding agents follow the same rule.** If you use an AI coding assistant, don't point it at TickerQ's commercial
  source or ask it to reproduce it. [AGENTS.md](AGENTS.md) gives agents this rule.

TickerQ code from before commit `13a9b544` is MIT OR Apache-2.0 and is fine to use.

## Build and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js](https://nodejs.org/) (current LTS) for
the dashboard, and Docker for the MongoDB tests (they start a container with Testcontainers).

```bash
# Dashboard frontend (embedded into TickerNet.Dashboard)
(cd src/TickerNet.Dashboard/wwwroot && npm ci && npm run build && npm test)

# The core package bundles the source generator, so build it first
dotnet build src/TickerNet.SourceGenerator/TickerNet.SourceGenerator.csproj -c Release
dotnet build TickerNet.slnx -c Release
dotnet test TickerNet.slnx -c Release --no-build
```

## Pull requests

1. Open an issue first for anything bigger than a small fix, so we can agree on the approach.
2. Branch from `main`, keep the pull request to one change, and add tests for new behaviour.
3. Follow the existing code style. Keep public API changes small and mention them in the description.
4. Update the docs in the same pull request: `README.md`, the package READMEs and `site/` (the docs site).
5. If your change picks between real alternatives, adds or removes a package or public interface, or sets a rule
   other changes must follow, add an architecture decision record under `site/decisions/`.

Pull requests are merged with squash and merge.

## Reporting issues

Use [GitHub Issues](https://github.com/TickerNet/TickerNet/issues) for bugs and feature requests. Include the
TickerNet and .NET versions, steps to reproduce, and what you expected to happen. Report security problems
privately, as [SECURITY.md](SECURITY.md) describes.

## Project rules

[GOVERNANCE.md](GOVERNANCE.md) explains who looks after TickerNet and the pledge that keeps it open source.
[TRADEMARKS.md](TRADEMARKS.md) explains how the name may be used.
