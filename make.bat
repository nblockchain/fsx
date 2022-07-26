FOR /f "tokens=* delims=" %%A in ('"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe') do set BUILDTOOL=%%A

IF %1.==install. GOTO Install
IF %1.==. GOTO JustBuild
GOTO Error

:Install
    "%BUILDTOOL%" fsx.sln /p:Configuration=Release
    mkdir "%ProgramFiles%\fsx"
    copy fsxc\bin\Release\*.* "%ProgramFiles%\fsx"
    GOTO End

:JustBuild
    "%BUILDTOOL%" fsx.sln /p:Configuration=Debug
    GOTO End

:Error
    ECHO "Argument not recognized: %1"

:End
