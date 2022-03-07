FOR /f "tokens=* delims=" %%A in ('"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe') do set BUILDTOOL=%%A

"%BUILDTOOL%" fsx.sln
