@ECHO OFF
SET "FSXFSX=%ProgramW6432%\fsx\fsx.dll"

IF NOT EXIST "%FSXFSX%" (
    ECHO "%FSXFSX% not found" && EXIT /b 1
)

dotnet "%FSXFSX%" %*
