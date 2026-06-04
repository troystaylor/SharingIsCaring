# local-packages

Drop the AgentControlSpecification nupkg here to enable live ACS evaluation in the container.

## Quick start

```powershell
# From this folder's parent (container-app/)
./scripts/build-acs-nupkg.ps1
```

The script runs a Linux build container that clones the upstream repo at a pinned commit, compiles the Rust native library for `linux-x64`, packs the .NET nupkg with `AgentControlSpecificationAllowIncompleteNativePack=true`, and copies the result into this folder.

## What happens next

- `AgentGovernance.Api.csproj` has a conditional `PackageReference` that picks up any `AgentControlSpecification.*.nupkg` in this folder.
- `Program.cs` is compiled with the `ACS_ENABLED` symbol, which switches the three ACS endpoints from HTTP 501 stubs to real SDK calls.
- The next `dotnet publish` / `docker build` produces a container with live ACS evaluation.

## What if I'm not on `linux-x64`?

Edit `scripts/build-acs-nupkg.ps1` and change `--platform linux/amd64` to your runtime target. The container runtime image is `mcr.microsoft.com/dotnet/aspnet:8.0`, which supports `linux-x64` and `linux-arm64`.

## Rego policies

If your manifests use `type: rego`, the SDK shells out to OPA. The Dockerfile installs OPA at `/usr/local/bin/opa` and sets `ACS_OPA_PATH` automatically when ACS is enabled — see `container-app/Dockerfile`.
