@echo off
rem 用固定的编辑器打开本工程，并把编辑器日志也落进 log\ 下
set "UNITY=D:\Hylyre\UnityEditor\2022.3.50f1c1\Editor\Unity.exe"
set "PROJ=%~dp0"
if not exist "%PROJ%log" mkdir "%PROJ%log"
start "" "%UNITY%" -projectPath "%PROJ%." -logFile "%PROJ%log\editor.log"
