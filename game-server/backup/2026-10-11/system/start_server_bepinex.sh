export DOORSTOP_ENABLED=1
export DOORSTOP_TARGET_ASSEMBLY=./BepInEx/core/BepInEx.Preloader.dll
export LD_LIBRARY_PATH="./doorstop_libs:$LD_LIBRARY_PATH"
export LD_PRELOAD="libdoorstop_x64.so:$LD_PRELOAD"
export LD_LIBRARY_PATH="./linux64:$LD_LIBRARY_PATH"
export SteamAppId=892970
echo "Starting server PRESS CTRL-C to exit"
exec ./valheim_server.x86_64 -name "Fedoheim" -port 2456 -world "Fedoheimv12" -password "<REDACTED>" -public 0 -preset hard -setkey nomap
