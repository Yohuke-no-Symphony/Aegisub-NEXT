# -*- coding: utf-8 -*-
Unicode true
!include "${AEGINEXT_CONFIG_FILE}"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\AegiNext"
!define MULTIUSER_EXECUTIONLEVEL Highest
!define MULTIUSER_MUI
!define MULTIUSER_USE_PROGRAMFILES64
!define MULTIUSER_INSTALLMODE_DEFAULT_CURRENTUSER
!define MULTIUSER_INSTALLMODE_COMMANDLINE
!define MULTIUSER_INSTALLMODE_INSTDIR "AegiNext"
!define MULTIUSER_INSTALLMODE_DEFAULT_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MULTIUSER_INSTALLMODE_DEFAULT_REGISTRY_VALUENAME "InstallLocation"
!define MULTIUSER_INSTALLMODE_INSTDIR_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MULTIUSER_INSTALLMODE_INSTDIR_REGISTRY_VALUENAME "InstallLocation"
!define MULTIUSER_INSTALLMODE_FUNCTION RestoreRequestedDirectory
!include "MultiUser.nsh"
!include "MUI2.nsh"

Name "AegiNext ${AEGINEXT_PRODUCT_VERSION}"
OutFile "${AEGINEXT_OUTPUT_FILE}"
InstallDir ""
SetCompressor /SOLID lzma
SetOverwrite on
VIProductVersion "${AEGINEXT_FILE_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "AegiNext"
VIAddVersionKey /LANG=1033 "ProductVersion" "${AEGINEXT_PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "FileVersion" "${AEGINEXT_FILE_VERSION}"
VIAddVersionKey /LANG=1033 "FileDescription" "AegiNext Setup"
VIAddVersionKey /LANG=1033 "LegalCopyright" "AegiNext contributors"

Var requestedDirectory
Var previousDirectory
Var previousUninstaller
Var uninstallDirectory
Var installedMode
Var desktopShortcutRequested

!define MUI_ICON "${AEGINEXT_ICON_FILE}"
!define MUI_UNICON "${AEGINEXT_ICON_FILE}"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MULTIUSER_PAGE_INSTALLMODE
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "$(DesktopShortcutOption)"
!define MUI_FINISHPAGE_RUN_FUNCTION CreateDesktopShortcut
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"

LangString FilesInUse ${LANG_ENGLISH} "Close AegiNext and its export worker, then run setup again. A file is in use or cannot be accessed."
LangString FilesInUse ${LANG_SIMPCHINESE} "请关闭 AegiNext 及其导出进程，再重新运行安装程序。有文件被占用或无法访问。"
LangString PreviousUninstallFailed ${LANG_ENGLISH} "The previous installation could not be removed. Close AegiNext and its export worker, then try again."
LangString PreviousUninstallFailed ${LANG_SIMPCHINESE} "无法移除旧安装。请关闭 AegiNext 及其导出进程，然后重试。"
LangString InvalidInstallState ${LANG_ENGLISH} "The installation state is missing or invalid. Reinstall AegiNext in this directory before uninstalling."
LangString InvalidInstallState ${LANG_SIMPCHINESE} "安装记录缺失或无效。请先将 AegiNext 重新安装到此目录，再执行卸载。"
LangString DeleteFailed ${LANG_ENGLISH} "An installed file could not be deleted. Close AegiNext and its export worker, then run the uninstaller again."
LangString DeleteFailed ${LANG_SIMPCHINESE} "无法删除已安装文件。请关闭 AegiNext 及其导出进程，再重新运行卸载程序。"
LangString UnsupportedArchitecture ${LANG_ENGLISH} "This package requires 64-bit Windows."
LangString UnsupportedArchitecture ${LANG_SIMPCHINESE} "此安装包需要 64 位 Windows。"
LangString ScopeConflict ${LANG_ENGLISH} "This directory belongs to an installation with a different scope. Choose another directory or uninstall that installation first."
LangString ScopeConflict ${LANG_SIMPCHINESE} "此目录属于另一安装范围。请选择其他目录，或先卸载该安装。"
LangString DestinationNotWritable ${LANG_ENGLISH} "The destination directory is not writable. Choose another directory or check its permissions."
LangString DestinationNotWritable ${LANG_SIMPCHINESE} "无法写入目标目录。请选择其他目录，或检查目录权限。"
LangString DesktopShortcutOption ${LANG_ENGLISH} "Create an AegiNEXT desktop shortcut"
LangString DesktopShortcutOption ${LANG_SIMPCHINESE} "创建 AegiNEXT 桌面快捷方式"
LangString DesktopShortcutFailed ${LANG_ENGLISH} "The desktop shortcut could not be created or recorded. Check desktop and installation directory permissions."
LangString DesktopShortcutFailed ${LANG_SIMPCHINESE} "无法创建或记录桌面快捷方式。请检查桌面及安装目录的写入权限。"

!macro AegiNextCheckFile PATH
    ${If} ${FileExists} "${PATH}"
        StrCpy $1 "${PATH}"
        System::Call 'kernel32::CreateFileW(w r1, i 0x40010000, i 0, p 0, i 3, i 0, p 0) p .r0'
        ${If} $0 == -1
            MessageBox MB_OK|MB_ICONSTOP "$(FilesInUse)" /SD IDOK
            SetErrorLevel 1
            Abort
        ${EndIf}
        System::Call 'kernel32::CloseHandle(p r0)'
    ${EndIf}
!macroend

!macro AegiNextCheckDelete
    ${If} ${Errors}
        MessageBox MB_OK|MB_ICONSTOP "$(DeleteFailed)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
    ClearErrors
!macroend

!include "${AEGINEXT_PAYLOAD_INCLUDE}"

Function RestoreRequestedDirectory
    ${If} $requestedDirectory != ""
        StrCpy $INSTDIR $requestedDirectory
    ${EndIf}
FunctionEnd

Function .onInit
    ${IfNot} ${RunningX64}
        MessageBox MB_OK|MB_ICONSTOP "$(UnsupportedArchitecture)" /SD IDOK
        SetErrorLevel 1
        Quit
    ${EndIf}
    StrCpy $requestedDirectory $INSTDIR
    SetRegView 64
    !insertmacro MULTIUSER_INIT
    StrCpy $desktopShortcutRequested 0
    ${GetParameters} $0
    ClearErrors
    ${GetOptions} $0 "/DesktopShortcut" $1
    ${IfNot} ${Errors}
        StrCpy $desktopShortcutRequested 1
    ${EndIf}
    ClearErrors
FunctionEnd

Function CreateDesktopShortcut
    SetOutPath "$INSTDIR"
    ClearErrors
    CreateShortcut "$DESKTOP\AegiNEXT.lnk" "$INSTDIR\aegi-next.exe" "" "$INSTDIR\aegi-next.exe" 0
    ${If} ${Errors}
        Goto desktop_shortcut_failed
    ${EndIf}
    WriteINIStr "$INSTDIR\install-state.ini" "Install" "DesktopShortcut" "1"
    ${If} ${Errors}
        Delete "$DESKTOP\AegiNEXT.lnk"
        Goto desktop_shortcut_failed
    ${EndIf}
    Return
desktop_shortcut_failed:
    MessageBox MB_OK|MB_ICONSTOP "$(DesktopShortcutFailed)" /SD IDOK
    SetErrorLevel 1
FunctionEnd

Function .onInstSuccess
    IfSilent 0 desktop_shortcut_done
    ${If} $desktopShortcutRequested == 1
        Call CreateDesktopShortcut
    ${EndIf}
desktop_shortcut_done:
FunctionEnd

Function un.onInit
    StrCpy $uninstallDirectory $INSTDIR
    ReadINIStr $installedMode "$uninstallDirectory\install-state.ini" "Install" "Mode"
    ReadINIStr $LANGUAGE "$uninstallDirectory\install-state.ini" "Install" "Language"
    SetRegView 64
    !insertmacro MULTIUSER_UNINIT
    ${If} $installedMode == "AllUsers"
        ${If} $MultiUser.Privileges != "Admin"
        ${AndIf} $MultiUser.Privileges != "Power"
            SetErrorLevel 1
            Quit
        ${EndIf}
        Call un.MultiUser.InstallMode.AllUsers
    ${ElseIf} $installedMode == "CurrentUser"
        Call un.MultiUser.InstallMode.CurrentUser
    ${Else}
        MessageBox MB_OK|MB_ICONSTOP "$(InvalidInstallState)" /SD IDOK
        SetErrorLevel 1
        Quit
    ${EndIf}
    StrCpy $INSTDIR $uninstallDirectory
FunctionEnd

Section "AegiNext" SEC_APP
    ReadINIStr $installedMode "$INSTDIR\install-state.ini" "Install" "Mode"
    ${If} $installedMode != ""
    ${AndIf} $installedMode != $MultiUser.InstallMode
        Goto scope_conflict
    ${EndIf}
    ${If} $MultiUser.InstallMode == "CurrentUser"
        ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "InstallLocation"
    ${Else}
        ReadRegStr $0 HKCU "${UNINSTALL_KEY}" "InstallLocation"
    ${EndIf}
    ${If} $0 != ""
        GetFullPathName $0 "$0"
        GetFullPathName $1 "$INSTDIR"
        ${If} $0 == $1
            Goto scope_conflict
        ${EndIf}
    ${EndIf}
    !insertmacro AegiNextCheckInstalledFiles
    !insertmacro AegiNextCheckFile "$INSTDIR\install-state.ini"
    ClearErrors
    CreateDirectory "$INSTDIR"
    ${If} ${Errors}
        Goto destination_not_writable
    ${EndIf}
    GetTempFileName $0 "$INSTDIR"
    ${If} ${Errors}
        Goto destination_not_writable
    ${EndIf}
    Delete "$0"
    ${If} ${Errors}
        Goto destination_not_writable
    ${EndIf}
    ReadRegStr $previousDirectory SHCTX "${UNINSTALL_KEY}" "InstallLocation"
    ${If} $previousDirectory != ""
        StrCpy $previousUninstaller "$previousDirectory\Uninstall.exe"
        IfFileExists "$previousUninstaller" 0 previous_uninstall_failed
        ClearErrors
        ExecWait '"$previousUninstaller" /S /$MultiUser.InstallMode _?=$previousDirectory' $0
        ${If} ${Errors}
            Goto previous_uninstall_failed
        ${EndIf}
        ${If} $0 != 0
            Goto previous_uninstall_failed
        ${EndIf}
        IfFileExists "$previousDirectory\aegi-next.exe" previous_uninstall_failed
        IfFileExists "$previousDirectory\aegn-exporter.exe" previous_uninstall_failed
        IfFileExists "$previousDirectory\aegnc.exe" previous_uninstall_failed
        ClearErrors
        Delete "$previousUninstaller"
        ${If} ${Errors}
            Goto previous_uninstall_failed
        ${EndIf}
        RMDir "$previousDirectory"
    ${EndIf}
    SetOutPath "$INSTDIR"
    ClearErrors
    !insertmacro AegiNextInstallFiles
    ${If} ${Errors}
        SetErrorLevel 1
        Abort
    ${EndIf}
    SetOutPath "$INSTDIR"
    WriteINIStr "$INSTDIR\install-state.ini" "Install" "Mode" "$MultiUser.InstallMode"
    WriteINIStr "$INSTDIR\install-state.ini" "Install" "Language" "$LANGUAGE"
    WriteINIStr "$INSTDIR\install-state.ini" "Install" "DesktopShortcut" "0"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateDirectory "$SMPROGRAMS\AegiNext"
    CreateShortcut "$SMPROGRAMS\AegiNext\AegiNext.lnk" "$INSTDIR\aegi-next.exe"
    CreateShortcut "$SMPROGRAMS\AegiNext\Uninstall.lnk" "$INSTDIR\Uninstall.exe" "/$MultiUser.InstallMode"
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "DisplayName" "AegiNext"
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "DisplayVersion" "${AEGINEXT_PRODUCT_VERSION}"
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "Publisher" "AegiNext"
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\aegi-next.exe,0"
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\" /$MultiUser.InstallMode'
    WriteRegStr SHCTX "${UNINSTALL_KEY}" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S /$MultiUser.InstallMode'
    WriteRegDWORD SHCTX "${UNINSTALL_KEY}" "EstimatedSize" ${AEGINEXT_ESTIMATED_SIZE}
    WriteRegDWORD SHCTX "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD SHCTX "${UNINSTALL_KEY}" "NoRepair" 1
    ${If} ${Errors}
        SetErrorLevel 1
        Abort
    ${EndIf}
    SetErrorLevel 0
    Goto install_done
previous_uninstall_failed:
    MessageBox MB_OK|MB_ICONSTOP "$(PreviousUninstallFailed)" /SD IDOK
    SetErrorLevel 1
    Abort
install_done:
    Goto section_end
scope_conflict:
    MessageBox MB_OK|MB_ICONSTOP "$(ScopeConflict)" /SD IDOK
    SetErrorLevel 1
    Abort
destination_not_writable:
    MessageBox MB_OK|MB_ICONSTOP "$(DestinationNotWritable)" /SD IDOK
    SetErrorLevel 1
    Abort
section_end:
SectionEnd

Section "Uninstall"
    !insertmacro AegiNextCheckInstalledFiles
    !insertmacro AegiNextCheckFile "$INSTDIR\install-state.ini"
    ReadINIStr $0 "$INSTDIR\install-state.ini" "Install" "DesktopShortcut"
    ${If} $0 == "1"
        !insertmacro AegiNextCheckFile "$DESKTOP\AegiNEXT.lnk"
    ${EndIf}
    ClearErrors
    !insertmacro AegiNextUninstallFiles
    ReadINIStr $0 "$INSTDIR\install-state.ini" "Install" "DesktopShortcut"
    ${If} $0 == "1"
        ClearErrors
        Delete "$DESKTOP\AegiNEXT.lnk"
        !insertmacro AegiNextCheckDelete
    ${EndIf}
    ClearErrors
    Delete "$SMPROGRAMS\AegiNext\AegiNext.lnk"
    !insertmacro AegiNextCheckDelete
    Delete "$SMPROGRAMS\AegiNext\Uninstall.lnk"
    !insertmacro AegiNextCheckDelete
    RMDir "$SMPROGRAMS\AegiNext"
    ClearErrors
    DeleteRegKey SHCTX "${UNINSTALL_KEY}"
    !insertmacro AegiNextCheckDelete
    Delete "$INSTDIR\install-state.ini"
    !insertmacro AegiNextCheckDelete
    Delete "$INSTDIR\Uninstall.exe"
    SetOutPath "$TEMP"
    RMDir "$INSTDIR"
    SetErrorLevel 0
SectionEnd
