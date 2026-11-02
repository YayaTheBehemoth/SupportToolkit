@echo off
title SupportToolkit
echo.
echo SupportToolkit is starting. A page opens in your browser.
echo Leave this window open while you work. When you are finished, press
echo "Exit SupportToolkit" in the page, or close this window.
echo.
"%~dp0SupportToolkit.exe" serve
if errorlevel 1 (
    echo.
    echo SupportToolkit stopped with a problem. The message above says why.
    pause
)
