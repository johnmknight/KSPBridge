# CKAN submission

This directory holds metadata for submission to
[CKAN](https://github.com/KSP-CKAN/CKAN), the Comprehensive Kerbal
Archive Network — KSP's de-facto package manager.

## What's here

- `KSPBridge.netkan` — the **netkan** indexer file. It tells the CKAN
  bot how to discover new releases (via GitHub) and how to install
  them (look for the `KSPBridge/` folder in the release zip and copy
  it under `GameData/`).

## Submitting

CKAN listings live in the
[`KSP-CKAN/CKAN-meta`](https://github.com/KSP-CKAN/CKAN-meta) repo,
not here. Submission is a one-time PR:

1. Fork [`KSP-CKAN/CKAN-meta`](https://github.com/KSP-CKAN/CKAN-meta).
2. Copy `.ckan/KSPBridge.netkan` from this repo into the
   `NetKAN/N/KSPBridge/` directory of the fork (filename stays
   `KSPBridge.netkan`).
3. Open a PR. The CKAN maintainers run automated checks; if the
   netkan validates against the latest GitHub release and the
   `KSPBridge.version` file is parseable, they merge it.
4. Once merged, the CKAN inflater bot picks up new GitHub releases
   automatically — no further action needed per release as long as
   `make-release.ps1` keeps regenerating `KSPBridge.version` correctly.

## Maintenance

The `$kref` (`#/ckan/github/johnmknight/KSPBridge`) tells CKAN to watch
the GitHub releases for new tags. The `$vref` (`#/ckan/ksp-avc`) tells
it to read `GameData/KSPBridge/KSPBridge.version` from the release zip
to determine version + KSP compatibility — that's the single source of
truth, so as long as `make-release.ps1` regenerates it from csproj on
every release, the netkan needs no further hand-editing for routine
version bumps.

If KSP compatibility changes (a future KSP 1.13 etc.), update the
`KSP_VERSION_MIN` / `KSP_VERSION_MAX` block in `make-release.ps1`
and the netkan's `ksp_version_min` / `ksp_version_max` fields, then
PR the netkan changes back to `CKAN-meta`.
