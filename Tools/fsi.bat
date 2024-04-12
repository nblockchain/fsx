@ECHO OFF

SET "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
SET "INSTALL_MSG=Please install .NET v6, or higher; or Visual Studio."

IF NOT EXIST "%VSWHERE%" (
    echo:
    echo Tool vswhere.exe not found.
    echo %INSTALL_MSG%
    exit /b 1
)

FOR /f "tokens=* delims=" %%A in ('"%VSWHERE%" -latest -requires Microsoft.VisualStudio.Component.FSharp -find **\fsi.exe') do set RUNNER=%%A

IF "%RUNNER%"=="" (
    echo:
    echo F# not found.
    echo %INSTALL_MSG%
    exit /b 1
)

"%RUNNER%" --define:LEGACY_FRAMEWORK %*
