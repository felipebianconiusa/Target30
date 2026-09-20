# Gera o build de produção do Angular e copia pra src/Target30.Api/wwwroot, de onde a própria API
# serve o app (um processo, uma origem). Depois: `dotnet run --launch-profile https` em
# src/Target30.Api e abra https://localhost:7059.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $root 'frontend'
$wwwroot = Join-Path $root 'src/Target30.Api/wwwroot'

Push-Location $frontend
try { npx ng build --configuration production } finally { Pop-Location }

$dist = Join-Path $frontend 'dist/target30-web/browser'
if (-not (Test-Path (Join-Path $dist 'index.html'))) { throw "Build não encontrado em $dist" }

# wwwroot só contém o build copiado por este script (fica no .gitignore): recria do zero.
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item (Join-Path $dist '*') $wwwroot -Recurse
Write-Host "Frontend copiado para $wwwroot"
