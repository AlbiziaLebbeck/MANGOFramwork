# Binary-safe transport for an already authorized isolated restore. Never write captured SQL or stderr to the chat/console.
function Invoke-RecoverySsh {
    param([Parameter(Mandatory)][string]$Command, [string]$InputFile, [string]$OutputFile, [Parameter(Mandatory)][string]$LogStem)
    $start = [Diagnostics.ProcessStartInfo]::new('ssh.exe')
    foreach ($argument in @('-T','-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02',$Command)) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true; $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $process = [Diagnostics.Process]::new(); $process.StartInfo=$start
    $inputStream=$null; $outputStream=$null; $startedProcess=$false
    try {
        if ($OutputFile) { $outputStream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None) }
        else { $outputStream=[IO.MemoryStream]::new() }
        if (-not $process.Start()) { throw 'Cannot start the owned recovery SSH process.' }
        $startedProcess=$true
        $outputTask=$process.StandardOutput.BaseStream.CopyToAsync($outputStream)
        $errorTask=$process.StandardError.ReadToEndAsync()
        $inputTask=$null; $closed=$false
        if ($InputFile) { $inputStream=[IO.File]::OpenRead($InputFile); $inputTask=$inputStream.CopyToAsync($process.StandardInput.BaseStream) }
        else { $process.StandardInput.Close(); $closed=$true }
        $deadline=[DateTime]::UtcNow.AddMinutes(10)
        while (-not $process.HasExited) {
            if (-not $closed -and $inputTask.IsCompleted) {
                try { [void]$inputTask.GetAwaiter().GetResult() } catch { }
                $process.StandardInput.Close(); $closed=$true
            }
            if ([DateTime]::UtcNow -gt $deadline) { $process.Kill($true); throw 'Isolated recovery command timed out; inspect private logs and preserve the failed drill namespace.' }
            Start-Sleep -Milliseconds 100
        }
        $process.WaitForExit()
        [void]$outputTask.GetAwaiter().GetResult()
        $errors=$errorTask.GetAwaiter().GetResult()
        [IO.File]::WriteAllText($LogStem+'.stderr',$errors,[Text.UTF8Encoding]::new($false))
        if ($process.ExitCode -ne 0 -or ($null -ne $inputTask -and $inputTask.IsFaulted)) { throw 'Isolated recovery command failed. Error details remain in the restricted recovery log, not console output.' }
        if (-not $OutputFile) { return [Text.Encoding]::UTF8.GetString($outputStream.ToArray()) }
    } finally {
        if ($null -ne $inputStream) { $inputStream.Dispose() }
        if ($null -ne $outputStream) { $outputStream.Dispose() }
        if ($startedProcess -and -not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
}
