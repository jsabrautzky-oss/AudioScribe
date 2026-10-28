#define MyAppName "AudioScribe"
#define MyAppVersion "0.1.7"
#define MyAppPublisher "AudioScribe"
#define MyAppExeName "AudioScribe.exe"
#define MyAppIcon "..\src\AudioScribe.App\Assets\app_icon.ico"
#define AppSourceDir "..\dist\AudioScribe"
#define CudaExcludes "cublas*.dll;cublasLt*.dll;cudart*.dll;cudnn*.dll;cusparse*.dll;cusolver*.dll;curand*.dll;nvrtc*.dll;nvcuda.dll;cupti*.dll;nvtx*.dll;nvToolsExt*.dll;engine\cuda\*;cuda\*;cublas*;cudnn*;cusparse*;nvrtc*"

[Setup]
AppId={{5D1F8D6F-7B1C-4F8A-89C6-0E3A5F2C9D41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\AudioScribe
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
OutputDir=..\dist
OutputBaseFilename=AudioScribe-Setup-v0.1.7
SetupIconFile={#MyAppIcon}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppIcon}"; DestDir: "{app}"; DestName: "app_icon.ico"; Flags: ignoreversion
Source: "{#AppSourceDir}\*"; DestDir: "{app}"; Excludes: "{#CudaExcludes}"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\AudioScribe"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"
Name: "{autodesktop}\AudioScribe"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"; Tasks: desktopicon
Name: "{autoprograms}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\AudioScribe"
Type: filesandordirs; Name: "{localappdata}\AudioScribe\models"
Type: filesandordirs; Name: "{localappdata}\AudioScribe\nltk_data"
Type: filesandordirs; Name: "{app}\models"
Type: filesandordirs; Name: "{app}"
