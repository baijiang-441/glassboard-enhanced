param([switch]$Test)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build source/GlassBoard.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed' }
    Copy-Item -LiteralPath source/bin/Release/net48/GlassBoard.exe -Destination portable/GlassBoard.exe -Force
    if ($Test) {
        dotnet build tests/Tests.csproj -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
        # Shell extensions may launch independent helpers. Wait for the test process itself,
        # rather than Start-Process -Wait's entire descendant process tree.
        $checks = Start-Process -FilePath (Join-Path $PSScriptRoot 'tests/bin/Release/net48/Tests.exe') -NoNewWindow -PassThru
        $checks.WaitForExit()
        if ($checks.ExitCode -ne 0) { throw 'Regression checks failed' }
    }
} finally { Pop-Location }
