@ECHO OFF
SET "FSXFSX=%ProgramW6432%\fsx\fsx.exe"

IF NOT EXIST "%FSXFSX%" (
    ECHO "%FSXFSX% not found" && EXIT /b 1
)

"%FSXFSX%" %*
