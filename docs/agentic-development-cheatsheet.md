# Agentic Development Cheat Sheet (Beginner-Friendly)

This repo has strict agent rules in `AGENTS.md`. This cheat sheet shows how to
work with an AI coding agent safely in this codebase, with realistic chat-style
examples that clearly label HUMAN vs AGENT messages.

## 1) Non-Negotiables (The Agent Must Do These)

- Read `AGENTS.md` and `PLAN.md` first.
- Find the single **ACTIVE** iteration in `PLAN.md`.
- Only implement work explicitly listed in that ACTIVE iteration.
- Before implementation: check the current git branch and compare it to the
  expected branch for the ACTIVE iteration.
- If there is ambiguity (multiple approaches, unclear edge cases, protocol
  changes, etc.): stop and ask up to 5 clarifying questions.

Important workflow detail:
- The agent can propose exact `git` commands, but per `AGENTS.md` it must not
  switch branches / create branches unless the HUMAN explicitly instructs it to.

## 2) Starting Work: Create/Move to the Iteration Branch

This is what "starting an iteration" usually looks like in practice.

Example chat:

HUMAN:
"Implement the ACTIVE iteration in PLAN.md."

AGENT:
"Before implementing, I need to check the current branch and the expected
branch for the ACTIVE iteration. If they differ, I will propose the exact git
command(s), but I will not switch branches unless you tell me to."

AGENT (after checking):
"Current branch is `main`. Expected branch for the ACTIVE iteration is
`<fill-in-expected-branch-name>`. Please run one of:
- `git switch -c <fill-in-expected-branch-name>` (create new)
- `git switch <fill-in-expected-branch-name>` (if it already exists)
Then tell me to continue."

If you want the AGENT to create/switch the branch itself, be explicit:

HUMAN:
"Create and switch to branch `<fill-in-expected-branch-name>` now."

Note:
- If your team has a standard naming convention, put it in `PLAN.md` so the
  expected branch name is unambiguous.

## 3) The Most Common Task: Minor Bug Fix (With Regression Test)

Definition (from `AGENTS.md`):
- Localized fix.
- No protocol/architecture change.
- Must include a regression test.

Example chat:

HUMAN:
"Bug: server accepts InitConfig without `model`. Please fix."

AGENT:
"I will first verify this bug fix is listed in the ACTIVE iteration in
`PLAN.md`. If not, I have to stop. If yes, I will:
1) add a regression test that fails today,
2) make the smallest fix to pass it,
3) run the relevant tests."

Example "good" output expectations (concrete):
- Invalid InitConfig -> error JSON -> connection closes deterministically.

## 4) Significant Bug (Stop and Plan)

Significant bug definition (from `AGENTS.md`):
- Impacts protocol behavior, streaming semantics, buffering, or error handling,
  or involves trade-offs.

Example chat:

HUMAN:
"Sometimes transcript messages arrive out of order. Fix it."

AGENT:
"This likely touches streaming semantics / buffering and could require design
trade-offs, so it is a significant bug. I will not implement yet. Options:
1) Add a new iteration focused on ordering guarantees and backpressure policy.
2) Update the ACTIVE iteration to include this work explicitly.
Which do you prefer?"

## 5) New Feature (Always Requires a Plan Change)

New feature definition (from `AGENTS.md`):
- Any new capability, option/config, field, or client-observable behavior.

Example chat:

HUMAN:
"Add `--max-sessions` to the proxy."

AGENT:
"That is a new feature (new CLI option / behavior). I can propose a `PLAN.md`
update (new iteration or expanded ACTIVE iteration), but I cannot implement
until the plan is updated and approved."

## 6) Out-of-Plan Work (Docs/Tooling Only)

Allowed only when:
- The HUMAN explicitly declares it out-of-plan, AND
- It is docs/tooling/config only, and does not change runtime behavior.

Example chat:

HUMAN:
"Out of plan: add a beginner cheat sheet and link it from README."

AGENT:
"Confirmed: doc-only out-of-plan change. I will not touch runtime logic or
`PLAN.md`. I will add `docs/...` and update `README.md` to reference it."

## 7) Finishing an Iteration: Commit + PR

House rule from `AGENTS.md`:
- The agent should not take potentially impactful actions (like switching
  branches, committing, opening PRs) unless the HUMAN explicitly instructs it.

Typical flow:
1) Ensure tests pass for the iteration.
2) Commit the changes.
3) Open a pull request.

Example chat:

HUMAN:
"Finalize this iteration. You can commit all changes and open a PR."

AGENT:
"I will:
1) check `git status`,
2) run the smallest set of tests that cover the changes,
3) propose a commit message,
4) commit,
5) propose PR creation commands (or use `gh pr create` if available).
Confirm the target base branch (e.g. `main`) and the PR title."

Notes:
- Creating a PR usually requires your git remote hosting credentials and either
  a browser flow or GitHub CLI (`gh`). The agent can propose the exact commands,
  but you may need to run them depending on environment permissions.

Example commands (what the AGENT may propose, and may run if explicitly told):

```bash
git status
dotnet test
git add -A
git commit -m "docs: add agentic development cheat sheet"
git push -u origin HEAD
gh pr create --base main --fill
```

## 8) Ask-First Checklist (If Any Item Is "Yes", Stop and Ask)

- Not listed in ACTIVE iteration?
- Multiple valid technical approaches?
- Unclear edge cases or error semantics?
- Protocol / buffering / streaming changes?
- Adds new CLI option / env var / JSON schema field?

## 9) Quick Commands (Optional)

```bash
cat AGENTS.md
cat PLAN.md
git status
git branch --show-current
```
