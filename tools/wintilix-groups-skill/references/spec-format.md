# Team spec format

A spec is the compact file you write. `scripts/wintilix_groups.mjs compose|install` expands it into WinTilix's own group format and writes each agent's complete role prompt, so you only supply what is specific to the domain and the seat.

```json
{
  "name": "Team name shown in WinTilix",
  "summary": "One sentence for humans (not sent to agents).",
  "regulated": true,
  "context": "One short paragraph given to EVERY agent: what the team is for, the lifecycle or gates it follows, the standard of evidence.",
  "fallbackModel": "",
  "useWorktrees": false,
  "agents": [
    {
      "name": "chief-engineer",
      "kind": "Coordinator",
      "model": "opus",
      "title": "chief engineer and coordinator",
      "access": "write",
      "optional": false,
      "mission": "One sentence: the outcome this seat is responsible for.",
      "tasks": ["Concrete responsibilities, each a sentence."],
      "limits": ["What this seat must not do, each a sentence."],
      "folder": "",
      "firstMessage": ""
    }
  ]
}
```

| Field | Notes |
|---|---|
| `name` | Becomes the `claude -n` session name and the address teammates use. Letters, digits, `.`, `_`, `-`; unique. |
| `kind` | `Coordinator`, `Reviewer`, `Backend`, `Ui`, `Builder` or `Qa`. Only these six appear in the app's role box. Drives the tile colour. |
| `model` | `opus`, `sonnet` or `fable`, or a full model name. Opus for coordination, independent review and hard analysis; sonnet for volume work. |
| `access` | `write` (default) or `read-only`. Read-only agents may only write their own notes under `.team/reports/`. Use it for reviewers, auditors, QA that must not fix what they find. |
| `optional` | Seat skipped by `--core-only`. Optional seats must not be the only Reviewer or Qa. |
| `regulated` | Set for domains with standards or legal duties; the validator then warns if no Reviewer looks like a standards, compliance or ethics seat. |
| `useWorktrees` | Only for git projects. Gives each agent its own `<repo>-<name>` worktree on branch `agent/<name>`. Needs each agent's folder to be a git repository, so leave off for non-code teams. |
| `folder` | Usually blank: the user sets it per run. |
| `firstMessage` | Usually blank so the agent waits. Use it only for a seat that must start by itself. |

## What compose adds

For each agent the final prompt is: identity line, the shared `context`, `MISSION`, `RESPONSIBILITIES` (your `tasks`), `LIMITS`, an access rule, a rule for the kind (coordinator, reviewer, QA or maker behaviour), and the **team protocol**: the roster by name, use `ListAgents`/`SendMessage` with a `.team/` file fallback, report as DONE / EVIDENCE / BLOCKED, mark unverified facts, and escalate human decisions. Do not repeat these in your fields; that wastes the character budget (limit 6,000 per prompt).

## Commands

```
node scripts/wintilix_groups.mjs list
node scripts/wintilix_groups.mjs validate <spec.json> [--core-only]
node scripts/wintilix_groups.mjs compose  <spec.json> [--core-only] [--out groups.json]
node scripts/wintilix_groups.mjs install  <spec.json ...> | --all-presets [--core-only] [--dry-run] [--store <path>]
```

Run from the skill folder (`~/.claude/skills/wintilix-groups`). Node is the only dependency. `--store` points at another `agent-groups.json`, useful for testing. The group id is derived from the name, so reinstalling the same name replaces it.
