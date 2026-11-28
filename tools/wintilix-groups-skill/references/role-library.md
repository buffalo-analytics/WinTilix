# Role library

Patterns for staffing a team and for writing the seat prompts. Contents: the standard roster, role patterns by function, mapping to the six app kinds, writing a prompt that works, sizing.

## The standard roster

| Function | Kind | Model | Access | Why it exists |
|---|---|---|---|---|
| Coordinator | Coordinator | opus | write (board only) | One owner of scope, tasks, gates and human communication. Without one, agents duplicate or contradict each other. |
| Architect / systems engineer / review designer | Builder or Reviewer | opus | write or read-only | Fixes the structure before makers start. For engineering this is requirements and interfaces; for reviews it is the protocol. |
| Makers (1 to 4) | Builder / Backend / Ui | sonnet | write | The actual work, split by discipline so each has a clear lane. |
| Independent reviewer | Reviewer | opus | read-only | A gate that did not make the work. Opus because finding what the author missed is the hardest job. |
| Verifier | Qa | sonnet | read-only | Checks the result against acceptance criteria from a clean state; does not fix. |
| Standards, compliance or ethics | Reviewer | opus | read-only | For regulated work. Keeps the matrix of obligations and evidence; never signs a declaration. |
| Specialist critic (security, red team, risk) | Reviewer | opus | read-only | Adversarial view on one failure class. |
| Release, manufacturing, publishing | Builder | sonnet | write | Gets the work out of the team safely. Optional for small jobs. |

Team size: 5 to 6 is a small core, 8 to 10 is full. Each extra agent costs a session and adds coordination, so make a seat optional unless the team genuinely fails without it. Two makers who do the same thing are not worth two seats; split by discipline instead.

## Mapping non-software roles onto the six kinds

The kind only chooses the tile colour, so pick the one that matches how the seat behaves in the gate:

- Plans, coordinates, owns the board: `Coordinator`.
- Judges others' work, read-only, issues verdicts (design reviewer, standards engineer, appraiser, red team, security): `Reviewer`.
- Tests or verifies against criteria (verification engineer, citation checker, QA): `Qa`.
- Does technical or analytical work behind the scenes (controls, simulation, statistics, services): `Backend`.
- Produces what a person reads or uses (UI, report writer, drawings, user-facing artefacts): `Ui`.
- Everything else that makes something (mechanical design, literature search, manufacturing, release): `Builder`.

## Role patterns by field (starting points, not limits)

- **Software**: coordinator, architect, backend, ui, builder, gate reviewer, security, QA, release.
- **Mechanical and mechatronic**: chief engineer, systems and requirements, mechanical design, analysis and simulation, controls, electronics and firmware, standards and safety, design reviewer, verification, manufacturing and DFM. Standards: ISO 12100 (risk assessment), ISO 13849-1 and IEC 62061 (safety control functions), IEC 60204-1, IEC 61131-3, ISO 9001, ISO 286 and ISO 2768 (fits and tolerances), ISO 1101 (GD&T), plus machinery legislation for the target market.
- **Scientific and evidence review**: review lead, literature search, appraiser, statistician, domain expert, citation checker, red team, writer, regulatory and ethics. Frameworks: PRISMA, GRADE, CONSORT/REFLECT, STROBE, STARD, ARRIVE.
- **Other fields you may be asked for** (research each properly, see the checklist): electrical and power engineering, civil and structural, chemical and process, medical device (ISO 13485, IEC 62304, ISO 14971), food safety (HACCP, ISO 22000), data and machine learning, legal or contract review, financial audit, technical writing and documentation, security assessment, agricultural and environmental assessment.

Always confirm a standard's existence and current edition when you cite it in a spec; do not rely on memory for clause numbers.

## Writing a seat's fields

The prompt is assembled for you; you supply `mission`, `tasks`, `limits`, and the team `context`. What makes them work:

- **Mission is an outcome, not a job title.** "Prove the design works on paper before metal is cut" tells the agent what good looks like; "do analysis" does not.
- **Tasks are verbs with evidence.** Say what the seat produces and what proof it attaches: "Recompute headline effect sizes and compare", "paste the test result". Agents that must show evidence stop reporting hopes.
- **Limits protect the gate.** A reviewer must not fix; QA must not adjust criteria; a standards seat must not sign conformity; a release seat must not deploy without the human. Write the limit that would hurt most if the seat quietly crossed it.
- **Put domain knowledge in tasks and context**: the named standards, frameworks, checklists, tools and typical failure modes. That is where research pays off.
- **Keep it short.** Each full prompt must stay under 6,000 characters including the shared protocol (about 1,000 of that is protocol; aim for 3,000 to 4,500 in total). Cut adjectives, keep specifics.
- **Name the human-owned decisions** for that seat (money, safety sign-off, regulatory declarations, outward-facing action).
- **Use opus for judgement, sonnet for volume.** Coordinator, independent reviewers, standards and hard analysis get opus.
