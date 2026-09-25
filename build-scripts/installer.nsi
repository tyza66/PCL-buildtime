!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "PCL2 Avalonia"
OutFile "@OUTFILE@"
InstallDir "$PROGRAMFILES\PCL2 Avalonia"
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
  CreateDirectory "$SMPROGRAMS\PCL2 Avalonia"
  CreateShortcut "$SMPROGRAMS\PCL2 Avalonia\PCL2 Avalonia.lnk" "$INSTDIR\PCL2.Avalonia.exe"
  CreateShortcut "$DESKTOP\PCL2 Avalonia.lnk" "$INSTDIR\PCL2.Avalonia.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2Avalonia" "DisplayName" "PCL2 Avalonia"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2Avalonia" "UninstallString" '"$INSTDIR\uninstall.exe"'
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2Avalonia" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\uninstall.exe"
  RMDir /r "$INSTDIR"
  Delete "$SMPROGRAMS\PCL2 Avalonia\PCL2 Avalonia.lnk"
  Delete "$DESKTOP\PCL2 Avalonia.lnk"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\PCL2Avalonia"
SectionEnd
