# Security policy

## Reporting a vulnerability

Please don't report security problems in a public issue. Use GitHub's private vulnerability reporting instead:
open the repository's **Security** tab and choose **Report a vulnerability**
(https://github.com/TickerNet/TickerNet/security/advisories/new).

Include what the problem is, how to reproduce it, and what an attacker could do with it. We'll acknowledge the
report, keep you updated while we work on a fix, and credit you in the advisory unless you'd rather we didn't.

## Supported versions

Security fixes go into the latest 1.x release.

| Version | Supported |
| --- | --- |
| Latest 1.x release | Yes |
| Older 1.x releases | No: update to the latest 1.x |
| 1.0.0 previews | No |

## Verifying a package

nuget.org signs every TickerNet package with its repository signature, and `dotnet restore` checks it. From 1.0.1
on, each package also has a signed build provenance attestation from the release workflow. To check that a package
was built by TickerNet's release workflow from a commit in this repository, download the `.nupkg` (from nuget.org or
the GitHub release) and run:

```bash
gh attestation verify TickerNet.1.0.1.nupkg --repo TickerNet/TickerNet
```
