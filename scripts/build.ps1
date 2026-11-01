# Windows entry point; all build logic lives in build.py.
$ErrorActionPreference = "Stop"
if (Get-Command py -ErrorAction SilentlyContinue) {
    & py -3 "$PSScriptRoot/build.py" @args
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    & python "$PSScriptRoot/build.py" @args
} else {
    throw "Install Python 3.10 or later and make py or python available on PATH."
}
exit $LASTEXITCODE
