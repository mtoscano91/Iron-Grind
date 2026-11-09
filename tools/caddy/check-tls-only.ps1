<#
.SYNOPSIS
    Story 016 (AC-AUTH-08) TLS-only front check for the dev Caddy + gateway run.
.DESCRIPTION
    Runs five checks against Caddy and prints PASS or FAIL for each:
      a. Plaintext HTTP to 443 is closed within 2 s; the received bytes are nothing, or exactly Go's fixed rejection.
      b. The gateway log gained no "Request starting" line during (a). Reported last: as a positive control,
         (d) and (e) must add such lines, or request logging is not visible and (b) fails.
      c. A TCP connect to port 80 is refused.
      d. TLS POST /v1/login with the fixture returns 200, application/octet-stream, body 00 01.
      e. TLS POST /v1/register with body 00 FF FF returns 400 (proves the body was forwarded to the gateway).
    Exits 0 when all checks pass, 1 otherwise. Uses curl.exe explicitly (Windows 10/11 ship it).
.PARAMETER GatewayLog
    File receiving the gateway's stdout. The gateway must run with
    Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics=Information.
.PARAMETER HostName
    Host used for every check. Default: localhost.
.PARAMETER CaCert
    Root certificate that signed Caddy's tls internal certificate. Default: Caddy's local root on Windows.
.PARAMETER NoRevocationCheck
    Adds --ssl-no-revoke to curl.exe. Applied automatically when HostName is localhost (dev only).
.PARAMETER Fixture
    ConnectionRequest body for the login check. Default: tools\caddy\fixtures\login-unknown-user.bin.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\caddy\check-tls-only.ps1 -GatewayLog "$env:LOCALAPPDATA\IronGrind\gateway-check.log"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GatewayLog,

    [string]$HostName = 'localhost',

    [string]$CaCert = (Join-Path $env:APPDATA 'Caddy\pki\authorities\local\root.crt'),

    # Dev only. Windows' curl.exe (schannel) fails the revocation check against Caddy's local CA, which publishes
    # no CRL or OCSP. Passing this switch adds --ssl-no-revoke to curl.exe. It is also applied automatically when
    # HostName is localhost. Never use it against a public host.
    [switch]$NoRevocationCheck,

    # Default resolved below: Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param() defaults.
    [string]$Fixture = ''
)

if ([string]::IsNullOrEmpty($Fixture)) {
    $Fixture = Join-Path $PSScriptRoot 'fixtures\login-unknown-user.bin'
}

# Go net/http's fixed reply to plaintext HTTP sent to a TLS port (exact bytes, LF at the end).
$GoRejection = "HTTP/1.0 400 Bad Request`r`n`r`nClient sent an HTTP request to an HTTPS server.`n"
$PlaintextRequest = "POST /v1/login HTTP/1.1`r`nHost: $HostName`r`nContent-Type: application/octet-stream`r`nContent-Length: 0`r`n`r`n"
$ProbeTimeoutMs = 2000
# TCP connect timeout (not the AC-AUTH-08 EOF window): Windows retries a refused SYN to a local port for about 2 s per address, and localhost resolves to ::1 and 127.0.0.1.
$ConnectTimeoutMs = 6000
# Wait before reading the gateway log: its console logger writes asynchronously.
$LogSettleMs = 200
$skipRevocation = $NoRevocationCheck.IsPresent -or ($HostName -eq 'localhost')
# @(...) around the if: PowerShell unrolls a one-element array to a string, and splatting a string passes one character per argument.
[string[]]$revocationArgs = @(if ($skipRevocation) { '--ssl-no-revoke' })
$script:FailCount = 0

function Write-Check {
    param([string]$Name, [bool]$Pass, [string]$Detail)
    $tag = if ($Pass) { 'PASS' } else { 'FAIL' }
    if (-not $Pass) { $script:FailCount++ }
    Write-Host ("[{0}] {1} - {2}" -f $tag, $Name, $Detail)
}

function Format-Text {
    param([byte[]]$Bytes)
    if ($Bytes.Length -eq 0) { return '(empty)' }
    return ([System.Text.Encoding]::ASCII.GetString($Bytes) -replace "`r", '\r' -replace "`n", '\n')
}

function Format-ByteHex {
    param([byte[]]$Bytes)
    if ($Bytes.Length -eq 0) { return '(empty)' }
    return [BitConverter]::ToString($Bytes)
}

function Get-SocketErrorCode {
    param($Exception)
    $ex = $Exception
    while ($null -ne $ex) {
        if ($ex -is [System.Net.Sockets.SocketException]) { return $ex.SocketErrorCode.ToString() }
        $ex = $ex.InnerException
    }
    return ''
}

function Invoke-PlaintextProbe {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $connect = $client.BeginConnect($HostName, 443, $null, $null)
        if (-not $connect.AsyncWaitHandle.WaitOne($ConnectTimeoutMs)) {
            return [pscustomobject]@{ Connected = $false; Closed = $false; Bytes = [byte[]]@(); Detail = 'connect to 443 timed out after 6 s' }
        }
        $client.EndConnect($connect)

        $stream = $client.GetStream()
        $request = [System.Text.Encoding]::ASCII.GetBytes($PlaintextRequest)
        try {
            $stream.Write($request, 0, $request.Length)
            $stream.Flush()
        }
        catch [System.IO.IOException] {
            # Caddy closed before reading the whole request: closed with no bytes, which AC-AUTH-08 allows.
            return [pscustomobject]@{ Connected = $true; Closed = $true; Bytes = [byte[]]@(); Detail = 'closed before the request was written' }
        }

        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $buffer = New-Object byte[] 4096
        $received = New-Object System.IO.MemoryStream
        $closed = $false
        while ($true) {
            $remaining = $ProbeTimeoutMs - [int]$clock.ElapsedMilliseconds
            if ($remaining -le 0) { break }
            $stream.ReadTimeout = $remaining
            try {
                $n = $stream.Read($buffer, 0, $buffer.Length)
            }
            catch [System.IO.IOException] {
                break
            }
            if ($n -eq 0) {
                $closed = $true
                break
            }
            $received.Write($buffer, 0, $n)
        }

        return [pscustomobject]@{ Connected = $true; Closed = $closed; Bytes = $received.ToArray(); Detail = '' }
    }
    catch {
        return [pscustomobject]@{ Connected = $false; Closed = $false; Bytes = [byte[]]@(); Detail = $_.Exception.Message }
    }
    finally {
        $client.Close()
    }
}

function Invoke-TlsPost {
    param([string]$Path, [string]$BodyFile)
    $out = [System.IO.Path]::GetTempFileName()
    try {
        $meta = & curl.exe --silent --show-error @revocationArgs --cacert $CaCert -o $out -w '%{http_code} %{content_type}' -X POST --data-binary "@$BodyFile" -H 'Content-Type: application/octet-stream' "https://$HostName$Path"
        $curlExit = $LASTEXITCODE
        $parts = ([string]$meta).Trim() -split ' ', 2
        $code = $parts[0]
        $contentType = if ($parts.Count -gt 1) { $parts[1] } else { '' }
        return [pscustomobject]@{
            Exit        = $curlExit
            Code        = $code
            ContentType = $contentType
            Body        = [System.IO.File]::ReadAllBytes($out)
        }
    }
    finally {
        Remove-Item -LiteralPath $out -ErrorAction SilentlyContinue
    }
}

function Get-LogLength {
    if (Test-Path -LiteralPath $GatewayLog -PathType Leaf) { return (Get-Item -LiteralPath $GatewayLog).Length }
    return -1
}

# Text appended to the gateway log from byte $From to $To. The log must be UTF-8 or ASCII (README section 7):
# Windows PowerShell 5.1's `>` writes UTF-16, in which 'Request starting' would never match.
function Read-LogRange {
    param([long]$From, [long]$To)
    $stream = New-Object System.IO.FileStream($GatewayLog, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $stream.Position = $From
        $bytes = New-Object byte[] ([int]($To - $From))
        $read = 0
        while ($read -lt $bytes.Length) {
            $n = $stream.Read($bytes, $read, $bytes.Length - $read)
            if ($n -eq 0) { break }
            $read += $n
        }
        return [System.Text.Encoding]::UTF8.GetString($bytes, 0, $read)
    }
    finally {
        $stream.Dispose()
    }
}

Write-Host "check-tls-only: host=$HostName gateway-log=$GatewayLog"
Write-Host ("curl.exe revocation check: {0}" -f $(if ($skipRevocation) { 'skipped (--ssl-no-revoke; dev only)' } else { 'enabled' }))
Write-Host ''

# Record the gateway log length before the plaintext probe (check b compares against it).
$logBefore = Get-LogLength

# ---- a. plaintext probe
$probe = Invoke-PlaintextProbe
if (-not $probe.Connected) {
    Write-Check 'a plaintext to 443' $false $probe.Detail
}
elseif (-not $probe.Closed) {
    Write-Check 'a plaintext to 443' $false 'no EOF within 2 s (connection left open or reset)'
}
else {
    $isEmpty = $probe.Bytes.Length -eq 0
    $isGoRejection = ([System.Text.Encoding]::ASCII.GetString($probe.Bytes) -ceq $GoRejection)
    Write-Check 'a plaintext to 443 closed within 2 s with nothing or Go rejection' ($isEmpty -or $isGoRejection) `
        "received $($probe.Bytes.Length) bytes"
}
Write-Host "  plaintext probe received (text): $(Format-Text $probe.Bytes)"
Write-Host "  plaintext probe received (hex):  $(Format-ByteHex $probe.Bytes)"

# Console logging is asynchronous: give the gateway time to flush anything the probe caused before measuring.
Start-Sleep -Milliseconds $LogSettleMs
$logAfterProbe = Get-LogLength
# Check b is reported after (e): its positive control needs the Request starting lines that (d) and (e) produce.

# ---- c. port 80 refused
$client = New-Object System.Net.Sockets.TcpClient
try {
    $connect = $client.BeginConnect($HostName, 80, $null, $null)
    if (-not $connect.AsyncWaitHandle.WaitOne($ConnectTimeoutMs)) {
        Write-Check 'c TCP connect to port 80 refused' $false 'timed out after 6 s (expected a refused connection)'
    }
    else {
        try {
            $client.EndConnect($connect)
            Write-Check 'c TCP connect to port 80 refused' $false 'connection SUCCEEDED: something listens on port 80'
        }
        catch {
            $code = Get-SocketErrorCode $_.Exception
            if ($code -eq 'ConnectionRefused') {
                Write-Check 'c TCP connect to port 80 refused' $true 'ConnectionRefused'
            }
            else {
                Write-Check 'c TCP connect to port 80 refused' $false ("expected ConnectionRefused, got '{0}': {1}" -f $code, $_.Exception.Message)
            }
        }
    }
}
finally {
    $client.Close()
}

# ---- d. TLS login through Caddy
if (-not (Test-Path -LiteralPath $CaCert -PathType Leaf)) {
    Write-Check 'd TLS POST /v1/login returns 200 + 00 01' $false "CA root not found: $CaCert (run caddy trust, or pass -CaCert)"
}
elseif (-not (Test-Path -LiteralPath $Fixture -PathType Leaf)) {
    Write-Check 'd TLS POST /v1/login returns 200 + 00 01' $false "fixture not found: $Fixture"
}
else {
    $login = Invoke-TlsPost -Path '/v1/login' -BodyFile $Fixture
    $bodyOk = ($login.Body.Length -eq 2 -and $login.Body[0] -eq 0 -and $login.Body[1] -eq 1)
    $pass = ($login.Exit -eq 0 -and $login.Code -eq '200' -and $login.ContentType -ceq 'application/octet-stream' -and $bodyOk)
    Write-Check 'd TLS POST /v1/login returns 200 + 00 01' $pass `
        ("curl exit {0}, status {1}, content type '{2}', body {3}" -f $login.Exit, $login.Code, $login.ContentType, (Format-ByteHex $login.Body))
}

# ---- e. register forwarding (malformed body must reach the gateway and get its 400)
$registerFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllBytes($registerFile, [byte[]](0x00, 0xFF, 0xFF))
    if (-not (Test-Path -LiteralPath $CaCert -PathType Leaf)) {
        Write-Check 'e TLS POST /v1/register returns 400 (forwarded)' $false "CA root not found: $CaCert"
    }
    else {
        $register = Invoke-TlsPost -Path '/v1/register' -BodyFile $registerFile
        $pass = ($register.Exit -eq 0 -and $register.Code -eq '400')
        Write-Check 'e TLS POST /v1/register returns 400 (forwarded)' $pass `
            ("curl exit {0}, status {1}, body {2}" -f $register.Exit, $register.Code, (Format-ByteHex $register.Body))
    }
}
finally {
    Remove-Item -LiteralPath $registerFile -ErrorAction SilentlyContinue
}

# ---- b. gateway log: no new request during (a), with a positive control from (d) and (e)
Start-Sleep -Milliseconds $LogSettleMs
$logEnd = Get-LogLength
$checkB = 'b gateway log has no Request starting during (a)'
if ($logBefore -lt 0) {
    Write-Check $checkB $false "log file not found: $GatewayLog"
}
elseif ($logAfterProbe -lt $logBefore -or $logEnd -lt $logAfterProbe) {
    Write-Check $checkB $false 'log file shrank during the run (rotated or truncated?)'
}
else {
    $probeText = Read-LogRange -From $logBefore -To $logAfterProbe
    $controlText = Read-LogRange -From $logAfterProbe -To $logEnd
    $probeCount = ([regex]::Matches($probeText, 'Request starting')).Count
    $controlCount = ([regex]::Matches($controlText, 'Request starting')).Count
    $detail = "{0} log bytes during (a) with {1} 'Request starting'; {2} lines from (d)/(e)" -f ($logAfterProbe - $logBefore), $probeCount, $controlCount
    if ($probeCount -gt 0) {
        Write-Check $checkB $false $detail
    }
    elseif ($controlCount -eq 0) {
        Write-Check $checkB $false "$detail - request logging not visible (Hosting.Diagnostics level, wrong file, or a UTF-16 log)"
    }
    else {
        Write-Check $checkB $true $detail
    }
}

Write-Host ''
if ($script:FailCount -eq 0) {
    Write-Host 'RESULT: all checks PASS'
    exit 0
}

Write-Host "RESULT: $($script:FailCount) check(s) FAIL"
exit 1
