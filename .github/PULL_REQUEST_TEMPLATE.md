## What this changes

<!-- One or two sentences. Link the issue: "Fixes #12". -->

## How it was tested

- [ ] `dotnet run --project tests` passes
- [ ] Loaded in the game through Pulsar from a local folder
- [ ] World load, back to menu, load again: no plugin errors in the log

## Checklist

- [ ] The change is about cargo management (one plugin, one job)
- [ ] New decisions are in `ClientPlugin/Policy/` and have a test
- [ ] Items are moved only through `TransferService`
- [ ] No network or file access was added
- [ ] Version number and `LwCargoManager.xml` are unchanged
- [ ] `Docs/UserManual.md` is updated if players see the change
