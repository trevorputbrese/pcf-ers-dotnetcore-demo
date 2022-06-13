version_settings(constraint=">=0.22.1")
os.putenv ("DOCKER_BUILDKIT" , "1" )
trigger="source" # use "build" to trigger deploy after compiling locally. use "source" to trigger on any file change
rid = "linux-x64"
configuration = "Debug"
appName = "ers"
appFolder = "sample"
syncFolder = "./" + appFolder + "/bin/.buildsync"
buildSyncCmd = "dotnet publish ./" + appFolder + " --configuration " + configuration + " --runtime " + rid + " --no-self-contained --output " + syncFolder
isWindows = True if os.name == "nt" else False
expected_ref = "%EXPECTED_REF%" if isWindows else "$EXPECTED_REF"


# a local build triggers a second build specific for linux into $syncFolder

if trigger == "build":
  trigger_deps = ["./" + appFolder "/bin/" + configuration]
  trigger_ignore = ["./" + appFolder "/bin/**/" + rid]
elif trigger == "source":
  trigger_deps = ["./" + appFolder ""]
  trigger_ignore = ["./" + appFolder "/bin/**", "./" + appFolder "/obj/**"]

local_resource(
  "live-update-build",
  cmd= buildSyncCmd,
  deps=trigger_deps,
  ignore=trigger_ignore
)

docker_compose("docker-compose.yaml")


custom_build(
        "ers:live-sync",
        "docker-compose stop && " + buildSyncCmd + " && docker build ./rsync-exec -t " + expected_ref,
        deps=[syncFolder, "./" + appFolder "/manifest.yaml", "./rsync-exec"],
        live_update=[
          sync(syncFolder, "/sync"),
          run("/resync.sh")
        ]
    )
