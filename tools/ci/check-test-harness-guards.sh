#!/usr/bin/env bash
#
# check-test-harness-guards.sh
#
# AC-TC-02 (Networking Core epic, Story 002 — networking-test-harness.md /
# story-002-test-harness-observer-release-stripping.md):
#
#   "Given the production source tree (src/), when a static analysis pass runs on
#    all .cs files outside #if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD blocks,
#    then no call site references a method on any of the four test-harness
#    interfaces."
#
# The four test-harness interfaces are ITransportFaultInjector, IServerCrashInjector,
# IZoneTestConfigurator, and INetworkTestObserver. This script is a pre-build,
# source-text-only check — it does NOT require a Unity/IL2CPP build (that is
# AC-TC-01's job: a post-build binary string-scan of a Release Player build,
# wired as a separate, currently-placeholder CI job — see tests.yml).
#
# How it works:
#   For every *.cs file under the target directory, this script walks the file
#   line by line, tracking nested #if/#else/#endif conditional-compilation state.
#   A line is considered "guarded" if it is nested inside at least one #if (or
#   #elif) block whose condition is exactly `UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD`
#   (either operand order), OR if it is nested inside any enclosing block that is
#   itself already guarded. Reaching the #else branch of the guard condition itself
#   is treated as UNGUARDED (that branch is the "release build" code path) unless
#   an outer enclosing block is independently guarding it.
#
#   Any line outside a guarded region that contains one of the four interface
#   names is reported as a violation.
#
# Known limitations (acceptable for this codebase's actual convention, where every
# test-harness file wraps its entire contents in a single top-level #if/#endif):
#   - #elif conditions are not evaluated as a distinct guard condition; an #elif
#     branch inherits the parent scope's guard state as if it were a plain #else.
#   - Multi-line #if conditions (using a trailing `\` continuation) are not
#     supported; every #if is assumed to fit on one line, matching this project's
#     C# conditional-compilation style.
#
# Usage:
#   tools/ci/check-test-harness-guards.sh [src-dir]
#       Scans [src-dir] (default: src) and exits 1 if any unguarded reference is
#       found, printing every offending file:line. Exits 0 (silent, beyond a
#       summary line) if none are found.
#
#   tools/ci/check-test-harness-guards.sh --self-test
#       Proves this script is not a no-op (per AC-TC-02's own QA Test Case): builds
#       a small scratch fixture containing one correctly-guarded reference and one
#       deliberately UNGUARDED reference, runs the real check against it, and
#       asserts the guarded file produces zero violations while the unguarded file
#       produces exactly one. Exits 0 if both assertions hold, 1 otherwise.

set -euo pipefail

INTERFACES=("ITransportFaultInjector" "IServerCrashInjector" "IZoneTestConfigurator" "INetworkTestObserver")
PATTERN=$(IFS='|'; echo "${INTERFACES[*]}")

# Scans every *.cs file under $1, printing "file:line:content" for every
# unguarded reference to one of the four test-harness interface names found.
# Returns nothing (empty stdout) if none are found.
scan_directory() {
    local target_dir="$1"

    while IFS= read -r -d '' file; do
        local result
        result=$(awk -v pattern="$PATTERN" '
            function is_guard_condition(cond) {
                gsub(/[ \t]/, "", cond)
                return (cond == "UNITY_INCLUDE_TESTS||DEVELOPMENT_BUILD" || cond == "DEVELOPMENT_BUILD||UNITY_INCLUDE_TESTS")
            }
            BEGIN { depth = 0 }
            {
                trimmed = $0
                gsub(/^[ \t]+/, "", trimmed)

                if (trimmed ~ /^#if[ \t]/ || trimmed ~ /^#ifdef[ \t]/) {
                    cond = trimmed
                    sub(/^#(if|ifdef)[ \t]+/, "", cond)
                    depth++
                    is_guard[depth] = is_guard_condition(cond)
                    parent_guarded = (depth > 1 ? guarded[depth - 1] : 0)
                    guarded[depth] = parent_guarded || is_guard[depth]
                    next
                }
                if (trimmed ~ /^#elif[ \t]/) {
                    # Treated conservatively as inheriting the parent scope only —
                    # not re-evaluated as its own guard condition (see limitations).
                    parent_guarded = (depth > 1 ? guarded[depth - 1] : 0)
                    guarded[depth] = parent_guarded
                    next
                }
                if (trimmed ~ /^#else/) {
                    parent_guarded = (depth > 1 ? guarded[depth - 1] : 0)
                    if (is_guard[depth]) {
                        # The #else branch of the guard condition itself is the
                        # "release build" path — unguarded unless an outer block
                        # independently guards it.
                        guarded[depth] = parent_guarded
                    } else {
                        guarded[depth] = parent_guarded
                    }
                    next
                }
                if (trimmed ~ /^#endif/) {
                    depth--
                    next
                }

                currently_guarded = (depth > 0 ? guarded[depth] : 0)

                if (!currently_guarded && $0 ~ pattern) {
                    print FILENAME ":" NR ":" $0
                }
            }
        ' "$file")

        if [[ -n "$result" ]]; then
            printf '%s\n' "$result"
        fi
    done < <(find "$target_dir" -type f -name '*.cs' -print0 | sort -z)
}

run_check() {
    local target_dir="${1:-src}"

    if [[ ! -d "$target_dir" ]]; then
        echo "check-test-harness-guards: directory '$target_dir' does not exist." >&2
        return 1
    fi

    local violations
    violations=$(scan_directory "$target_dir")

    if [[ -n "$violations" ]]; then
        echo "AC-TC-02 FAILED: unguarded reference(s) to test-harness interfaces found in '$target_dir':" >&2
        while IFS= read -r line; do
            echo "  VIOLATION: $line" >&2
        done <<< "$violations"
        return 1
    fi

    echo "AC-TC-02 PASSED: no unguarded test-harness interface references found in '$target_dir'."
    return 0
}

run_self_test() {
    local scratch_dir
    scratch_dir=$(mktemp -d)
    trap 'rm -rf "$scratch_dir"' RETURN

    # A correctly-guarded reference — must NOT be flagged.
    cat > "$scratch_dir/Guarded.cs" <<'EOF'
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
namespace IronGrind.Networking
{
    internal static class GuardedCaller
    {
        internal static void Call(ITransportFaultInjector injector)
        {
            injector.Reset();
        }
    }
}
#endif
EOF

    # A deliberately UNGUARDED reference — must be flagged, proving this script
    # is not a no-op (AC-TC-02's own QA Test Case).
    cat > "$scratch_dir/Unguarded.cs" <<'EOF'
namespace IronGrind.Networking
{
    internal static class UnguardedCaller
    {
        internal static void Call(IServerCrashInjector injector)
        {
            injector.ClearRegistered();
        }
    }
}
EOF

    local violations
    violations=$(scan_directory "$scratch_dir")
    local violation_count
    violation_count=$(printf '%s\n' "$violations" | grep -c "Unguarded.cs" || true)
    local guarded_false_positive_count
    guarded_false_positive_count=$(printf '%s\n' "$violations" | grep -c "Guarded.cs" || true)

    local ok=1

    if [[ "$violation_count" -ne 1 ]]; then
        echo "SELF-TEST FAILED: expected exactly 1 violation in Unguarded.cs, found $violation_count." >&2
        ok=0
    fi

    if [[ "$guarded_false_positive_count" -ne 0 ]]; then
        echo "SELF-TEST FAILED: expected 0 violations in Guarded.cs, found $guarded_false_positive_count (false positive)." >&2
        ok=0
    fi

    if [[ "$ok" -eq 1 ]]; then
        echo "SELF-TEST PASSED: the unguarded fixture was correctly flagged, and the guarded fixture was not — this script is not a no-op."
        return 0
    fi

    return 1
}

main() {
    if [[ "${1:-}" == "--self-test" ]]; then
        run_self_test
        exit $?
    fi

    run_check "${1:-src}"
    exit $?
}

main "$@"
