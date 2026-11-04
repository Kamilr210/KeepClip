---
name: keepclip-quality
description: Run and diagnose the repository-local KeepClip quality checks after code changes or before a merge. Use for this KeepClip Windows/.NET repository, including build, tests, analyzers and coverage; not for release publishing or unrelated repositories.
---

# KeepClip quality checks

Read the repository [AGENTS.md](../../../AGENTS.md) and the relevant sections
of [the QA guide](../../../docs/quality-assurance.md). Run commands from the
repository root, where `KeepClip.slnx` and `scripts/quality-check.ps1` exist.

Use `-Mode Quick` while iterating and `-Mode Full` before finishing code or QA
changes. Use the guide to select optional FFmpeg or desktop tests when the change
touches those integrations and their prerequisites are available. Do not install
or download native tools automatically.

Inspect the newest run's summary, failing step log and TRX before changing code.
Distinguish a product regression from an unavailable SDK, package feed, locked
file or native dependency. Correct the smallest demonstrated cause, rerun its
test and then the full suite. Follow the three-attempt limit in `AGENTS.md` when
progress stops. A filtered, skipped or offline run does not replace full QA.

Report the commands actually executed, their exit codes, report location,
remaining failures and checks that could not run. Coverage describes exercised
code; missing coverage in native/device paths is not a reason to exclude those
classes from the denominator. The runner verifies code and never publishes a
release or sends data to a user's cloud account.
