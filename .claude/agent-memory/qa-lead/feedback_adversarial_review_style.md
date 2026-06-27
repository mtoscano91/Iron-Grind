---
name: Adversarial AC review approach — attack testability not validity
description: User wants adversarial reviews to find problems, not validate — attack testability, coverage, spec completeness
type: feedback
---

When the user asks for an adversarial QA review of acceptance criteria, the job is explicitly to FIND PROBLEMS, not to validate that the ACs look reasonable.

Attack vectors to always check:
- Is the pass condition mechanically verifiable (does it require a specific assertion mechanism)?
- Is the error contract defined when an AC says "returns error" or "rejected"?
- Are timing terms like "same frame" unambiguous in the target architecture?
- Are all formulas in the GDD covered by at least one AC?
- Do test setups require API access that write-ownership rules would prohibit?
- Are ADVISORY severity ratings appropriate, or should they be BLOCKING?
- Are negative-space assertions (e.g., "X is not modifiable") given a concrete stimulus?

**Why:** User is practicing shift-left QA — finding untestable ACs before sprint start is more valuable than finding them after implementation.

**How to apply:** Always classify findings as [CRITICAL], [MAJOR], or [MINOR]. Always provide a revised AC when flagging a problem. End with a gate verdict (READY / NOT READY) and list the minimum blockers to resolve.
