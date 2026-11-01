; Instalador do TradeRock para Windows x64 (só ESP32). Compilado pelo Inno Setup 6:
;   scripts/package-windows.sh --installer   (ou ISCC /DAppVersion=... /DSourceDir=... /DOutputDir=... traderock.iss)
;
; - Pede administrador (decisão do autor, D20): instala em Arquivos de Programas e cria a regra do
;   Firewall do Windows para o modo servidor, só nas redes privadas e só para o traderock.exe, e a regra do
;   Sword/Shield na rede de casa (modo LAN, D24): UDP do pokeldn, só redes privadas e só para o Python embutido.
; - O app em si nunca roda como administrador: o "Abrir o TradeRock" do fim roda como o usuário, e também a
;   reabertura depois de uma atualização feita pelo app (D29).
; - Os dados (cofre, prod.keys, contas) ficam em %LOCALAPPDATA%\TradeRock de cada usuário e não são
;   apagados na desinstalação.

#ifndef AppVersion
  #error Defina /DAppVersion=x.y.z
#endif
#ifndef SourceDir
  #define SourceDir "..\..\build\windows\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\dist"
#endif

#define FirewallRule "TradeRock (modo servidor)"
#define LanRule "TradeRock (Sword/Shield na rede de casa)"
; Portas do modo LAN do Sword/Shield (pokeldn/ldn/pia_lan.py): busca, broadcast e estações.
#define LanPorts "30000,40000,49152-49155"

[Setup]
AppId={{6B0C2E4A-7D1F-4E0B-9B53-2A8C7F3D9E61}
AppName=TradeRock
AppVersion={#AppVersion}
AppVerName=TradeRock {#AppVersion}
AppPublisher=TradeRock
AppPublisherURL=https://github.com/theusrsilva/TradeRock
AppSupportURL=https://github.com/theusrsilva/TradeRock/issues
DefaultDirName={autopf}\TradeRock
DefaultGroupName=TradeRock
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile={#SourceDir}\LICENSE
SetupIconFile=traderock.ico
UninstallDisplayIcon={app}\traderock.exe
UninstallDisplayName=TradeRock
OutputDir={#OutputDir}
OutputBaseFilename=TradeRock-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ptbr.DesktopIcon=Criar um atalho na Área de Trabalho
en.DesktopIcon=Create a desktop shortcut
ptbr.Firewall=Liberar o modo servidor no Firewall do Windows (só redes privadas)
en.Firewall=Allow server mode in Windows Firewall (private networks only)
ptbr.LanFirewall=Liberar a troca do Sword/Shield pela rede de casa no Firewall do Windows (só redes privadas)
en.LanFirewall=Allow Sword/Shield trades over the home network in Windows Firewall (private networks only)
ptbr.Launch=Abrir o TradeRock
en.Launch=Open TradeRock
ptbr.DataKept=Seus dados (cofre, contas e prod.keys) continuam em %LOCALAPPDATA%\TradeRock de cada usuário. Apague essa pasta se não for mais usar o TradeRock.
en.DataKept=Your data (vault, accounts and prod.keys) stays in each user's %LOCALAPPDATA%\TradeRock. Delete that folder if you won't use TradeRock anymore.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
Name: "firewall"; Description: "{cm:Firewall}"
Name: "lanfirewall"; Description: "{cm:LanFirewall}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\TradeRock"; Filename: "{app}\traderock.exe"
Name: "{autodesktop}\TradeRock"; Filename: "{app}\traderock.exe"; Tasks: desktopicon

[Run]
; Regra do firewall: entrada TCP só para o traderock.exe, só no perfil "Privada". O app só escuta na
; rede quando o modo servidor é ligado (D19); fora disso, só em 127.0.0.1.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FirewallRule}"" dir=in action=allow program=""{app}\traderock.exe"" protocol=TCP profile=private enable=yes"; Flags: runhidden; Tasks: firewall
; Regra do modo LAN: entrada UDP só para o Python embutido (é ele que roda o pokeldn), só no perfil "Privada".
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#LanRule}"""; Flags: runhidden; Tasks: lanfirewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#LanRule}"" dir=in action=allow program=""{app}\esp32-runtime\python\python.exe"" protocol=UDP localport={#LanPorts} profile=private enable=yes"; Flags: runhidden; Tasks: lanfirewall
Filename: "{app}\traderock.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Atualização pelo app (D29): ele roda este instalador com /SILENT /TRADEROCKRESTART=1 e fecha; no fim, o app novo
; abre de novo como o usuário (nunca como administrador).
Filename: "{app}\traderock.exe"; Flags: nowait runasoriginaluser; Check: RestartAfterUpdate

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#LanRule}"""; Flags: runhidden; RunOnceId: "RemoveLanRule"

[Code]
function RestartAfterUpdate: Boolean;
begin
  Result := ExpandConstant('{param:TRADEROCKRESTART|0}') = '1';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(ExpandConstant('{cm:DataKept}'), mbInformation, MB_OK);
end;
