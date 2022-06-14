#!/bin/bash
if [[ -z "${APP_NAME}" ]]; then
  APP_NAME=$(yq '.applications[0].name' /app/manifest.yaml)
fi
appid=$(cf app $APP_NAME --guid)
sshendpoint=$(cf curl /v2/info | jq -r .app_ssh_endpoint | awk -F: '{print $1}')
if [ $appid == *"not found"* ]; then
    cd /app
    cf push $APP_NAME 
fi


s="/usr/bin/rsync --exclude 'watchexec' -azP -e 'sshpass -p $(cf ssh-code) ssh -oStrictHostKeyChecking=no -p 2222 -l cf:$appid/0' /sync/ vcap@$sshendpoint:/home/vcap/app"
eval "$s"