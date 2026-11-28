#!/usr/bin/env node
// WinTilix agent-group tool: team spec -> validated group definition -> installed saved group.
//
//   node wintilix_groups.mjs list
//   node wintilix_groups.mjs validate <spec.json> [--core-only]
//   node wintilix_groups.mjs compose  <spec.json> [--core-only] [--out groups.json]
//   node wintilix_groups.mjs install  <spec.json ...> | --all-presets  [--core-only] [--dry-run] [--store <path>]
//
// A spec is the compact, human-edited description of a team (see references/spec-format.md).
// compose expands it into the exact objects WinTilix keeps in %LOCALAPPDATA%\WinTilix\agent-groups.json,
// writing each agent's full role prompt (role + limits + the shared team protocol).
// install merges those groups into the store (replacing a group of the same name, backing the file up first).

import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const specsDir = path.resolve(here, "..", "assets", "specs");

// The group window's role box only offers these six kinds; anything else shows blank and falls back to
// a grey badge. They drive the tile colour, so non-software roles are mapped onto the nearest kind.
const KINDS = ["Coordinator", "Reviewer", "Backend", "Ui", "Builder", "Qa"];
const MODEL_ALIASES = ["opus", "sonnet", "fable"];
// The prompt travels inside `pwsh -EncodedCommand <base64 of UTF-16>`, about 2.7x its length, and
// CreateProcess stops at 32,767 characters. 6,000 characters encodes to about 16,000, leaving a wide margin.
const PROMPT_ERROR = 6000;
const PROMPT_WARN = 4800;

const KIND_RULES = {
  Coordinator:
    "As coordinator you own .team/board.md: break the goal into tasks, each with an owner, an acceptance check and a status. " +
    "Assign work, keep everyone unblocked, and settle disagreements or escalate them to the human. " +
    "Do not do large pieces of the work yourself. Nothing is 'done' until the reviewer and QA have both signed off.",
  Reviewer:
    "Give a verdict per item: PASS, PASS WITH NOTES or FAIL, with findings ranked by severity and each pointing at a file, line or section. " +
    "You are independent of whoever made the work. Say plainly what you did not check.",
  Qa:
    "Verify against the acceptance check, never against the maker's description of what they did. " +
    "Report exact steps, expected result and actual result so anyone can reproduce your finding.",
  Backend:
    "Work only on tasks the coordinator assigns. Hand finished work to the reviewer and QA with evidence; never mark your own work approved.",
  Ui:
    "Work only on tasks the coordinator assigns. Hand finished work to the reviewer and QA with evidence; never mark your own work approved.",
  Builder:
    "Work only on tasks the coordinator assigns. Hand finished work to the reviewer and QA with evidence; never mark your own work approved.",
};

const ACCESS_RULES = {
  "read-only":
    "ACCESS: read-only on the deliverables. Never edit, overwrite, commit or push project files. " +
    "Your only writing is your own notes under .team/reports/<your-name>-*.md.",
  write:
    "ACCESS: change only what your assigned task covers. Do not touch other agents' work. " +
    "If the project is a git repository, commit only your own files, listed by explicit path.",
};

function fail(msg) {
  console.error("error: " + msg);
  process.exit(1);
}

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, "utf8").replace(/^﻿/, ""));
  } catch (e) {
    fail(`cannot read ${file}: ${e.message}`);
  }
}

function storePath(override) {
  if (override) return path.resolve(override);
  const root = process.env.LOCALAPPDATA;
  if (!root) fail("LOCALAPPDATA is not set; pass --store <path to agent-groups.json>");
  return path.join(root, "WinTilix", "agent-groups.json");
}

// Stable id from the group name, so installing the same team twice replaces it instead of duplicating it.
function groupId(name) {
  return crypto.createHash("md5").update("wintilix-groups:" + name.trim().toLowerCase()).digest("hex");
}

function selectAgents(spec, coreOnly) {
  return spec.agents.filter((a) => !(coreOnly && a.optional));
}

function bullets(items) {
  return items.map((s) => "- " + s.replace(/\s+/g, " ").trim()).join("\n");
}

function buildPrompt(spec, agent, team) {
  const peers = team
    .filter((p) => p.name !== agent.name)
    .map((p) => `${p.name} (${p.title})`)
    .join("; ");
  const parts = [];
  parts.push(`You are "${agent.name}", the ${agent.title} of the "${spec.name}" team.`);
  if (spec.context) parts.push(spec.context.trim());
  parts.push(`MISSION: ${agent.mission.trim()}`);
  if (agent.tasks?.length) parts.push("RESPONSIBILITIES:\n" + bullets(agent.tasks));
  if (agent.limits?.length) parts.push("LIMITS:\n" + bullets(agent.limits));
  parts.push(ACCESS_RULES[agent.access ?? "write"]);
  parts.push(KIND_RULES[agent.kind]);
  parts.push(
    `TEAM PROTOCOL: your team is ${peers || "just you"}. Those names are Claude session names: run ListAgents to see who is live and use SendMessage to reach them. ` +
      `If either tool is missing, use the project folder instead: .team/board.md (the coordinator's task list) and .team/inbox-<name>.md (append-only messages). ` +
      `Work in the project folder the human names; if you start in the home folder, ask the coordinator which one. ` +
      `Report as DONE / EVIDENCE / BLOCKED, in a few lines. Mark anything you could not verify as 'unverified' and never invent a source, figure or test result. ` +
      `Decisions that belong to the human (money, safety sign-off, scope, anything outward-facing) go to the human, not to a teammate.`
  );
  return parts.join("\n\n");
}

function validateSpec(spec, coreOnly) {
  const errors = [];
  const warnings = [];
  if (!spec || typeof spec !== "object") return { errors: ["spec is not an object"], warnings };
  if (!spec.name?.trim()) errors.push("spec.name is required");
  if (!Array.isArray(spec.agents) || spec.agents.length === 0) {
    errors.push("spec.agents needs at least one agent");
    return { errors, warnings };
  }
  const team = selectAgents(spec, coreOnly);
  const seen = new Set();
  for (const a of team) {
    const label = `agent '${a.name ?? "?"}'`;
    if (!a.name || !/^[\w.-]+$/.test(a.name)) errors.push(`${label}: name must be letters, digits, '.', '_' or '-' (it becomes the claude -n session name)`);
    else if (seen.has(a.name.toLowerCase())) errors.push(`${label}: duplicate name`);
    else seen.add(a.name.toLowerCase());
    if (!KINDS.includes(a.kind)) errors.push(`${label}: kind must be one of ${KINDS.join(", ")} (got '${a.kind}')`);
    if (!a.model?.trim()) errors.push(`${label}: model is required`);
    else if (!MODEL_ALIASES.includes(a.model)) warnings.push(`${label}: model '${a.model}' is not an alias (${MODEL_ALIASES.join("/")}); fine only if it is a full model name`);
    if (!a.title?.trim()) errors.push(`${label}: title is required`);
    if (!a.mission?.trim()) errors.push(`${label}: mission is required`);
    if (a.access && !ACCESS_RULES[a.access]) errors.push(`${label}: access must be 'write' or 'read-only'`);
  }
  // The roster rules that make a team trustworthy rather than a pile of builders.
  const kinds = team.map((a) => a.kind);
  const coordinators = kinds.filter((k) => k === "Coordinator").length;
  if (coordinators !== 1) errors.push(`a team needs exactly one Coordinator (found ${coordinators})`);
  if (!kinds.includes("Qa")) errors.push("a team needs a Qa agent: someone who verifies against the acceptance check");
  if (!kinds.includes("Reviewer")) errors.push("a team needs a Reviewer agent: an independent gate that did not make the work");
  if (!team.some((a) => !["Coordinator", "Reviewer", "Qa"].includes(a.kind))) errors.push("a team needs at least one maker (Builder, Backend or Ui)");
  if (spec.regulated && !team.some((a) => a.kind === "Reviewer" && /standard|compliance|ethic|regulat|iso/i.test(a.name + " " + a.title))) {
    warnings.push("spec is marked regulated but no Reviewer looks like a standards/compliance role");
  }
  if (!errors.length) {
    for (const a of team) {
      const len = buildPrompt(spec, a, team).length;
      if (len > PROMPT_ERROR) errors.push(`agent '${a.name}': prompt is ${len} characters (limit ${PROMPT_ERROR}); shorten the mission, tasks or limits`);
      else if (len > PROMPT_WARN) warnings.push(`agent '${a.name}': prompt is ${len} characters (getting close to ${PROMPT_ERROR})`);
    }
  }
  return { errors, warnings };
}

function composeGroup(spec, coreOnly, suffix = "") {
  const team = selectAgents(spec, coreOnly);
  const name = spec.name.trim() + suffix;
  return {
    id: groupId(name),
    name,
    fallbackModel: spec.fallbackModel ?? "",
    useWorktrees: !!spec.useWorktrees,
    agents: team.map((a) => ({
      name: a.name,
      role: a.kind,
      model: a.model,
      folder: a.folder ?? "",
      prompt: buildPrompt(spec, a, team),
      firstMessage: a.firstMessage ?? "",
    })),
  };
}

function loadSpec(file, coreOnly) {
  const spec = readJson(file);
  const { errors, warnings } = validateSpec(spec, coreOnly);
  for (const w of warnings) console.error(`warning (${path.basename(file)}): ${w}`);
  if (errors.length) fail(`${path.basename(file)} is not valid:\n  - ` + errors.join("\n  - "));
  return spec;
}

function summarize(group) {
  const rows = group.agents.map((a) => `  ${a.name.padEnd(22)} ${a.role.padEnd(12)} ${a.model.padEnd(7)} prompt ${String(a.prompt.length).padStart(4)} chars`);
  return `${group.name}  (${group.agents.length} agents, worktrees ${group.useWorktrees ? "on" : "off"})\n` + rows.join("\n");
}

function presetFiles() {
  return fs.existsSync(specsDir) ? fs.readdirSync(specsDir).filter((f) => f.endsWith(".json")).sort().map((f) => path.join(specsDir, f)) : [];
}

function parseArgs(argv) {
  const out = { files: [], flags: new Set(), opts: {} };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === "--out" || a === "--store") out.opts[a.slice(2)] = argv[++i];
    else if (a.startsWith("--")) out.flags.add(a.slice(2));
    else out.files.push(a);
  }
  return out;
}

const [cmd, ...rest] = process.argv.slice(2);
const args = parseArgs(rest);
const coreOnly = args.flags.has("core-only");

switch (cmd) {
  case "list": {
    console.log("Preset team specs (assets/specs):");
    for (const f of presetFiles()) {
      const s = readJson(f);
      const optional = s.agents.filter((a) => a.optional).length;
      console.log(`  ${path.basename(f).padEnd(34)} ${s.name}  -  ${s.agents.length} agents (${optional} optional)`);
    }
    const store = storePath(args.opts.store);
    console.log(`\nInstalled in ${store}:`);
    if (!fs.existsSync(store)) console.log("  (no store file yet)");
    else for (const g of readJson(store)) console.log(`  ${g.name}  -  ${g.agents.length} agents`);
    break;
  }
  case "validate": {
    if (!args.files.length) fail("validate needs a spec file");
    for (const f of args.files) {
      const spec = loadSpec(f, coreOnly);
      console.log("ok  " + summarize(composeGroup(spec, coreOnly)));
    }
    break;
  }
  case "compose": {
    if (args.files.length !== 1) fail("compose needs exactly one spec file");
    const group = composeGroup(loadSpec(args.files[0], coreOnly), coreOnly);
    if (args.opts.out) {
      fs.writeFileSync(args.opts.out, JSON.stringify([group], null, 2));
      console.log(summarize(group) + `\nwritten to ${args.opts.out}`);
    } else {
      console.log(JSON.stringify([group], null, 2));
    }
    break;
  }
  case "install": {
    const files = args.flags.has("all-presets") ? presetFiles() : args.files;
    if (!files.length) fail("install needs spec files or --all-presets");
    const groups = files.map((f) => composeGroup(loadSpec(f, coreOnly), coreOnly));
    const store = storePath(args.opts.store);
    const existing = fs.existsSync(store) ? readJson(store) : [];
    if (!Array.isArray(existing)) fail(`${store} is not a JSON array; refusing to touch it`);
    const names = new Set(groups.map((g) => g.name.toLowerCase()));
    const kept = existing.filter((g) => !names.has(String(g.name).toLowerCase()));
    const replaced = existing.length - kept.length;
    const merged = [...kept, ...groups].sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: "base" }));
    for (const g of groups) console.log(summarize(g) + "\n");
    if (args.flags.has("dry-run")) {
      console.log(`dry run: would write ${merged.length} groups to ${store} (${groups.length} installed, ${replaced} replaced, ${kept.length} untouched)`);
      break;
    }
    fs.mkdirSync(path.dirname(store), { recursive: true });
    let backup = "";
    if (fs.existsSync(store)) {
      backup = `${store}.bak-${new Date().toISOString().replace(/[:.]/g, "-")}`;
      fs.copyFileSync(store, backup);
    }
    fs.writeFileSync(store, JSON.stringify(merged, null, 2));
    console.log(`installed ${groups.length} group(s) into ${store} (${replaced} replaced, ${kept.length} untouched)` + (backup ? `\nbackup: ${backup}` : ""));
    break;
  }
  case "export-templates": {
    // Writes every preset as a full and a "(core)" group definition, for embedding in the WinTilix app
    // (src/WinTilix.Core/Agents/Templates). The ids are derived from the names, so they never change.
    const outDir = args.files[0];
    if (!outDir) fail("export-templates needs an output folder");
    fs.mkdirSync(outDir, { recursive: true });
    for (const f of presetFiles()) {
      const slug = path.basename(f, ".json");
      for (const core of [false, true]) {
        const spec = loadSpec(f, core);
        if (core && !spec.agents.some((a) => a.optional)) continue;
        const group = composeGroup(spec, core, core ? " (core)" : "");
        fs.writeFileSync(path.join(outDir, core ? slug + "-core.json" : slug + ".json"), JSON.stringify(group, null, 2));
        console.log(`${group.name}  (${group.agents.length} agents)`);
      }
    }
    break;
  }
  default:
    console.log("usage: node wintilix_groups.mjs list | validate <spec> | compose <spec> [--out f] | export-templates <dir> | install <spec...|--all-presets> [--core-only] [--dry-run] [--store path]");
    process.exit(cmd ? 1 : 0);
}
