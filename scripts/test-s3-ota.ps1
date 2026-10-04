param(
    [Parameter(Mandatory=$true)][switch]$BareBoard,
    [string]$Port = 'COM7',
    [ValidateSet('Check','Update','Rollback')][string]$Mode = 'Check',
    [string]$BoardAddress = '192.168.3.62'
)
$ErrorActionPreference = 'Stop'
if (!$BareBoard) { throw 'Only run with ESC and all peripherals disconnected.' }
$root = Split-Path -Parent $PSScriptRoot
$serial = [IO.Ports.SerialPort]::new($Port,115200)
$serial.ReadTimeout = 1000
try {
    $serial.Open(); $serial.DiscardInBuffer(); $serial.WriteLine('I')
    $deadline = [DateTime]::UtcNow.AddSeconds(5); $key = ''
    while ([DateTime]::UtcNow -lt $deadline -and !$key) {
        try { $line = $serial.ReadLine().Trim(); if ($line.StartsWith('WIFIKEY|')) { $key = $line.Substring(8) } } catch [TimeoutException] { }
    }
    if (!$key) { throw 'No local board credential returned.' }
} finally { $serial.Dispose() }
$base = "http://$BoardAddress"
$headers = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("proptest:$key")); 'X-Proptest' = '1' }
$key = $null
function Api([string]$Path, $Body = $null) {
    if ($null -eq $Body) { return Invoke-RestMethod "$base$Path" -Headers $headers -TimeoutSec 4 }
    return Invoke-RestMethod "$base$Path" -Method Post -Body $Body -Headers $headers -TimeoutSec 4
}
function Require($Condition, [string]$Text) { if (!$Condition) { throw $Text } }
function Upload([byte[]]$Bytes) {
    $http = [Net.Http.HttpClient]::new(); $http.Timeout = [TimeSpan]::FromSeconds(90)
    $body = [Net.Http.MultipartFormDataContent]::new()
    try {
        foreach ($h in $headers.GetEnumerator()) { $http.DefaultRequestHeaders.Add($h.Key,$h.Value) }
        $body.Add([Net.Http.ByteArrayContent]::new($Bytes),'firmware','test.ptfw')
        $result = $http.PostAsync("$base/update",$body).GetAwaiter().GetResult()
        return @{ Code=[int]$result.StatusCode; Text=$result.Content.ReadAsStringAsync().GetAwaiter().GetResult() }
    } finally { $body.Dispose(); $http.Dispose() }
}
function WaitBoard {
    for ($n=0;$n -lt 20;$n++) { Start-Sleep -Seconds 1; try { return Api '/status' } catch { } }
    throw 'Board did not reconnect.'
}
$report = [Collections.Generic.List[string]]::new()
try {
    $s = Api '/status'; if ($s.pending) { $s = Api '/confirm' @{} }
    Require ($s.pin -eq 14 -and $s.max -eq 100 -and !$s.armed -and $s.applied -eq 0 -and $s.ready) 'Incorrect startup state.'
    $report.Add('Boot: GPIO14, cap10, zero, ready')
    $package = [IO.File]::ReadAllBytes((Join-Path $root 'artifacts/firmware-s3-030/PROpTEST-S3-0.3.0.ptfw'))
    if ($Mode -eq 'Check') {
        try { Invoke-RestMethod "$base/status" -TimeoutSec 4 | Out-Null; throw 'Authentication missing.' } catch { if ($_.Exception.Response.StatusCode -ne 401) { throw } }
        $s = Api '/arm' @{}; $token = $s.token
        Require ($s.armed -and $s.applied -eq 0 -and $token) 'Arm failed.'
        $s = Api '/gas' @{ token=$token; value='100' }
        for ($n=0;$n -lt 25;$n++) { Start-Sleep -Milliseconds 100; $s=Api '/lease' @{token=$token}; Require ($s.applied -le 100) 'Exceeded10%.' }
        Require ($s.applied -eq 100) 'PWM ramp did not reach10%.'
        $report.Add('Ramp: reaches10% and never exceeds cap')
        $r = Upload ([Text.Encoding]::ASCII.GetBytes('blocked-while-armed')); Require ($r.Code -eq 400 -and $r.Text.Contains('зупиніть')) 'OTA allowed while armed.'
        Start-Sleep -Milliseconds 1300; $s=Api '/status'
        Require (!$s.armed -and $s.applied -eq 0) 'Lease failed to stop.'
        $s=Api '/lease' @{token=$token}; Require (!$s.armed) 'Stale lease revived motor.'
        $report.Add('Lease expires; stale token cannot restart; OTA blocked while armed')
        $s=Api '/arm' @{}; $token=$s.token
        try { Api '/gas' @{token=$token; value='101'} | Out-Null; throw 'Overlimit accepted.' } catch { if ($_.Exception.Response.StatusCode -ne 400) { throw } }
        $s=Api '/status'; Require (!$s.armed -and $s.applied -eq 0) 'Bad command did not stop.'
        $s=Api '/arm' @{}; $token=$s.token; $null=Api '/gas' @{token=$token;value='60'}
        # A deliberately stalled HTTP request must not block the independent stop task.
        $socket=[Net.Sockets.TcpClient]::new($BoardAddress,80)
        try { $bytes=[Text.Encoding]::ASCII.GetBytes("GET /status HTTP/1.1`r`nHost: $BoardAddress`r`n"); $socket.GetStream().Write($bytes); Start-Sleep -Milliseconds 1600 } finally { $socket.Dispose() }
        $s=Api '/status'; Require (!$s.armed -and $s.applied -eq 0) 'HTTP stall blocked lease stop.'
        $report.Add('Overlimit disarms; stalled HTTP cannot keep PWM running')
        $s=Api '/arm' @{}; $token=$s.token
        $until=[DateTime]::UtcNow.AddSeconds(31)
        while ([DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 200; $s=Api '/lease' @{token=$token} }
        Require (!$s.armed -and $s.reason -eq 'TIME_LIMIT') '30-second cutoff missing.'
        $report.Add('30-second timeout at zero with continuous lease')
        $r=Upload ([Text.Encoding]::ASCII.GetBytes('not firmware')); Require ($r.Code -eq 400) 'Invalid package accepted.'
        $bad=[byte[]]$package.Clone(); $bad[$bad.Length-50]=$bad[$bad.Length-50] -bxor 1
        $r=Upload $bad; Require ($r.Code -eq 400 -and $r.Text.Contains('SHA-256')) 'Corrupted SHA256 not rejected by hash check.'
        $s=Api '/status'; Require (!$s.armed -and !$s.pending) 'Bad update changed state.'
        $report.Add('Invalid and corrupted OTA rejected; current firmware still running')
    } else {
        $before=$s.slot
        $r=Upload $package; Require ($r.Code -eq 200) "OTA failed: $($r.Text)"
        Start-Sleep -Seconds 2; $s=WaitBoard
        Require ($s.pending -and $s.slot -ne $before -and !$s.armed -and $s.applied -eq 0) 'New slot not pending at zero.'
        if ($Mode -eq 'Update') {
            $s=Api '/confirm' @{}; Require (!$s.pending) 'Confirmation failed.'
            $report.Add("OTA actual Wi-Fi upload: $before -> $($s.slot), confirmed, zero")
        } else {
            Write-Output 'New image pending; waiting for automatic rollback without confirmation.'
            Start-Sleep -Seconds 55
            Start-Sleep -Seconds 8
            $s=WaitBoard
            Require ($s.slot -eq $before -and !$s.pending -and !$s.armed -and $s.applied -eq 0) 'Rollback failed.'
            $report.Add('Unconfirmed image automatically rolled back after60seconds')
        }
    }
} finally { try { $null=Api '/stop' @{} } catch { }; $headers.Clear() }
$report | ForEach-Object { Write-Output $_ }
$report | ConvertTo-Json | Set-Content (Join-Path $root "artifacts/hardware-diagnostics/s3-030-$Mode.json") -Encoding utf8
