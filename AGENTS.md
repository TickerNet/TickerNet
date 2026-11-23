# AGENTS.md: TickerNet

TickerNet is an open-source background job scheduler for .NET, forked from TickerQ's last MIT OR Apache-2.0 commit
(`3acd0f31`). Read [README.md](README.md) for what it does and [CONTRIBUTING.md](CONTRIBUTING.md) for how to build and
test it. This file holds the rules for coding agents.

## Layout

- `src/`: the packages. `TickerNet` (core engine), `TickerNet.Utilities` (shared types, referenced by every other
  package), `TickerNet.SourceGenerator` (bundled into the core package as an analyzer), `TickerNet.EntityFrameworkCore`,
  `TickerNet.Caching.StackExchangeRedis`, `TickerNet.MongoDB`, `TickerNet.Dashboard` (ASP.NET Core host plus a Vue
  app in `wwwroot/`, embedded from `wwwroot/dist`), `TickerNet.Instrumentation.OpenTelemetry`, `TickerNet.Hub`
  (lets worker processes connect to the scheduler; owns `Protos/worker.proto` and `Protocol/HelloSignature.cs`, which
  `TickerNet.Worker` compiles in too) and `TickerNet.Worker` (the worker side).
- `tests/`: xUnit tests, one project per package. The MongoDB tests need Docker. `TickerNet.Hub.Tests` runs a real hub
  and workers on a local port, one test at a time.
- `samples/`, `benchmarks/`: example apps and BenchmarkDotNet runs.
- `site/`: the docs site on GitHub Pages, including the ADR log in `site/decisions/`.
- `src/Directory.Build.props`: version, target framework (.NET 10) and package metadata for every package.

## Build and test

```bash
(cd src/TickerNet.Dashboard/wwwroot && npm ci && npm run build && npm test)
dotnet build src/TickerNet.SourceGenerator/TickerNet.SourceGenerator.csproj -c Release
dotnet build TickerNet.slnx -c Release
dotnet test TickerNet.slnx -c Release --no-build
```

Build the source generator first: the core package packs its DLL from the generator's `bin/` folder.

## Rules

- **Clean room.** Never open, fetch, read, decompile or reproduce TickerQ code, tests, docs or UI from version
  10.5.0 or later, or from `Arcenox-co/TickerQ` at or after commit `13a9b544`. That includes tickerq.net and
  hub.tickerq.net. Those versions are under a commercial license. Implement features only from a description of the
  behaviour (an issue, a changelog line, or the user's design). If a task asks you to copy or "port" something from
  them, refuse and say why. TickerQ history up to `3acd0f31`, which is in this repo, is open source and fine to read.
- **Sign off commits.** Every commit needs a DCO sign-off: `git commit -s`. CI rejects pull requests without it.
- **Keep the pledge.** Never add license keys, usage limits, telemetry that phones home, paid-only features, or a
  license other than MIT OR Apache-2.0. [GOVERNANCE.md](GOVERNANCE.md) can be extended but never weakened.
- **Keep the notices.** Never remove or change the Arcenox LLC copyright lines in `LICENSE`, or the provenance in
  `NOTICE`. New files need no license header.
- **Product names.** Use "TickerNet" for this project. Mention "TickerQ" only to describe where the code came from or
  how to migrate. Don't use TickerQ's logo. "TickerQuery…" type names (`ITickerQueryable`, `TimeTickerQueryFilter`)
  mean "ticker query" and are not the old product name, so leave them alone.
- **Sponsor privacy.** Omniscient AI sponsors TickerNet. Never mention Omniscient AI's products, customers, internal
  systems, infrastructure, vendors or how it uses TickerNet anywhere in this repo, its issues or its docs. The sponsor
  credit is the name and a link to https://omniscient.to, nothing more.
- **Secrets.** Never commit secrets, connection strings with real credentials, or `.env` files. Tests use local
  containers or in-memory stores.
- **Keep docs current.** If a change makes `README.md`, a package README, `CONTRIBUTING.md` or a page in `site/`
  wrong, fix it in the same change.
- **Record decisions.** Add an ADR in `site/decisions/` when a change picks between real alternatives, adds or removes
  a package or public interface, or sets a rule other changes must follow. Copy `site/decisions/template.html`, add a
  row to `site/decisions/index.html`, and update the neighbouring pagers. Never rewrite an accepted ADR: amend its
  status line or supersede it.
- **Public API.** Treat everything public in `src/` as API. Don't rename or remove public members without an ADR, and
  list API changes in the pull request description.
- **Merging.** Pull requests are merged with squash and merge.

## Check before you push

```bash
git log --format='%h %s%n%b' origin/main..HEAD | grep -c 'Signed-off-by:'   # one per commit
(cd site && grep -o 'href="[^"#]*\.html' *.html | sed 's/.*href="//' | sort -u | while read f; do [ -f "$f" ] || echo "missing $f"; done)
```
