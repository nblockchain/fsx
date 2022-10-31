@ECHO OFF
SET "FSIBAT=%ProgramFiles%\fsx\fsi.bat"
SET "FSXFSX=%ProgramFiles%\fsx\fsx.fsx"

IF NOT EXIST "%FSIBAT%" (
    ECHO "%FSIBAT% not found" && EXIT /b 1
)

IF NOT EXIST "%FSXFSX%" (
    ECHO "%FSXFSX% not found" && EXIT /b 1
)
CALL "%ProgramFiles%\fss\fsi.bat" "%ProgramFiles%\fsx\fsx.fsx" %* || EXIT /b
