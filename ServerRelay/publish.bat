@echo off
setlocal
cls

echo ============================================================
echo   Universal Voice Chat - Docker Publish Utility
echo ============================================================
echo.

:: Check if Docker is installed
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Docker not found. Please install Docker Desktop first.
    pause
    exit /b
)

:: User Inputs
set /p DOCKER_USER="Enter your Docker Hub username: "
set /p IMAGE_NAME="Enter image name (e.g. SampVoice): "
set /p IMAGE_TAG="Enter tag (default: latest): "

:: Auto-lowercase using PowerShell (Docker requires lowercase)
for /f "usebackq delims=" %%i in (`powershell "'%DOCKER_USER%'.ToLower()"`) do set DOCKER_USER=%%i
for /f "usebackq delims=" %%i in (`powershell "'%IMAGE_NAME%'.ToLower()"`) do set IMAGE_NAME=%%i

if "%IMAGE_TAG%"=="" set IMAGE_TAG=latest

set FULL_IMAGE_NAME=%DOCKER_USER%/%IMAGE_NAME%:%IMAGE_TAG%

echo.
echo [1/3] Building Docker image: %FULL_IMAGE_NAME%...
docker build -t %FULL_IMAGE_NAME% .

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed.
    pause
    exit /b
)

echo.
echo [2/3] Authenticating with Docker Hub...
docker login

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Login failed.
    pause
    exit /b
)

echo.
echo [3/3] Pushing image to registry...
docker push %FULL_IMAGE_NAME%

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Push failed.
    pause
    exit /b
)

echo.
echo ============================================================
echo   SUCCESS! Your relay is now on Docker Hub.
echo   Image URL: docker.io/%FULL_IMAGE_NAME%
echo ============================================================
echo.
pause
