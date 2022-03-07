@ECHO OFF

FOR /f "tokens=* delims=" %%A in ('"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.VisualStudio.Component.FSharp -find **\fsi.exe') do set RUNNER=%%A

"%RUNNER%" %*
