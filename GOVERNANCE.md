# Governance

TickerNet exists because the scheduler it was forked from moved from an open-source license to a commercial one.
This document makes sure TickerNet itself can never take that step, while leaving everyone free to build what they
like on top of it.

## In short: two promises

| Build anything on it | Open source, forever |
| --- | --- |
| Use, change and redistribute TickerNet under **MIT or Apache-2.0**, whichever you choose. Fork it, build closed-source or commercial products on it, and keep your changes private. Nothing in this document adds conditions to that. | Every TickerNet release stays **MIT OR Apache-2.0**: no paid edition, no source-available version, no license keys and no CLA. Omniscient AI has made this pledge irrevocably. |

## Sponsor and steward

[Omniscient AI](https://omniscient.to) (Omniscient AI Private Limited) set up the TickerNet fork, maintains it and
sponsors it. As steward it:

- funds maintainer time and the project's running costs;
- holds the project's name, the `TickerNet` GitHub organization, the docs site and the `TickerNet` package IDs on
  NuGet, on behalf of the project and its users, under the pledge below.

The steward does not own contributors' work. Contributors keep the copyright in what they write and license it to
everyone under the project license (see [CONTRIBUTING.md](CONTRIBUTING.md)).

## Your freedom to build on TickerNet

The project license is MIT OR Apache-2.0, at your option. Under either one you may:

- use TickerNet in any product, open source or proprietary, free or paid;
- fork it, change it and publish your own version under a different name;
- build derivative works and license them on any terms the license you chose allows, including proprietary terms;
- keep your changes to yourself.

What the license asks in return is small: keep the copyright and license notices (and, if you choose Apache-2.0, follow
its notice and change-marking terms), and accept that the software comes without warranty. The pledge below is a
promise TickerNet makes about itself. It adds no conditions to the license, and no future change to this document can
take these freedoms away from code already released.

The only limit outside the license is the name: products built on TickerNet can't be published as "TickerNet".
[TRADEMARKS.md](TRADEMARKS.md) explains the name rules.

## Open-source pledge: TickerNet stays open source forever

Omniscient AI, as steward, and every maintainer of TickerNet, make this pledge to TickerNet's users and contributors.
It is irrevocable.

1. **Every release stays open source.** Every version of TickerNet published from this repository, or under a
   `TickerNet` package ID on NuGet, is licensed under MIT OR Apache-2.0, with complete source code in this
   repository.
2. **No commercial edition under the TickerNet name.** There will be no source-available, "community edition",
   paid-only, license-key, revenue-threshold or usage-limited version of TickerNet. Features will not be held back
   from the open-source release to sell them separately.
3. **No license changes by assignment.** TickerNet does not use a contributor license agreement. Contributions are
   accepted under the project license with a [Developer Certificate of Origin](https://developercertificate.org/)
   sign-off, so no single party collects the rights to relicense everyone's work.
4. **A handover keeps the pledge.** If the steward stops supporting TickerNet, it will transfer the name, the GitHub
   organization, the docs site and the NuGet package IDs to an open-source foundation or to the active maintainers.
   It will not sell or transfer them to anyone who does not take on this pledge.

Paid services around TickerNet, such as support, consulting or hosting, are fine. They never change TickerNet's
license or decide which features TickerNet gets.

## Maintainers

TickerNet has one maintainer today: [jeff-pang](https://github.com/jeff-pang), whose time Omniscient AI funds. One
maintainer is a risk, and we say so plainly. While it lasts, these protect you:

- **The license.** Anyone can fork TickerNet and carry it on, under MIT or Apache-2.0, without asking anyone.
- **The handover promise** in the pledge below: the project goes to a foundation or its active maintainers, never to
  someone who drops the pledge.
- **Checks that don't depend on trusting one person.** Every change goes through a pull request with required build,
  test and sign-off checks and a CodeQL scan. Packages are published only by the release workflow, with signed build
  provenance, and release tags and releases can't be changed afterwards. [SECURITY.md](SECURITY.md) lists them.

We are looking for co-maintainers, and working towards a second person with owner access to the GitHub organization
and the NuGet package IDs, so the project never depends on one account.

### Becoming a maintainer

1. **Contributor.** Anyone who opens an issue, answers a question in Discussions or sends a pull request. Once a
   pull request of yours is merged, your name goes in [CONTRIBUTORS.md](CONTRIBUTORS.md).
2. **Triager.** After a few good contributions, ask in Discussions or on a pull request. A maintainer can give you
   triage access: you can label, assign and close issues and pull requests, but not merge.
3. **Maintainer.** After about three months of steady, good contributions and reviews, the maintainers invite you.
   Maintainers review and merge pull requests and publish releases. A new maintainer accepts the
   [open-source pledge](#open-source-pledge-tickernet-stays-open-source-forever) in their first pull request to this
   file, which adds their name above.

Maintainers who are inactive for six months become emeritus and keep their credit. They can come back by asking.
Once there are two or more maintainers, every pull request needs approval from a maintainer who didn't write it.

## How decisions are made

- Maintainers review and merge pull requests. A change goes in when a maintainer approves it and nobody with a good
  reason objects.
- Decisions that pick between real alternatives, add or remove a package or public interface, or set a rule other
  changes must follow get an architecture decision record on the docs site (`site/decisions/`).
- This document changes only through a pull request with an ADR. Changes may add to the pledge, to the freedoms
  above, or make them clearer. They may never weaken either.
