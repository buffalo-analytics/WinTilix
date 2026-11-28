---
name: wintilix-groups
description: Design, research and install WinTilix agent groups (the "Open Claude Dev Group" teams of several Claude Code agents, each with a role, a model and a role prompt). Use this whenever the user wants a team, swarm, crew or group of agents for WinTilix - a software dev team, a mechanical or mechatronic engineering team, a scientific or veterinary review team, an ISO or standards-compliance team, or any other domain ("make me a group that does X") - or wants to add, change, list or deploy the saved WinTilix groups, role prompts, coordinators, QA or reviewer agents. Also use it when the user says dev group, agent group, WinTilix group, agent-groups.json, or asks how a team for some profession should be staffed, even if they never say the word skill.
---

# WinTilix agent groups

WinTilix opens a group as one tab of tiles, one `claude` session per tile. Each agent has a name, a role kind (badge colour), a model, an optional folder, and a **role prompt** appended to Claude's system prompt. Saved groups live in `%LOCALAPPDATA%\WinTilix\agent-groups.json` and appear in the group window's saved list, so installing a group makes it selectable.

This skill's job is to turn "I want a group that does X" into a well-staffed, well-prompted, installed group. The value is in two things: **researching how real teams in that domain are staffed**, and **writing role prompts that make each agent behave like a professional in that seat**. The scripts only do the mechanical part.

Run the script from the skill folder, `C:\Users\DavidP\.claude\skills\wintilix-groups` (Node 18+ is the only dependency; this machine has no Python).

## What a good group always has

Every group, whatever the field, gets the same backbone. Agents left to themselves tend to agree with each other and call work done too early, so the structure has to force independence:

- **One coordinator** (kind `Coordinator`, opus). Owns the task board, assigns work, runs the gates, escalates human decisions. Does not do the large work itself.
- **One or more makers** (`Builder`, `Backend`, `Ui`) who do the actual work in their discipline.
- **An independent reviewer** (`Reviewer`, read-only) that did not make the work and is the quality gate.
- **A verifier** (`Qa`) that checks against the acceptance criteria rather than the maker's description.
- **A standards/compliance/ethics reviewer** whenever the domain is regulated (machinery, medical, food, veterinary, finance, aviation, anything with ISO/IEC/legal obligations). This is the "ISO person".

The script refuses a team without a coordinator, a Reviewer, a Qa and a maker.

## Workflow

1. **Understand the ask.** Which domain, what kind of work (design, review, build, investigate), what outputs, and who signs off? If it is one of the shipped presets, say so and skip to step 4. Run `node scripts/wintilix_groups.mjs list` to see the presets and what is already installed.

2. **Research the domain before staffing it** (unless a preset fits). Read `references/research-checklist.md` and follow it. Short version: find how a real team in that field is organised, which standards and review gates govern it, what typically goes wrong, and what the deliverables are. Use web search or a deep-research skill and any domain skill the user has (for animal disease work, `african-controlled-diseases`). A team designed from memory of "what an engineer is" comes out generic; one designed from the field's actual gates and standards comes out useful.

3. **Compose the spec.** Write a team spec JSON (format in `references/spec-format.md`). Pick roles using `references/role-library.md`, which has the role patterns, the kind-to-colour mapping for non-software roles, and how to write a prompt that works. Save the spec in the user's project or a scratch folder; if the team will be reused, add it to `assets/specs/` so it ships as a preset. Mark nice-to-have seats `"optional": true` so the user can launch a smaller core team.

4. **Validate, then show the roster.** `node scripts/wintilix_groups.mjs validate <spec.json>` checks names, kinds, the backbone roster and prompt length. Show the user a short table (agent, kind, model, one-line job) and mention the optional seats and the standards you based it on. Ask before installing if the team is new; presets can be installed on request.

5. **Install.** `node scripts/wintilix_groups.mjs install <spec.json>` (add `--core-only` for the smaller team, `--dry-run` to preview). It backs up the store first and replaces a group of the same name, so re-running is safe. WinTilix reads the file whenever the group window opens, so no restart is needed.

6. **Tell the user how to deploy.** In WinTilix: Open Claude Dev Group, pick the saved group, set each agent's folder (or leave it blank for the home folder), choose a first message if wanted, and Open. For a git project turn on worktrees so builders do not share a checkout; leave worktrees off for non-code groups.

## Hard constraints from the app (why the scripts enforce them)

- The role box only offers `Coordinator, Reviewer, Backend, Ui, Builder, Qa`. Any other value displays blank and the badge goes grey. Map other professions onto the nearest kind; the real job is carried by the name, title and prompt.
- The prompt travels as a command line (`pwsh -EncodedCommand`), which inflates it about 2.7 times against a Windows limit of 32,767 characters. Keep each full prompt under 6,000 characters (aim for 3,000 to 4,500); the script fails above that.
- Agent names become `claude -n` session names and are what teammates use with `SendMessage`, so they must be unique and use only letters, digits, `.`, `_`, `-`. Use stable role names (`coordinator`, `qa`), not clever ones.
- Every agent starts in its folder or the home folder. Prompts therefore tell agents to work in the folder the human names, and to fall back to `.team/` files in that folder if `ListAgents` or `SendMessage` are not available.

## Things to get right

- Decisions with consequences stay with the human: spending, safety sign-off, regulatory declarations, deployment, anything outward-facing. The composed prompts say so; keep it that way when adding custom text.
- Never let an agent invent standards clauses, citations, figures or test results. The shared protocol demands "unverified" markings; reinforce it in roles where fabrication is costly (standards, citations, statistics).
- Do not edit the shipped presets to fit one project; make a copy with a new name.
- Do not touch `agent-roles.json` or `agent-colors.json`; WinTilix maintains them when a group opens.
- Optional extension, only if the user asks: add more role kinds (for example Research, Compliance) to `AgentRoleKind` in `src/WinTilix.Core/Agents/AgentRole.cs` so non-software roles get their own colours. That is an app change and needs a release.

## Testing safely

`list` and `install` default to the real store. When testing, or when the user wants a dry look, pass `--store <copy of agent-groups.json>` on every call so the real file is never read or changed.
