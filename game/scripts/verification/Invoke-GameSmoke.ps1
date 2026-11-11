function Invoke-GameSmoke {
    param(
        [string]$Runtime,
        [string]$ResultFile,
        [hashtable]$Environment,
        [scriptblock]$Prepare,
        [int]$TimeoutSeconds = 70,
        [switch]$WaitForExit,
        [switch]$Visible,
        [switch]$WithSound
    )

    $mutex = [System.Threading.Mutex]::new($false, 'Local\WenmingGameSmoke')
    $ownsMutex = $false
    $process = $null
    try {
        try {
            $ownsMutex = $mutex.WaitOne(0)
        } catch [System.Threading.AbandonedMutexException] {
            $ownsMutex = $true
        }
        if (!$ownsMutex) { throw 'A game smoke test is already running. Run game tests one at a time.' }

        $sessionId = [System.Diagnostics.Process]::GetCurrentProcess().SessionId
        $runningGame = Get-CimInstance Win32_Process -Filter "SessionId = $sessionId AND (Name = 'Survivalcraft.exe' OR Name = 'dotnet.exe')" |
            Where-Object { $_.Name -eq 'Survivalcraft.exe' -or $_.CommandLine -match '(?i)Survivalcraft\.dll(?=["\s]|$)' } |
            Select-Object -First 1
        if ($runningGame) { throw "A game instance is already running (PID $($runningGame.ProcessId)). Close it before starting a smoke test." }

        if (!$Environment) { $Environment = @{} }
        if (!$Environment.ContainsKey('WENMING_TEST_BACKGROUND')) {
            $Environment.WENMING_TEST_BACKGROUND = if ($Visible) { '0' } else { '1' }
        }
        if (!$Environment.ContainsKey('WENMING_TEST_MUTE')) {
            $Environment.WENMING_TEST_MUTE = if ($WithSound) { '0' } else { '1' }
        }
        & $Prepare
        $marker = Join-Path $Runtime $ResultFile
        if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
        $assembly = Join-Path $Runtime 'Survivalcraft.dll'
        $process = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"{0}"' -f $assembly) -WorkingDirectory $Runtime -WindowStyle Hidden -Environment $Environment -PassThru -RedirectStandardOutput (Join-Path $Runtime 'stdout.log') -RedirectStandardError (Join-Path $Runtime 'stderr.log')

        for ($i = 0; $i -lt $TimeoutSeconds -and !(Test-Path -LiteralPath $marker) -and !$process.HasExited; $i++) {
            Start-Sleep -Seconds 1
        }
        if (!(Test-Path -LiteralPath $marker)) { throw "The game did not publish $ResultFile. See $Runtime\stderr.log." }
        $result = Get-Content -Raw -LiteralPath $marker
        Write-Output $result
        if (($result | ConvertFrom-Json).result -ne 'passed') { throw "Game smoke test failed. See $marker." }
        if ($WaitForExit) {
            if (!$process.WaitForExit(30000)) { throw 'The game did not close normally.' }
            if ($process.ExitCode -ne 0) { throw "The game exited with code $($process.ExitCode)." }
        }
    } finally {
        try {
            if ($process) {
                if (!$process.HasExited) {
                    try { $process.Kill() } catch [System.InvalidOperationException] {
                        if (!$process.HasExited) { throw }
                    }
                }
                if (!$process.WaitForExit(5000)) { throw "The test game did not exit (PID $($process.Id))." }
            }
        } finally {
            if ($process) { $process.Dispose() }
            if ($ownsMutex) { $mutex.ReleaseMutex() }
            $mutex.Dispose()
        }
    }
}
