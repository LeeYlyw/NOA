# NOA repository guidance

- At the start of a new task, read `PROJECT_STATUS.md`, then inspect `git status --short`, the current branch, and relevant code. The status file is a handoff, not proof that a feature works.
- Keep implementation, automated checks, Unity visual checks, and real two-PC results separate in reports. Do not repeat completed work solely because a new chat started; rerun a check when related code changed or a concrete failure appears.
- The current demo target is one complete two-player stage for October 28. Four role/character variants do not mean four concurrent connections. The user excluded the doppelganger from this demo and has not approved an extra escape stage.
- Do not edit animation clips, motions, Animator Controllers, models, or prefabs without following the user's current instructions. Record requested motion connections in `Server/PresentationAnimationNotes.md`.
- Preserve teammates' and local changes. Make reversible commits by work unit. Update `PROJECT_STATUS.md` when scope, verified results, or remaining work changes.
