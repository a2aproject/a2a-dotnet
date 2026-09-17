#!/bin/bash
# ITK harness for a2a-dotnet — a thin shim over a2a-itk's shared driver.
#
# Everything that used to live here (clone, image build, container start,
# readiness poll, POST /run, result reporting, nightly metrics) is now in
# a2a-itk/scripts/run_itk_shared.sh, which the SDK repos share.
#
# The agent is no longer pre-published here either. a2a-itk builds .NET agents
# itself now, so `dotnet publish` runs inside the container against its own
# pinned toolchain, and the NuGet cache it writes to lands in the launcher
# cache mount the shared driver already sets up.
set -e
cd "$(dirname "${BASH_SOURCE[0]}")"

ITK_SDK_NAME=dotnet
ITK_SCENARIO_SET=shared
# InstructionProto.cs is a checked-in C# translation of the proto rather than
# codegen output, so there is nothing for the copy to feed.
ITK_COPY_PROTO=0

# The agent is the bind-mounted SUT, so it never goes through the launcher's
# build phase; spawn publishes it instead, and only when publish/ is absent.
# Leaving the directory behind would make the next run exec the previous
# build — a green result for code that is no longer there. Re-publishing costs
# ~30s inside a 180s readiness window.
itk_extra_cleanup() { rm -rf publish; }

# --- bootstrap -------------------------------------------------------------
# The shared driver lives in a2a-itk, so the checkout has to exist before it
# can be sourced. CI has already placed it here via actions/checkout; locally
# we clone it from a2aproject/a2a-itk.
: "${A2A_ITK_REVISION:?A2A_ITK_REVISION environment variable must be set}"
if [ ! -d a2a-itk ]; then
  git clone https://github.com/a2aproject/a2a-itk.git a2a-itk
fi
(cd a2a-itk && git fetch origin && git checkout "$A2A_ITK_REVISION" \
  && { git symbolic-ref -q HEAD > /dev/null && git pull origin "$A2A_ITK_REVISION" || true; })

source a2a-itk/scripts/run_itk_shared.sh
