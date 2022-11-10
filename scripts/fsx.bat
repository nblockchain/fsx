@ECHO OFF
SET "FSXFSX=%ProgramW6432%\fsx\fsx.fsx"

IF NOT EXIST "%FSXFSX%" (
    ECHO "%FSXFSX% not found" && EXIT /b 1
)

dotnet fsi "%FSXFSX%" %*
