param([string]$Port)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $cli = 'artifacts/hardware-diagnostics/tools/arduino-cli/arduino-cli.exe'
    $config = 'artifacts/hardware-diagnostics/arduino/config.json'
    $board = 'esp32:esp32:esp32s3:CDCOnBoot=cdc,USBMode=hwcdc,FlashSize=16M,PSRAM=disabled'
    $output = 'artifacts/firmware-s3-030'
    & $cli compile --fqbn $board --config-file $config --output-dir $output firmware/PropTestS3
    if ($LASTEXITCODE -ne 0) { throw 'S3 compile failed.' }
    $image = [IO.File]::ReadAllBytes((Join-Path $projectRoot "$output/PropTestS3.ino.bin"))
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { $sha = [BitConverter]::ToString($hasher.ComputeHash($image)).Replace('-','').ToLowerInvariant() }
    finally { $hasher.Dispose() }
    $package = [IO.MemoryStream]::new()
    try {
        $magic = [Text.Encoding]::ASCII.GetBytes('PROPTEST-S3-OTA1')
        $length = [BitConverter]::GetBytes([uint32]$image.Length)
        $digest = [Text.Encoding]::ASCII.GetBytes($sha)
        $package.Write($magic,0,16)
        $package.Write($length,0,4)
        $package.Write($digest,0,64)
        $package.Write($image,0,$image.Length)
        [IO.File]::WriteAllBytes((Join-Path $projectRoot "$output/PROpTEST-S3-0.3.0.ptfw"), $package.ToArray())
    } finally { $package.Dispose() }
    if ($Port) {
        & $cli upload --port $Port --fqbn $board --config-file $config --input-dir $output firmware/PropTestS3
        if ($LASTEXITCODE -ne 0) { throw 'S3 upload failed.' }
    }
} finally { Pop-Location }
