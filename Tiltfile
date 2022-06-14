version_settings(constraint=">=0.22.1")
os.putenv ("DOCKER_BUILDKIT" , "1" )
appName = os.getenv("APP_NAME")
appFolder = os.getenv("APP_DIR")

trigger="source" # use "build" to trigger deploy after compiling locally. use "source" to trigger on any file change
rid = "linux-x64"
configuration = "Debug"


syncFolder = appFolder + "/bin/.buildsync"
buildSyncCmd = "dotnet publish " + appFolder + " --configuration " + configuration + " --runtime " + rid + " --no-self-contained --output " + syncFolder + " /p:LiveSync=true"
isWindows = True if os.name == "nt" else False
expected_ref = "%EXPECTED_REF%" if isWindows else "$EXPECTED_REF"


# a local build triggers a second build specific for linux into $syncFolder

if trigger == "build":
  trigger_deps = [appFolder + "/bin/" + configuration, "manifest-tilt.yml"]
  trigger_ignore = [appFolder + "/bin/**/" + rid, appFolder + "/git.properties"]
elif trigger == "source":
  trigger_deps = [appFolder, "manifest-tilt.yml"]
  trigger_ignore = [appFolder + "/bin/**", appFolder + "/obj/**", appFolder + "/obj/", appFolder + "/git.properties"]

local_resource(
  "live-update-build",
  cmd= buildSyncCmd,
  deps=trigger_deps,
  ignore=trigger_ignore
)

docker_compose("docker-compose.yaml")

# "docker-compose stop && " + buildSyncCmd + " && docker build ./rsync-exec -t " + expected_ref,
custom_build(
        appName + ":live-sync",
        
        "docker-compose stop && docker build ./rsync-exec -t " + expected_ref,
        deps=[syncFolder, "./rsync-exec"],
        live_update=[
          fall_back_on([syncFolder + '/manifest-tilt.yml', syncFolder + '/.gitignore']),
          sync(syncFolder, "/sync"),
          run("/resync.sh")
        ],
    )
