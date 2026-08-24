@echo off
REM Starts the catalogue stub and the storefront. A reverse proxy still has to sit in front of
REM both; see README.md. On IIS you do not need this at all — see "Hosting on IIS".
REM
REM Forward slashes below are deliberate: node accepts them on Windows.

if "%NG_ALLOWED_HOSTS%"=="" (
  echo Set NG_ALLOWED_HOSTS to the public hostname first, for example:
  echo     set NG_ALLOWED_HOSTS=upbazaar.example
  exit /b 1
)

if "%PORT%"=="" set PORT=4000
if "%STUB_API_PORT%"=="" set STUB_API_PORT=5200

start "upbazaar-catalogue" /b node api/catalog/stub-api.mjs --port %STUB_API_PORT%

set SSR_API_ORIGIN=http://127.0.0.1:%STUB_API_PORT%
node server/server.mjs
