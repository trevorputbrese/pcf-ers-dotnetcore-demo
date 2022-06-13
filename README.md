# Tanzu Application Service Base Demo for .NET
Base application to demonstrate TAS on .NET Core

## Credits and contributions
This is a .NET CORE port of the original Articulate demo app for Java Spring https://github.com/Pivotal-Field-Engineering/pcf-ers-demo

## Introduction
This base application is intended to demonstrate functionality of TAS:

* TAS api, target, login, and push
* TAS environment variables
* Actuator integration in TAS apps manager
* RDBMS service and application auto-configuration
* Blue green deployments
* Service discovery /w Eureka
* SSO integration
* mTLS using container certificates

The app can be deployed without any dependencies, but some demos require additional services to work.

Note: The app uses additional logic to automatically reconfigure itself based on service bindings to allow it to function without any dependencies. Many statements in startup configuration code would not normally exist for real app since lack of services is usually a fatal condition. 

## Getting Started

**Build automation script**

The app comes with build automation scripts that will help do a number of build targets (aka tasks). Each target, can be invoked by running `build.ps1` or `build.sh` followed by target name and optional parameters. Example:

```
.\build.sh Publish
```

All available targets are available by running `\build.ps1` with no args

**Included targets**

- `Publish` - compile the app targeting `linux-x64` - suitable for deployment to TAS. The manifest in root of the repo already points to folder where output of publish command is placed. A basic `cf push` demo can be done just by executing the following from root:

  ```
  .\build.ps1 Publish
  cf push
  ```

- `Pack` - packages output of `Publish` command as versioned zip file inside `/artifacts` folder

-  `Deploy` - deploys to current TAS with the following features: 

  - 3 copies of app `ers-blue`, `ers-green`, and `ers-backend`. Blue/green will be assigned public routes via default domain, while `ers-backend` will be mapped to an internal (container-to-container) domain.
  - Allow blue/green apps to talk c2c to `ers-backend` on port `8433`. `ers-backend` uses CF container identity cert for port 8433
  - If available in marketplace, create and bind to all apps the following services: mysql, eureka, sso

  Use this target to automate deployment of full demo. 



## SSO Demo

To demo SSO, you need to setup SSO plan to show up in marketplace. If you only have a single plan, it will be automatically determined, otherwise use `--sso-plan` argument to specify which plan to use. The demo configures each app to use an identity provider for the plan called `gcp` for demoing using SSO tile to integrate with Google as identity provider. You can change the name of the SSO identity provider to use via `sso-binding.json` file. 

