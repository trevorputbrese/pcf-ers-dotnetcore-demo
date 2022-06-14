#!/bin/bash
if [[ -z "${APP_NAME}" ]]; then
  APP_NAME=$(yq '.applications[0].name' /app/manifest.yaml)
fi
appid=$(cf app $APP_NAME --guid)

if [ $appid == *"not found"* ] || [ "$CF_PUSH_INIT" == "true" ]; then
    echo ======== Pushing app ======
    cd /syncorig

    cf push $APP_NAME -p /syncorig -f manifest-tilt.yml --var "AssemblyName=$AssemblyName"
    cd .. 
else
    echo ====== Existing app $APP_NAME is found. Using livesync instead of cf push. You can override this behavior by adding CF_PUSH_INIT=true into .env file =====
    /resync.sh
fi
exec /bin/bash -c "trap : TERM INT; sleep infinity & wait"