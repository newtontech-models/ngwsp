# AI Agent Rules (Source of Truth)

This repository is governed by the rules in this file.
They apply to ALL AI coding assistants interacting with this codebase
(e.g. Codex, GitHub Copilot, ChatGPT, others).

Failure to follow these rules is considered incorrect behavior.

---

## 0. Language Policy (Mandatory)

- ALL communication MUST be in English.
- This includes:
  - questions
  - plans
  - TODOs
  - comments
  - commit messages
  - documentation
  - logs (unless explicitly stated otherwise)
- Never switch languages, even if the user uses another language.

---

## 1. Planning-First Rule (Mandatory)

Before writing, modifying, or deleting ANY code:

1. Read the current `PLAN.md`
2. Identify the ACTIVE iteration
3. Verify that the requested work is explicitly listed in that iteration

If `PLAN.md` does not exist:
- STOP
- Ask the user to create or approve a plan
- Do NOT implement anything

### Absolutely forbidden
- Writing code without an approved plan
- Implementing work not listed in the ACTIVE iteration
- “Sneaking in” refactors, cleanups, or improvements

---

## 2. PLAN.md Is the Source of Truth

`PLAN.md` defines:
- scope
- architecture
- priorities
- constraints
- decisions

Rules:
- Treat `PLAN.md` as authoritative
- If it is ambiguous, outdated, or contradictory:
  - STOP
  - Ask clarifying questions
- Never override the plan silently

---

## 3. Iteration Discipline

- Only ONE iteration may be ACTIVE at a time
- Iterations must be clearly marked as one of:
  - `ACTIVE`
  - `DONE`
  - `CANCELLED`

Rules:
- Work ONLY on TODOs from the ACTIVE iteration
- Do NOT continue to the next iteration automatically
- Do NOT assume future work

---

## 4. Ambiguity Handling (Critical)

If ANY of the following is true:
- multiple reasonable technical approaches exist
- a requirement is underspecified
- an architectural decision is implied but not explicit
- edge cases are unclear
- trade-offs exist that affect behavior, performance, or safety

Then:
1. STOP implementation
2. Ask concise clarifying questions (max 5)
3. Present 2–3 options with brief trade-offs if helpful

Do NOT guess.
Do NOT “pick what seems best”.

---

## 5. Similarity & Consistency Rule

When an example repository or existing system is referenced:

- Prefer copying:
  - architecture
  - folder structure
  - DI patterns
  - configuration style
  - logging & error handling approach
- Maintain consistency across projects

Any deviation:
- must be explicit
- must be justified

---

## 6. Implementation Rules

When implementation is explicitly approved (e.g. “implement”, “implement the active iteration”):

- Work in small, incremental steps
- After each step:
  - list modified files
  - describe what was done
  - state what remains undone
- Prefer clarity and maintainability over cleverness
- Avoid speculative abstractions

---

## 7. Safety & Scope Control

Unless explicitly instructed otherwise:
- Do NOT delete files
- Do NOT refactor unrelated code
- Do NOT rename public APIs
- Do NOT introduce new dependencies
- Do NOT change configuration defaults

If scope creep is detected:
- STOP
- Ask for confirmation

---

## 8. Communication Style

- Be explicit, structured, and concise
- Clearly separate:
  - planning
  - questions
  - implementation
- If blocked: explain why
- If unsure: ask

---

## 9. Default Operating Mode

Unless explicitly instructed otherwise:
- Default mode = PLANNING
- Coding requires an explicit instruction such as:
  - "implement"
  - "implement the active iteration"
  - "continue with implementation"

---

## 10. Note for AI Assistants

If you are an AI assistant:
- Read this file before generating code
- Follow these rules strictly
- When in doubt, ask

## 11. Branch Safety Check (Mandatory)

- Before starting any implementation work:
  - Determine the current git branch.
  - Determine the expected branch for the ACTIVE iteration
    (based on iteration number and name).
- If the current branch is NOT the expected branch:
  - DO NOT implement anything.
  - Warn the user clearly.
  - Propose the exact git command(s) needed to switch/create the correct branch.
- Never switch branches or create branches unless explicitly instructed.

## 11.1 Merge Policy (Mandatory)

- All merges to `main` MUST be **squash merges** (one commit).
- Do NOT create merge commits into `main`.
- If the user asks to “merge to main”, prefer:
  - `git merge --squash <branch>` followed by a single commit, or
  - an equivalent squash workflow the user prefers.

## 12. .gitignore Rules (Mandatory)

- The `.gitignore` file is the authoritative source for ignored files.
- The agent MUST NOT modify `.gitignore` unless explicitly instructed.
- If new generated/build artifacts are introduced:
  - Propose `.gitignore` changes first
  - Do NOT apply them without approval
- If `.gitignore` is missing or incomplete for the project type:
  - Point it out during planning or validation
  - Do NOT silently fix it
- Add to .gitignore by default:
  - playground/
  - *:Zone.Identifier
  - ./venv
  - **/*.env

## 13. Out-of-Plan Changes

- Out-of-plan changes are allowed ONLY when explicitly declared by the user.
- They must:
  - be clearly scoped
  - be tooling, documentation or configuration only (e.g., editor, CI, launch configs, docs)
  - not affect runtime logic or architecture
- During an out-of-plan change:
  - DO NOT modify PLAN.md
  - DO NOT advance iterations
  - DO NOT implement feature work
- If the requested change affects runtime behavior:
  - refuse and ask to update the plan instead

## 13.1 Out-of-Plan on `main` (Mandatory)

- When doing out-of-plan changes directly on `main`, do **NOT** create any commit unless the user explicitly confirms they want a commit.
- Always ask for confirmation before running `git commit` for out-of-plan work.

## 14. Bugs and New Features (Mandatory Rules)

### Bug Handling

- A bug is a defect in EXISTING, PLANNED behavior.

There are two categories:

#### Minor bug
Definition:
- localized fix
- no change to protocol, architecture, or scope

Rules:
- May be implemented WITHOUT updating PLAN.md
- MUST include a regression test
- MUST NOT introduce new features or refactors

If unsure whether a bug is minor:
- treat it as significant

#### Significant bug
Definition:
- affects protocol behavior
- affects streaming semantics, buffering, error handling
- requires design trade-offs or architectural decisions

Rules:
- STOP
- Propose a plan update or a new iteration
- Do NOT implement until PLAN.md is updated and approved

---

### New Feature Handling

- Any new feature ALWAYS requires a plan change.

Definition of a feature:
- new capability
- new option or configuration
- new protocol field
- new behavior observable by clients

Rules:
- Do NOT implement new features directly
- Propose:
  - plan update OR
  - new iteration
- Wait for explicit plan approval
- After approval:
  - update PLAN.md
  - validate plan
  - then implement

---

### Tests Requirement

- Every bug fix MUST include a regression test.
- Every feature MUST include tests validating the new behavior.
- If tests cannot be added:
  - explain why
  - ask for approval to proceed without tests
