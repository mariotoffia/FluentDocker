# Internal AI-remediation prompt templates

> Internal tooling, not user documentation. These are the raw LLM prompts used to drive
> automated production-readiness remediation loops against `PROD_READY_ISSUES.md`-style
> review reports (see also `claude-migration-skill.md` in this directory). Kept under
> `tools/` so the repo root stays clean for release.

### Implement and test the plan
implement each task in PROD_READY_ISSUES.md:
{START LOOP} 
1. implement, test, and document it. 
2. Loop until zero bugs/issues left to be fixed -> {START LOOP} else {END LOOP}. (make sure that lint, test work properly)

{ADVERSIAL_LOOP_START}
1. Do a adversial QA review to gate for all fixes and if findings feed to {START LOOP} 
2. For each task finished - set the status to ✅ 
{ADVERSIAL_LOOP_END}

### Scanning code -> Plan

I want you to scan the code - use subagents to scan different areas of the code. Split by functionality and each driver do get their own review. Use a adversial review for each subagent:

Is the code production ready
Implementation is best practices net10
Is the documentation production ready
Do we have zero bugs
Is the code resilient so it can handle outages and come back on track
Is this library easy to consume and easy to understand
Is it easy find docs and read the docs (or are they overwhelming and bloated/fragmented)

Compile each subagent report from the main context - write in chunks (each chunk states what the subagent has examined e.g. driver dockerCLI and the findings from critical to minor. Report name: PROD_READY_ISSUES.md

Do a deep, thorough, precise, tough investigation and adeversial review.