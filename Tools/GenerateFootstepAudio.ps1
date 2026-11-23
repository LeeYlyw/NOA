# Deterministic boot steps for the hard laboratory floor. No third-party audio is used.
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$output = Join-Path $project 'Assets\Resources\Audio\Footsteps'
if (-not (Test-Path -LiteralPath $output)) { New-Item -ItemType Directory -Path $output | Out-Null }
$sampleRate = 48000
$sampleCount = [int]($sampleRate * 0.25)

for ($variant = 0; $variant -lt 4; $variant++) {
    $random = [Random]::new(7214 + $variant * 137)
    $samples = [double[]]::new($sampleCount)
    $lowNoise = 0.0
    $peak = 0.0
    for ($i = 0; $i -lt $sampleCount; $i++) {
        $t = $i / [double]$sampleRate
        $noise = $random.NextDouble() * 2.0 - 1.0
        $lowNoise = 0.89 * $lowNoise + 0.11 * $noise
        $pitch = 82.0 + $variant * 7.0
        $thud = 0.58 * [Math]::Sin(2.0 * [Math]::PI * ($pitch * $t + 48.0 * $t * $t)) *
            [Math]::Exp(-27.0 * $t)
        $strike = 0.38 * $noise * [Math]::Exp(-95.0 * $t)
        $body = 0.18 * $lowNoise * [Math]::Exp(-17.0 * $t)
        $scuffTime = $t - 0.035
        $scuff = if ($scuffTime -gt 0.0) {
            0.13 * $noise * (1.0 - [Math]::Exp(-90.0 * $scuffTime)) *
                [Math]::Exp(-27.0 * $scuffTime)
        } else { 0.0 }
        $value = ($thud + $strike + $body + $scuff) *
            [Math]::Min(1.0, $t / 0.002)
        $samples[$i] = $value
        $peak = [Math]::Max($peak, [Math]::Abs($value))
    }

    $path = Join-Path $output ('Step_{0:00}.wav' -f ($variant + 1))
    $stream = [IO.File]::Create($path)
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        $dataBytes = $sampleCount * 2
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
        foreach ($value in $samples) {
            $scaled = [Math]::Max(-1.0, [Math]::Min(1.0, $value * 0.76 / $peak))
            $writer.Write([Int16][Math]::Round($scaled * 32767.0))
        }
        $writer.Flush()
    } finally {
        $stream.Dispose()
    }
    Write-Host "Created $path"
}
