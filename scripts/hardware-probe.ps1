param([string]$Port = 'COM5', [ValidateRange(0,30)][int]$Maximum = 0)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $root 'artifacts/windows-v06/System.IO.Ports.dll')
$serial = [System.IO.Ports.SerialPort]::new($Port,115200)
$serial.NewLine = "`n"; $serial.WriteTimeout = 200
$log = [System.Text.StringBuilder]::new()
$pending = ''
function Receive-Lines {
    $script:pending += $serial.ReadExisting()
    while ($script:pending.Contains("`n")) {
        $index = $script:pending.IndexOf("`n")
        $line = $script:pending.Substring(0,$index).Trim()
        $script:pending = $script:pending.Substring($index+1)
        [void]$log.AppendLine($line)
        if ($line.StartsWith('FAULT:') -or $line.StartsWith('ERR:')) { throw "Board rejected operation: $line" }
        if ($line.StartsWith('D|')) {
            $parts = $line.Split('|')
            if ([int]$parts[3] -gt $Maximum) { throw 'Unexpected output exceeds probe limit' }
        }
    }
}
try {
    $serial.Open(); Start-Sleep -Milliseconds 3500
    $serial.DiscardInBuffer(); $serial.WriteLine(''); Start-Sleep -Milliseconds 100
    $serial.DiscardInBuffer(); $serial.WriteLine('P'); Start-Sleep -Milliseconds 200
    $reply = $serial.ReadExisting(); [void]$log.AppendLine($reply)
    if (!$reply.Contains('PT:2.0') -and !$reply.Contains('PT:2.1')) { throw 'No PT:2.x identity' }
    $serial.WriteLine('G'); Start-Sleep -Milliseconds 150
    $reply=$serial.ReadExisting(); [void]$log.AppendLine($reply)
    if (!$reply.Contains('ACK:G:0')) { throw 'No acknowledged zero output' }
    $serial.WriteLine('M:30'); $serial.WriteLine('F:200'); $serial.WriteLine('B')
    Start-Sleep -Milliseconds 150; Receive-Lines
    $targets = @(0)
    for ($step=5; $step -le $Maximum; $step+=5) { $targets += $step }
    foreach($target in $targets) {
        [void]$log.AppendLine("TX W:$target")
        $serial.WriteLine("W:$target")
        $until = [Environment]::TickCount64 + 2000
        while([Environment]::TickCount64 -lt $until) {
            $serial.WriteLine('H'); Start-Sleep -Milliseconds 150; Receive-Lines
        }
    }
} finally {
    if($serial.IsOpen) {
        $serial.WriteLine('G'); Start-Sleep -Milliseconds 150
        [void]$log.AppendLine($serial.ReadExisting())
    }
    $serial.Dispose()
    $path = Join-Path $root ("artifacts/hardware-diagnostics/probe-{0}-{1}.log" -f $Maximum,(Get-Date -Format 'yyyyMMdd-HHmmss'))
    $log.ToString() | Set-Content $path
    Write-Output $path
}

