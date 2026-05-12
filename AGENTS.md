# Agent Instructions

## Coding Standards

- Follow the repository `.editorconfig`; it is the source of truth for C# formatting, naming, and code style.
- Private fields must use `_camelCase`.
- Keep public, internal, protected, and private-protected members in PascalCase.
- Prefer small, scoped changes that match the existing project structure.
- Do not silence analyzers unless there is a specific, documented reason.

## Workflow Hygiene

- Before making changes, check whether the worktree is on a named branch. If the repository is detached or otherwise not on a branch, recommend creating a branch first.
- Keep each change focused on one feature, fix, or maintenance task. Do not bundle unrelated refactors or behavior changes into the same work item.
- Update `CHANGELOG.md` as user-facing changes are made. Keep changelog entries scoped to the same feature or fix as the code changes.
- Use Conventional Commits for commit messages and PR titles, such as `feat: add batch export` or `fix: preserve material paths`.

## Required Checks

Run these before handing work back:

```powershell
dotnet format GMConverter.slnx --verify-no-changes --severity warn --no-restore
dotnet build GMConverter.slnx --configuration Release --no-restore
```

If either command fails, fix the reported issue or clearly report the remaining blocker.

## Project Notes

- `GMConverter` is the core conversion library.
- `GMConverter.CLI` is the command-line frontend.
- `GMConverter.UI` is the Avalonia frontend.
- Avoid changing generated build output, IDE files, or unrelated user edits.
