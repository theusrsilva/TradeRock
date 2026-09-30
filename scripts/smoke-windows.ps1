# Fumaça do instalador do Windows: instala em silêncio (como administrador), confere o app, a regra do
# firewall e a API, e desinstala conferindo que a regra saiu. Usado no CI (job windows).
#
#   pwsh scripts/smoke-windows.ps1 dist\TradeRock-Setup-x.y.z.exe x.y.z
param([Parameter(Mandatory)] [string] $Installer, [Parameter(Mandatory)] [string] $Version)
$ErrorActionPreference = 'Stop'
$rule = 'TradeRock (modo servidor)'

function Wait-Process-Exit($p, $what) {
  if (-not $p.WaitForExit(300000)) { throw "$what demorou demais" }
  if ($p.ExitCode -ne 0) { throw "$what saiu com $($p.ExitCode)" }
}

Write-Host '==> instalação silenciosa'
$log = Join-Path $env:TEMP 'traderock-setup.log'
Wait-Process-Exit (Start-Process $Installer -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/TASKS=firewall', "/LOG=$log" -PassThru) 'instalador'
$app = Join-Path ${env:ProgramFiles} 'TradeRock\traderock.exe'
if (-not (Test-Path $app)) { throw "traderock.exe não instalado em $app" }

Write-Host '==> versão'
$out = & $app --version | Out-String
if ($out.Trim() -ne "TradeRock $Version") { throw "versão não confere: '$out'" }

Write-Host '==> firewall (só perfil privado, só o traderock.exe)'
$fw = Get-NetFirewallRule -DisplayName $rule
if ($fw.Direction -ne 'Inbound' -or $fw.Action -ne 'Allow' -or "$($fw.Profile)" -ne 'Private') { throw "regra do firewall inesperada: $($fw | Out-String)" }
$program = ($fw | Get-NetFirewallApplicationFilter).Program
if ($program -ne $app) { throw "regra do firewall aponta para $program" }

Write-Host '==> esptool embutido'
$py = Join-Path ${env:ProgramFiles} 'TradeRock\esp32-runtime\python\python.exe'
$run = Join-Path ${env:ProgramFiles} 'TradeRock\esp32-runtime\worker\run.py'
$resp = '{"op":"identify","port":"COM199"}' | & $py -I -B $run --esp32 | Out-String
if ($resp -notmatch '"serial_port_missing"') { throw "esptool respondeu: $resp" }

Write-Host '==> API (sem janela, pasta de dados temporária)'
$data = Join-Path $env:TEMP ('traderock-smoke-' + [guid]::NewGuid())
$stdout = Join-Path $env:TEMP 'traderock-smoke.out'
$proc = Start-Process $app -ArgumentList '--no-window', '--port', '5098', '--data-dir', $data -RedirectStandardOutput $stdout -PassThru
try {
  $url = $null
  for ($i = 0; $i -lt 60 -and -not $url; $i++) {
    Start-Sleep 1
    if (Test-Path $stdout) { $url = (Select-String -Path $stdout -Pattern 'http://127\.0\.0\.1:5098/\?t=\S+').Matches.Value | Select-Object -First 1 }
  }
  if (-not $url) { throw 'o app não informou o endereço' }
  $token = ($url -split '\?t=')[1]
  $health = Invoke-RestMethod -Uri 'http://127.0.0.1:5098/api/health' -Headers @{ 'X-TradeRock-Token' = $token }
  if ($health.os -ne 'windows' -or $health.version -ne $Version) { throw "health inesperado: $($health | ConvertTo-Json)" }
  $setup = Invoke-RestMethod -Uri 'http://127.0.0.1:5098/api/setup' -Headers @{ 'X-TradeRock-Token' = $token }
  if ($setup.system.os -ne 'windows') { throw 'leitura do sistema não é do Windows' }
  # Pasta de dados só do usuário (sem herança).
  $acl = Get-Acl $data
  if (-not $acl.AreAccessRulesProtected -or @($acl.Access).Count -ne 1) { throw "ACL da pasta de dados: $($acl.AccessToString)" }
} finally {
  Stop-Process -Id $proc.Id -ErrorAction SilentlyContinue
}

Write-Host '==> desinstalação'
$uninstaller = Join-Path ${env:ProgramFiles} 'TradeRock\unins000.exe'
Wait-Process-Exit (Start-Process $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -PassThru) 'desinstalador'
Start-Sleep 2
if (Test-Path $app) { throw 'traderock.exe continua depois de desinstalar' }
if (Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue) { throw 'a regra do firewall continua depois de desinstalar' }
Write-Host 'ok'
