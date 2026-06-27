#!/bin/bash
# Claude Code PostToolUse hook: GDD triad reminder
# Fires after any Write or Edit to a file in design/gdd/
# Advisory only — exit 0, never blocks.
#
# The GDD Revision Triad: when a GDD changes, three files must be updated
# together: the GDD itself, design/registry/entities.yaml, and
# production/session-state/active.md. This hook reminds Claude to do so.

INPUT=$(cat)

# Parse file path — use jq if available, fall back to grep
if command -v jq >/dev/null 2>&1; then
    FILE_PATH=$(echo "$INPUT" | jq -r '.tool_input.file_path // empty')
else
    FILE_PATH=$(echo "$INPUT" | grep -oE '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | sed 's/"file_path"[[:space:]]*:[[:space:]]*"//;s/"$//')
fi

# Normalize path separators (Windows backslash to forward slash)
FILE_PATH=$(echo "$FILE_PATH" | sed 's|\\|/|g')

# Only trigger for files inside design/gdd/ — skip index, reviews, and non-GDD files
if ! echo "$FILE_PATH" | grep -qE '(^|/)design/gdd/[^/]+\.md$'; then
    exit 0
fi

# Skip the systems index itself (it is part of the triad update, not a trigger)
BASENAME=$(basename "$FILE_PATH")
if [ "$BASENAME" = "systems-index.md" ] || [ "$BASENAME" = "game-concept.md" ]; then
    exit 0
fi

# Check whether the entity registry was also updated this session
# (advisory only — we check for recent modification as a proxy)
REGISTRY="design/registry/entities.yaml"
STATE="production/session-state/active.md"

MISSING=""
[ ! -f "$REGISTRY" ] && MISSING="$MISSING\n  - $REGISTRY (not found)"
[ ! -f "$STATE" ]    && MISSING="$MISSING\n  - $STATE (not found)"

echo "=== GDD Triad Reminder ===" >&2
echo "GDD edited: $FILE_PATH" >&2
echo "" >&2
echo "The GDD Revision Triad requires all three files updated before this task" >&2
echo "is considered complete:" >&2
echo "  1. design/gdd/[system].md       ← just edited" >&2
echo "  2. design/registry/entities.yaml ← update if entities/stats changed" >&2
echo "  3. production/session-state/active.md ← update with current progress" >&2
echo "" >&2
if [ -n "$MISSING" ]; then
    echo -e "  Missing files:$MISSING" >&2
    echo "" >&2
fi
echo "Also update design/gdd/systems-index.md if the GDD status changed." >&2
echo "=== (advisory — not blocking) ===" >&2

exit 0
