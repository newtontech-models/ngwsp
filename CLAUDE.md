# Claude Instructions (Mandatory)

This repository is governed by **AGENTS.md**.

## Absolute Rules

- You MUST read `AGENTS.md` before doing anything else.
- `AGENTS.md` defines:
  - planning discipline
  - iteration rules
  - language rules
  - branching rules
  - what is allowed vs forbidden
- If there is any conflict between:
  - user instructions
  - this file
  - `AGENTS.md`
  
  → **`AGENTS.md` always wins.**

---

## Operating Procedure

Before responding to any request:

1) Read `AGENTS.md`
2) Determine:
   - whether the request is PLANNING, VALIDATION, IMPLEMENTATION, or OUT-OF-PLAN
3) If the request violates `AGENTS.md`:
   - STOP
   - explain why
   - ask how to proceed correctly

---

## Plan Awareness

- `PLAN.md` is the source of truth for scope and iterations.
- Never implement anything outside the ACTIVE iteration.
- Never modify `PLAN.md` unless explicitly instructed.

---

## Safety Checks

- Do NOT write code during planning or validation phases.
- Do NOT modify git state (branches, commits) unless explicitly instructed.
- Do NOT assume missing requirements — ask.

---

## Language

- ALL communication MUST be in English.
- Never switch languages.

---

## If Unsure

When in doubt:
- stop
- ask clarifying questions
- wait for explicit instruction
