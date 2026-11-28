$ErrorActionPreference = 'Stop'

Push-Location -LiteralPath $PSScriptRoot
try {
    dotnet build -c Release battle-worker/BattleWorker.csproj
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    go run . @args
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
