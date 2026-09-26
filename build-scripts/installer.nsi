!include "MUI2.nsh"
!include "FileFunc.nsh"

; Script lives in build-scripts/, but publish output is at repo root
!cd ".."

!define MUI_ICON "Plain Craft Launcher 2/Images/icon.ico"
!define MUI_UNICON "Plain Craft Launcher 2/Images/icon.ico"

Name "PCL2-R"
OutFile "@OUTFILE@"
InstallDir "$PROGRAMFILES\PCL2-R"
SetCompressor /SOLID lzma
RequestExecutionLevel admin

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "publish\*.*"
  WriteUninstaller "$INSTDIR\uninstall.exe"
  CreateDirectory "$SMPROGRAMS\PCL2-R"
  CreateShortcut "$SMPROGRAMS\PCL2-R\PCL2-R.lnk" "$INSTDIR\PCL2.Avalonia.exe"
  CreateShortcut "$DESKTOP\PCL2-R.lnk" "$INSTDIR\PCL2.Avalonia.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2-R" "DisplayName" "PCL2-R"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2-R" "DisplayIcon" '"$INSTDIR\PCL2.Avalonia.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2-R" "UninstallString" '"$INSTDIR\uninstall.exe"'
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2-R" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"
  Delete "$SMPROGRAMS\PCL2-R\PCL2-R.lnk"
  Delete "$DESKTOP\PCL2-R.lnk"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2-R"
SectionEnd
