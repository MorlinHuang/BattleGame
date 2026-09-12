@echo off
rem 无界面出包到 Build\BattleGame.exe，日志在 log\build.log
set "UNITY=D:\Hylyre\UnityEditor\2022.3.50f1c1\Editor\Unity.exe"
set "PROJ=%~dp0"
if not exist "%PROJ%log" mkdir "%PROJ%log"
"%UNITY%" -batchmode -quit -projectPath "%PROJ%." -executeMethod Chashouji.EditorTools.BuildTool.BuildWindows -logFile "%PROJ%log\build.log"
echo 退出码 %ERRORLEVEL%
