# Builds a longer sliding-door cue from the project's existing door clunks.
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $project 'Assets\Map\design\door'
$target = Join-Path $project 'Assets\Resources\Audio\EscapeDoor_HeavySlide.wav'
$sampleRate = 48000

function Read-MonoPcm16($path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    if ([Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'RIFF' -or
        [BitConverter]::ToInt16($bytes, 20) -ne 1 -or
        [BitConverter]::ToInt16($bytes, 22) -ne 1 -or
        [BitConverter]::ToInt32($bytes, 24) -ne $sampleRate -or
        [BitConverter]::ToInt16($bytes, 34) -ne 16 -or
        [Text.Encoding]::ASCII.GetString($bytes, 36, 4) -ne 'data') {
        throw "Expected 48 kHz mono PCM WAV: $path"
    }
    $samples = [Int16[]]::new(($bytes.Length - 44) / 2)
    [Buffer]::BlockCopy($bytes, 44, $samples, 0, $bytes.Length - 44)
    return ,$samples
}

$start = Read-MonoPcm16 (Join-Path $sourceDir 'Door_Open_Tuk.wav')
$stop = Read-MonoPcm16 (Join-Path $sourceDir 'Door_Close_Teok.wav')
$count = [int]($sampleRate * 0.94)
$mix = [double[]]::new($count)
$random = [Random]::new(2601)
$lowNoise = 0.0
$peak = 0.0
for ($i = 0; $i -lt $count; $i++) {
    $t = $i / [double]$sampleRate
    $noise = 2.0 * $random.NextDouble() - 1.0
    $lowNoise = 0.992 * $lowNoise + 0.008 * $noise
    $slideEnvelope = [Math]::Max(0.0, [Math]::Min(1.0,
        [Math]::Min(($t - 0.025) / 0.10, (0.78 - $t) / 0.13)))
    $rumble = (1.8 * $lowNoise + 0.075 * [Math]::Sin(2 * [Math]::PI * 73 * $t) +
        0.042 * [Math]::Sin(2 * [Math]::PI * 116 * $t) + 0.038 * $noise) * $slideEnvelope
    $scrape = (0.032 * $noise + 0.026 * [Math]::Sin(2 * [Math]::PI * (245 - 65 * $t) * $t)) * $slideEnvelope
    $value = $rumble + $scrape

    $startIndex = [int]($t * $sampleRate * 0.76)
    if ($startIndex -ge 0 -and $startIndex -lt $start.Length) {
        $value += 1.55 * $start[$startIndex] / 32768.0
    }
    $stopIndex = [int](($t - 0.54) * $sampleRate * 0.74)
    if ($stopIndex -ge 0 -and $stopIndex -lt $stop.Length) {
        $value += 1.4 * $stop[$stopIndex] / 32768.0
    }
    $mix[$i] = $value
    $peak = [Math]::Max($peak, [Math]::Abs($value))
}

$directory = Split-Path -Parent $target
if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
$stream = [IO.File]::Create($target)
try {
    $writer = [IO.BinaryWriter]::new($stream)
    $dataBytes = $count * 2
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
    $writer.Write([int](36 + $dataBytes))
    $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([int]16)
    $writer.Write([Int16]1)
    $writer.Write([Int16]1)
    $writer.Write([int]$sampleRate)
    $writer.Write([int]($sampleRate * 2))
    $writer.Write([Int16]2)
    $writer.Write([Int16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
    $writer.Write([int]$dataBytes)
    foreach ($value in $mix) {
        $writer.Write([Int16][Math]::Round([Math]::Max(-1.0, [Math]::Min(1.0, $value * 0.87 / $peak)) * 32767))
    }
    $writer.Flush()
} finally {
    $stream.Dispose()
}
Write-Host "Created $target"
