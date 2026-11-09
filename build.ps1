$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $PSScriptRoot
try {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32manifest:app.manifest /out:FolderCleaner.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll FolderCleaner.cs Recycler.cs SelfTest.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Write-Host 'Built FolderCleaner.exe'
} finally { Pop-Location }
