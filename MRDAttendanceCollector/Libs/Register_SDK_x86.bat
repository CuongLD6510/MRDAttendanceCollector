@echo off
echo Dang ky ZKTeco SDK (32-bit)...
echo Can chay file nay voi quyen Administrator.
cd /d "%~dp0"
%windir%\SysWOW64\regsvr32.exe /s "%~dp0zkemkeeper.dll"
if %errorlevel% equ 0 (
    echo Dang ky thanh cong.
) else (
    echo Dang ky that bai. ErrorLevel=%errorlevel%
)
pause
