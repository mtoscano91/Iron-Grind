#!/usr/bin/env bash
# Story 016 (AC-AUTH-08) TLS-only front check for the VPS Caddy + gateway. First run: Story 017.
# Same five checks as check-tls-only.ps1:
#   a. plaintext to 443 closed within 2 s; received bytes are nothing or exactly Go's fixed rejection
#   b. gateway log gained no "Request starting" line during (a); reported last, because (d) and (e) must add such
#      lines as a positive control (otherwise request logging is not visible and (b) fails)
#   c. TCP connect to port 80 refused
#   d. TLS POST /v1/login with the fixture returns 200, application/octet-stream, body 00 01
#   e. TLS POST /v1/register with 00 FF FF returns 400 (forwarded to the gateway)
# Exits 0 when all pass, 1 on any failure, 2 on bad arguments.
#
# Usage (run with bash, not sh: the probes use bash's /dev/tcp):
#   bash check-tls-only.sh --gateway-log <path> [--host <name>] [--cacert <path>] [--fixture <path>]
#
# Requires: bash, timeout (coreutils), curl, cmp, od, stat, grep, mktemp.
set -u
# English error text for the port-80 "refused" match, whatever the VPS locale.
export LC_ALL=C

HOST="localhost"
GATEWAY_LOG=""
CACERT=""
FIXTURE="$(cd "$(dirname "$0")" && pwd)/fixtures/login-unknown-user.bin"
PROBE_TIMEOUT=2
# Wait before reading the gateway log: its console logger writes asynchronously.
LOG_SETTLE_SECONDS=0.2
FAILS=0

usage() {
    echo "usage: $0 --gateway-log <path> [--host <name>] [--cacert <path>] [--fixture <path>]"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --gateway-log) GATEWAY_LOG="${2:-}"; shift 2 ;;
        --host)        HOST="${2:-}"; shift 2 ;;
        --cacert)      CACERT="${2:-}"; shift 2 ;;
        --fixture)     FIXTURE="${2:-}"; shift 2 ;;
        -h|--help)     usage; exit 0 ;;
        *)             echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [ -z "$GATEWAY_LOG" ]; then
    echo "--gateway-log is required" >&2
    usage >&2
    exit 2
fi

if [ -n "$CACERT" ]; then
    CACERT_ARGS=(--cacert "$CACERT")
else
    CACERT_ARGS=()
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

pass() { echo "[PASS] $1 - $2"; }
fail() { echo "[FAIL] $1 - $2"; FAILS=$((FAILS + 1)); }

# Expected bytes, written with printf so they are exact.
printf 'HTTP/1.0 400 Bad Request\r\n\r\nClient sent an HTTP request to an HTTPS server.\n' > "$TMP_DIR/go-rejection.txt"
printf 'POST /v1/login HTTP/1.1\r\nHost: %s\r\nContent-Type: application/octet-stream\r\nContent-Length: 0\r\n\r\n' "$HOST" > "$TMP_DIR/plaintext-request.txt"
printf '\x00\x01' > "$TMP_DIR/expect-login.bin"
printf '\x00\xff\xff' > "$TMP_DIR/register.bin"

# Record the gateway log length before the plaintext probe (check b compares against it).
LOG_BEFORE=-1
if [ -f "$GATEWAY_LOG" ]; then
    LOG_BEFORE=$(stat -c %s "$GATEWAY_LOG")
fi

# ---- a. plaintext probe. Exit 0 = EOF within the timeout, 124 = still open, 3 = connect failed,
# 5 = closed before the request was written (no bytes; allowed by AC-AUTH-08). SIGPIPE is ignored so a write to a
# closed socket returns an error instead of killing the probe.
RECV="$TMP_DIR/plaintext-received.bin"
: > "$RECV"
timeout "$PROBE_TIMEOUT" bash -c '
    trap "" PIPE
    exec 3<>"/dev/tcp/$1/443" || exit 3
    cat "$2" >&3 2>/dev/null || exit 5
    cat <&3 > "$3"
' _ "$HOST" "$TMP_DIR/plaintext-request.txt" "$RECV"
PROBE_RC=$?

case "$PROBE_RC" in
    5)
        : > "$RECV"
        pass "a plaintext to 443 closed within 2 s with nothing or Go rejection" "closed before the request was written"
        ;;
    0)
        if [ ! -s "$RECV" ] || cmp -s "$RECV" "$TMP_DIR/go-rejection.txt"; then
            RECV_BYTES=$(stat -c %s "$RECV")
            pass "a plaintext to 443 closed within 2 s with nothing or Go rejection" "received $RECV_BYTES bytes"
        else
            fail "a plaintext to 443 closed within 2 s with nothing or Go rejection" "unexpected bytes received"
        fi
        ;;
    124) fail "a plaintext to 443 closed within 2 s with nothing or Go rejection" "no EOF within 2 s" ;;
    3)   fail "a plaintext to 443 closed within 2 s with nothing or Go rejection" "connect to 443 failed" ;;
    *)   fail "a plaintext to 443 closed within 2 s with nothing or Go rejection" "probe error (exit $PROBE_RC)" ;;
esac
echo "  plaintext probe received (text): $(cat -v "$RECV")"
echo "  plaintext probe received (hex):  $(od -An -tx1 "$RECV" | tr -s ' \n' ' ' | sed 's/^ //;s/ $//')"
[ -s "$RECV" ] || echo "  (empty)"

# Measure the log after the probe; check b is reported after (e), which supplies its positive control.
sleep "$LOG_SETTLE_SECONDS"
log_length() { if [ -f "$GATEWAY_LOG" ]; then stat -c %s "$GATEWAY_LOG"; else echo -1; fi; }
# Count of 'Request starting' lines in the log between byte offsets $1 and $2.
count_requests() { tail -c +"$(($1 + 1))" "$GATEWAY_LOG" | head -c "$(($2 - $1))" | grep -c 'Request starting'; }
LOG_AFTER_PROBE=$(log_length)

# ---- c. port 80 refused. Exit 124 = timed out, 0 = connected, otherwise inspect the error text.
ERR80=$(timeout "$PROBE_TIMEOUT" bash -c 'exec 3<>"/dev/tcp/$1/80"' _ "$HOST" 2>&1)
RC80=$?
if [ "$RC80" -eq 124 ]; then
    fail "c TCP connect to port 80 refused" "timed out after 2 s (expected a refused connection)"
elif [ "$RC80" -eq 0 ]; then
    fail "c TCP connect to port 80 refused" "connection SUCCEEDED: something listens on port 80"
elif printf '%s' "$ERR80" | grep -qi 'refused'; then
    pass "c TCP connect to port 80 refused" "connection refused"
else
    fail "c TCP connect to port 80 refused" "expected 'connection refused', got: $ERR80"
fi

# ---- TLS helper. Sets TLS_RC (curl exit), TLS_CODE (HTTP status), TLS_CTYPE (content type); body in $3.
tls_post() {
    local meta
    meta=$(curl -sS "${CACERT_ARGS[@]}" -o "$3" -w '%{http_code} %{content_type}' -X POST \
        --data-binary "@$2" -H 'Content-Type: application/octet-stream' "https://$HOST$1" 2>"$TMP_DIR/curl.err")
    TLS_RC=$?
    TLS_CODE=$(printf '%s' "$meta" | awk '{print $1}')
    TLS_CTYPE=$(printf '%s' "$meta" | awk '{print $2}')
}

# ---- d. TLS login through Caddy
if [ -n "$CACERT" ] && [ ! -f "$CACERT" ]; then
    fail "d TLS POST /v1/login returns 200 + 00 01" "CA root not found: $CACERT"
elif [ ! -f "$FIXTURE" ]; then
    fail "d TLS POST /v1/login returns 200 + 00 01" "fixture not found: $FIXTURE"
else
    tls_post /v1/login "$FIXTURE" "$TMP_DIR/login-body.bin"
    if [ "$TLS_RC" -eq 0 ] && [ "$TLS_CODE" = "200" ] \
        && [ "$TLS_CTYPE" = "application/octet-stream" ] \
        && cmp -s "$TMP_DIR/login-body.bin" "$TMP_DIR/expect-login.bin"; then
        pass "d TLS POST /v1/login returns 200 + 00 01" "status 200, application/octet-stream, body 00 01"
    else
        fail "d TLS POST /v1/login returns 200 + 00 01" \
            "curl exit $TLS_RC, status '$TLS_CODE', content type '$TLS_CTYPE', body $(od -An -tx1 "$TMP_DIR/login-body.bin" | tr -s ' \n' ' ')"
    fi
fi

# ---- e. register forwarding
if [ -n "$CACERT" ] && [ ! -f "$CACERT" ]; then
    fail "e TLS POST /v1/register returns 400 (forwarded)" "CA root not found: $CACERT"
else
    tls_post /v1/register "$TMP_DIR/register.bin" "$TMP_DIR/register-body.bin"
    if [ "$TLS_RC" -eq 0 ] && [ "$TLS_CODE" = "400" ]; then
        pass "e TLS POST /v1/register returns 400 (forwarded)" "status 400"
    else
        fail "e TLS POST /v1/register returns 400 (forwarded)" "curl exit $TLS_RC, status '$TLS_CODE'"
    fi
fi

# ---- b. gateway log: no new request during (a), with a positive control from (d) and (e)
sleep "$LOG_SETTLE_SECONDS"
LOG_END=$(log_length)
CHECK_B="b gateway log has no Request starting during (a)"
if [ "$LOG_BEFORE" -lt 0 ]; then
    fail "$CHECK_B" "log file not found: $GATEWAY_LOG"
elif [ "$LOG_AFTER_PROBE" -lt "$LOG_BEFORE" ] || [ "$LOG_END" -lt "$LOG_AFTER_PROBE" ]; then
    fail "$CHECK_B" "log file shrank during the run (rotated or truncated?)"
else
    PROBE_COUNT=$(count_requests "$LOG_BEFORE" "$LOG_AFTER_PROBE")
    CONTROL_COUNT=$(count_requests "$LOG_AFTER_PROBE" "$LOG_END")
    DETAIL="$((LOG_AFTER_PROBE - LOG_BEFORE)) log bytes during (a) with $PROBE_COUNT 'Request starting'; $CONTROL_COUNT lines from (d)/(e)"
    if [ "$PROBE_COUNT" -gt 0 ]; then
        fail "$CHECK_B" "$DETAIL"
    elif [ "$CONTROL_COUNT" -eq 0 ]; then
        fail "$CHECK_B" "$DETAIL - request logging not visible (Hosting.Diagnostics level or wrong file)"
    else
        pass "$CHECK_B" "$DETAIL"
    fi
fi

echo ""
if [ "$FAILS" -eq 0 ]; then
    echo "RESULT: all checks PASS"
    exit 0
fi

echo "RESULT: $FAILS check(s) FAIL"
exit 1
