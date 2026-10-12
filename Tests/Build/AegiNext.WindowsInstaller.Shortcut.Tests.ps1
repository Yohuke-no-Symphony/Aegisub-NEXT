#Requires -Version 7.2
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeDiscovery {
    $canRunInstallerIntegration = $false
    if ($IsWindows)
    {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $canRunInstallerIntegration = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -and
            [bool](Get-Command makensis -CommandType Application -ErrorAction SilentlyContinue)
    }
}

Describe 'Native optional desktop shortcut lifecycle' -Tag InstallerIntegration -Skip:(!$canRunInstallerIntegration) {
    BeforeAll {
        $repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        Import-Module (Join-Path $repository 'scripts/publish/AegiNext.Publish.psm1') -Force
        $publishModule = Get-Module AegiNext.Publish
        $fixture = Join-Path $TestDrive 'shortcut fixture'
        $fixtureRepository = Join-Path $fixture 'repository'
        $fixturePayload = Join-Path $fixture 'payload'
        $fixtureOutput = Join-Path $fixture 'package'
        $desktop = Join-Path $fixture 'desktop'
        $fixtureId = [guid]::NewGuid().ToString('N')
        $registryKey = "Software\AegiNextInstallerTest-$fixtureId"
        $startMenuFolder = "AegiNext-InstallerTest-$fixtureId"
        foreach ($directory in @($fixturePayload, $fixtureOutput, $desktop,
            (Join-Path $fixtureRepository 'scripts/publish'), (Join-Path $fixtureRepository 'src/AegiNext.Desktop/Assets')))
        {
            [IO.Directory]::CreateDirectory($directory) | Out-Null
        }
        $source = Get-Content -LiteralPath (Join-Path $repository 'scripts/publish/AegiNext.WindowsInstaller.nsi') -Raw
        $source = $source.Replace('Software\Microsoft\Windows\CurrentVersion\Uninstall\AegiNext', $registryKey)
        $source = $source.Replace('$SMPROGRAMS\AegiNext', ('$SMPROGRAMS\' + $startMenuFolder))
        $source = $source.Replace('$DESKTOP\AegiNEXT.lnk', (Join-Path $desktop 'AegiNEXT.lnk'))
        [IO.File]::WriteAllText((Join-Path $fixtureRepository 'scripts/publish/AegiNext.WindowsInstaller.nsi'), $source, [Text.UTF8Encoding]::new($false))
        Copy-Item -LiteralPath (Join-Path $repository 'src/AegiNext.Desktop/Assets/AppIcon.ico') -Destination (Join-Path $fixtureRepository 'src/AegiNext.Desktop/Assets')
        foreach ($name in @('aegi-next.exe', 'aegn-exporter.exe', 'aegnc.exe'))
        {
            Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32/cmd.exe') -Destination (Join-Path $fixturePayload $name)
        }
        $installer = & $publishModule {
            param($Root, $Payload, $Output)
            $compiler = Get-AegiNextNsisCompiler -RepositoryRoot $Root
            New-AegiNextWindowsInstaller -RepositoryRoot $Root -PayloadDirectory $Payload -PublishDirectory $Output `
                -ProductVersion 0.1.0 -RuntimeIdentifier win-x64 -NsisCompiler $compiler.Path
        } $fixtureRepository $fixturePayload $fixtureOutput

        function Invoke-InstallerFixture
        {
            param([string] $FilePath, [string] $Arguments)
            $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -Wait -PassThru
            return $process.ExitCode
        }
    }

    BeforeEach {
        $installation = Join-Path $fixture ('installed with spaces ' + [guid]::NewGuid().ToString('N'))
        $shortcut = Join-Path $desktop 'AegiNEXT.lnk'
        $mode = 'CurrentUser'
    }

    AfterEach {
        $uninstaller = Join-Path $installation 'Uninstall.exe'
        if ((Test-Path -LiteralPath $uninstaller) -and (Test-Path -LiteralPath (Join-Path $installation 'install-state.ini')))
        {
            $null = Invoke-InstallerFixture -FilePath $uninstaller -Arguments "/S /$mode _?=$installation"
        }
        if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
        foreach ($hive in @([Microsoft.Win32.RegistryHive]::CurrentUser, [Microsoft.Win32.RegistryHive]::LocalMachine))
        {
            $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)
            try { $root.DeleteSubKeyTree($registryKey, $false) }
            finally { $root.Dispose() }
        }
        foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('CommonPrograms')))
        {
            $directory = Join-Path $folder $startMenuFolder
            if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
        }
    }

    It 'handles the optional shortcut for <Scope>, enabled=<Enabled>' -ForEach @(
        @{ Scope = 'CurrentUser'; Enabled = $false }
        @{ Scope = 'CurrentUser'; Enabled = $true }
        @{ Scope = 'AllUsers'; Enabled = $false }
        @{ Scope = 'AllUsers'; Enabled = $true }
    ) {
        $mode = $Scope
        $option = if ($Enabled) { ' /DesktopShortcut' } else { '' }
        Invoke-InstallerFixture -FilePath $installer -Arguments "/S /$mode$option /D=$installation" | Should -Be 0
        Test-Path -LiteralPath $shortcut | Should -Be $Enabled
        $state = Get-Content -LiteralPath (Join-Path $installation 'install-state.ini') -Raw
        $state | Should -Match "(?m)^Mode=$mode\s*$"
        $state | Should -Match "(?m)^DesktopShortcut=$([int]$Enabled)\s*$"
        $hive = if ($mode -eq 'AllUsers') { [Microsoft.Win32.RegistryHive]::LocalMachine } else { [Microsoft.Win32.RegistryHive]::CurrentUser }
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey($hive, [Microsoft.Win32.RegistryView]::Registry64)
        try
        {
            $key = $root.OpenSubKey($registryKey)
            $key | Should -Not -BeNullOrEmpty
            try { $key.GetValue('InstallLocation') | Should -Be $installation }
            finally { $key.Dispose() }
        }
        finally { $root.Dispose() }
        if ($Enabled)
        {
            (Get-Item -LiteralPath $shortcut).Name | Should -BeExactly 'AegiNEXT.lnk'
            $shell = New-Object -ComObject WScript.Shell
            try
            {
                $link = $shell.CreateShortcut($shortcut)
                try
                {
                    $link.TargetPath | Should -Be (Join-Path $installation 'aegi-next.exe')
                    $link.WorkingDirectory | Should -Be $installation
                }
                finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) | Out-Null }
            }
            finally { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null }
        }
        Invoke-InstallerFixture -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S /$mode _?=$installation" | Should -Be 0
        Test-Path -LiteralPath $shortcut | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installation 'aegi-next.exe') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installation 'aegn-exporter.exe') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installation 'aegnc.exe') | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $installation 'install-state.ini') | Should -BeFalse
    }

    It 'preserves a desktop shortcut that this installation did not create' {
        Set-Content -LiteralPath $shortcut -Value 'unowned desktop shortcut'
        Invoke-InstallerFixture -FilePath $installer -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0
        Invoke-InstallerFixture -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S /CurrentUser _?=$installation" | Should -Be 0
        Get-Content -LiteralPath $shortcut -Raw | Should -Match 'unowned desktop shortcut'
    }

    It 'reports shortcut creation failure without recording ownership or removing the existing file' {
        Set-Content -LiteralPath $shortcut -Value 'locked unowned shortcut'
        $stream = [IO.File]::Open($shortcut, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try
        {
            Invoke-InstallerFixture -FilePath $installer -Arguments "/S /CurrentUser /DesktopShortcut /D=$installation" | Should -Be 1
            Get-Content -LiteralPath (Join-Path $installation 'install-state.ini') -Raw | Should -Match '(?m)^DesktopShortcut=0\s*$'
        }
        finally { $stream.Dispose() }
        Invoke-InstallerFixture -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S /CurrentUser _?=$installation" | Should -Be 0
        Get-Content -LiteralPath $shortcut -Raw | Should -Match 'locked unowned shortcut'
    }

    It 'preserves an unowned shortcut when a legacy installation state has no shortcut marker' {
        Invoke-InstallerFixture -FilePath $installer -Arguments "/S /CurrentUser /D=$installation" | Should -Be 0
        $state = Join-Path $installation 'install-state.ini'
        (Get-Content -LiteralPath $state | Where-Object { $_ -notmatch '^DesktopShortcut=' }) |
            Set-Content -LiteralPath $state -Encoding unicode
        Set-Content -LiteralPath $shortcut -Value 'legacy unowned shortcut'
        Invoke-InstallerFixture -FilePath (Join-Path $installation 'Uninstall.exe') -Arguments "/S /CurrentUser _?=$installation" | Should -Be 0
        Get-Content -LiteralPath $shortcut -Raw | Should -Match 'legacy unowned shortcut'
    }
}
