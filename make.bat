@ECHO OFF
where /q dotnet
IF ERRORLEVEL 1 (
    Tools\fsi.bat scripts\make.fsx %*
) ELSE (
    dotnet fsi scripts\make.fsx %*
)

