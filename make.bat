@ECHO OFF
where /q dotnet
IF ERRORLEVEL 1 (
    make-legacy.bat %*
) ELSE (
    dotnet fsi scripts\make.fsx %*
)

