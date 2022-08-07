@ECHO OFF
FOR /f "tokens=* delims=" %%A in ('"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe') do set BUILDTOOL=%%A

IF %1.==install. GOTO Install
IF %1.==. GOTO JustBuild
GOTO ErrorArg

:Install
    "%BUILDTOOL%" fsx.sln /p:Configuration=Release || EXIT /b
    REM TODO: ignore if it already exists?
    mkdir "%ProgramFiles%\fsx" || EXIT /b
    copy fsxc\bin\Release\*.* "%ProgramFiles%\fsx" || EXIT /b
    GOTO End

:JustBuild
    IF NOT EXIST .nuget (
        mkdir .nuget\ || EXIT /b
    )
    IF NOT EXIST .nuget\nuget.exe (
        powershell -Command "Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe -OutFile .nuget\nuget.exe" || EXIT /b
    )
    .nuget\nuget.exe restore fsx.sln  || EXIT /b
    "%BUILDTOOL%" fsx.sln /p:Configuration=Debug || EXIT /b
    GOTO End

:ErrorArg
    ECHO "Argument not recognized: %1"
    GOTO End

:End
