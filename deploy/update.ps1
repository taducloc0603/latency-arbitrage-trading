param(
  # Version comes from the GitHub Actions run number: tag = v$Version, e.g. v1.0.34
  [string]$Version = "1.0.34",

  # Optional Supabase credentials. If provided, they are persisted as User env
  # vars AND set for the launched process so the app can Load Config immediately.
  [string]$SupabaseUrl = "",
  [string]$SupabaseAnonKey = ""
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repo     = "taducloc0603/latency-arbitrage-trading"
$assetName = "LatencyArbTool.App"   # exe + zip base name (matches CI APP_NAME)
$procName  = "LatencyArbTool.App"   # process name = exe without .exe
$exeName   = "LatencyArbTool.App.exe"
$dllName   = "mt5engine-capi.dll"   # NOTE: hyphen, not underscore

$tag = "v$Version"
$zip = "$assetName-$Version-portable-win-x64.zip"
$url = "https://github.com/$repo/releases/download/$tag/$zip"

$base = "C:\LatencyArbTool"
$app  = Join-Path $base "app"
$pkg  = Join-Path $base $zip
$bak  = Join-Path $base ("backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))

Write-Host "=== Update LatencyArbTool $Version ===" -ForegroundColor Cyan

# 1) Stop app if running
Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force

# 2) Prepare folder
New-Item -ItemType Directory -Path $base -Force | Out-Null

# 3) Backup old app
if (Test-Path $app) {
  New-Item -ItemType Directory -Path $bak -Force | Out-Null
  Copy-Item "$app\*" $bak -Recurse -Force
  Write-Host "Backup created: $bak" -ForegroundColor Yellow
}

# 4) Download release zip
Write-Host "Downloading: $url"
Invoke-WebRequest -Uri $url -OutFile $pkg

# 5) Replace app folder
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Expand-Archive -Path $pkg -DestinationPath $app -Force

# 6) Validate required files
$exePath = Join-Path $app $exeName
$dllPath = Join-Path $app $dllName

$exeOk = Test-Path $exePath
$dllOk = Test-Path $dllPath

if (-not $exeOk -or -not $dllOk) {
  throw "Missing required files after extract. exe=$exeOk, $dllName=$dllOk"
}

# 7) Persist Supabase credentials (optional)
if ($SupabaseUrl -ne "" -and $SupabaseAnonKey -ne "") {
  [Environment]::SetEnvironmentVariable("SUPABASE_URL", $SupabaseUrl, "User")
  [Environment]::SetEnvironmentVariable("SUPABASE_ANON_KEY", $SupabaseAnonKey, "User")
  # Also set on the current process so the app started below inherits them now.
  $env:SUPABASE_URL = $SupabaseUrl
  $env:SUPABASE_ANON_KEY = $SupabaseAnonKey
  Write-Host "Supabase env vars set (User scope + current session)" -ForegroundColor Yellow
}
elseif (-not $env:SUPABASE_URL -or -not $env:SUPABASE_ANON_KEY) {
  Write-Host "WARNING: SUPABASE_URL / SUPABASE_ANON_KEY not set. Set them (or pass -SupabaseUrl/-SupabaseAnonKey) or the app cannot Load Config." -ForegroundColor DarkYellow
}

# 8) Create desktop shortcut
$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutPath = Join-Path $desktop "LatencyArbTool.lnk"

$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($shortcutPath)
$sc.TargetPath = $exePath
$sc.WorkingDirectory = $app
$sc.IconLocation = $exePath
$sc.Save()

Write-Host "Shortcut created: $shortcutPath" -ForegroundColor Yellow

# 9) Start app
Start-Process $exePath

Write-Host "DONE. App started from: $app" -ForegroundColor Green
