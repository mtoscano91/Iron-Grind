---
name: Adversarial Review Style — Pass 3 confirmed approach
description: How to conduct adversarial economy design reviews for this project — structure, depth, and what the user expects
type: feedback
---

The user runs multi-pass design reviews. Economy adversarial reviews are called explicitly with full GDD content provided in context. The expected output format is:

- Numbered findings (max 8)
- Each finding: domain, severity (BLOCKING/RECOMMENDED/ADVISORY), specific claim, why it matters, what would fix it
- Summary table at end
- Priority call identifying the highest-stakes finding

**Why:** This is a structured review process feeding into GDD revision passes. Findings become tracked issues resolved in the next revision session.

**How to apply:** Always read the actual GDD file, review log, entities.yaml, and any referenced GDDs before writing findings. Cross-reference specific line numbers and formula names. Generic claims ("the economy might have problems") are not useful — name the specific formula, the specific line, the specific failure condition. The user has invested multiple passes in this document and expects findings that prior passes missed.

Severity levels:
- BLOCKING: Will cause a demonstrable economy failure or pillar collapse if unresolved before implementation
- RECOMMENDED: Creates a design incoherence or technical debt that will require rework later
- ADVISORY: Worth noting but does not block progress; can be addressed in tuning or downstream GDDs
