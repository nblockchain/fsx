REM Obtained via "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe
REM TODO: call it inside configure.bat
"%ProgramFiles(x86)%\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe" fsx.sln
