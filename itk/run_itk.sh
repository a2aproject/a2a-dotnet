#!/bin/bash
set -ex

# Set default log level
export ITK_LOG_LEVEL="${ITK_LOG_LEVEL:-INFO}"

# Initialize default exit code
RESULT=1

# Cleanup function to be called on exit
cleanup() {
  set +x
  echo "Cleaning up artifacts..."
  docker stop itk-service > /dev/null 2>&1 || true
  docker rm itk-service > /dev/null 2>&1 || true
  docker rmi itk_service > /dev/null 2>&1 || true
  rm -rf a2a-itk > /dev/null 2>&1 || true
  echo "Done. Final exit code: $RESULT"
}

# Register cleanup function to run on script exit
trap cleanup EXIT

# 1. Pull a2a-itk and checkout revision
: "${A2A_ITK_REVISION:?A2A_ITK_REVISION environment variable must be set}"

if [ ! -d "a2a-itk" ]; then
  git clone https://github.com/a2aproject/a2a-itk.git a2a-itk
fi
cd a2a-itk
git fetch origin
git checkout "$A2A_ITK_REVISION"

# Only pull if it's a branch (not a detached HEAD)
if git symbolic-ref -q HEAD > /dev/null; then
  git pull origin "$A2A_ITK_REVISION"
fi
cd ..

# 2. Build the ITK service Docker image from a2a-itk root
docker build -t itk_service a2a-itk

# 3. Start Docker service
# Mount a2a-dotnet as repo and itk/ as the current agent
A2A_DOTNET_ROOT=$(cd .. && pwd)
ITK_DIR=$(pwd)

# Stop existing container if any
docker rm -f itk-service || true

# Create logs directory if debug
if [ "${ITK_LOG_LEVEL^^}" = "DEBUG" ]; then
  mkdir -p "$ITK_DIR/logs"
fi

DOCKER_MOUNT_LOGS=""
if [ "${ITK_LOG_LEVEL^^}" = "DEBUG" ]; then
  DOCKER_MOUNT_LOGS="-v $ITK_DIR/logs:/app/logs"
fi

docker run -d --name itk-service \
  -v "$A2A_DOTNET_ROOT:/app/agents/repo" \
  -v "$ITK_DIR:/app/agents/repo/itk" \
  $DOCKER_MOUNT_LOGS \
  -e ITK_LOG_LEVEL="$ITK_LOG_LEVEL" \
  -e ITK_READINESS_TIMEOUT=180 \
  -p 8000:8000 \
  itk_service

# 3.1. Fix dubious ownership for git
docker exec -u root itk-service git config --system --add safe.directory /app/agents/repo
docker exec -u root itk-service git config --system --add safe.directory /app/agents/repo/itk

# 4. Verify service is up
MAX_RETRIES=30
echo "Waiting for ITK service to start on 127.0.0.1:8000..."
set +e
for i in $(seq 1 $MAX_RETRIES); do
  if curl -s http://127.0.0.1:8000/ > /dev/null; then
    echo "Service is up!"
    break
  fi
  echo "Still waiting... ($i/$MAX_RETRIES)"
  sleep 2
done

# If we reached the end of the loop without success
if ! curl -s http://127.0.0.1:8000/ > /dev/null; then
  echo "Error: ITK service failed to start on port 8000"
  docker logs itk-service
  exit 1
fi

SCENARIO_FILE="scenarios_ci.json"
if [ "${ITK_NIGHTLY_RUN^^}" = "TRUE" ]; then
  SCENARIO_FILE="scenarios_full.json"
fi

echo "ITK Service is up! Sending compatibility test request using $SCENARIO_FILE..."
RESPONSE=$(curl -s -X POST http://127.0.0.1:8000/run \
  -H "Content-Type: application/json" \
  -d "@$SCENARIO_FILE")

if [ "${ITK_NIGHTLY_RUN^^}" = "TRUE" ]; then
  echo "Nightly run detected. Saving raw results..."
  echo "$RESPONSE" > raw_results.json
  python3 a2a-itk/scripts/process_results.py \
    --history_output_file itk_dotnet.json \
    --history_url https://github.com/a2aproject/a2a-dotnet/releases/download/nightly-metrics/itk_dotnet.json
  RESULT=$?
else
  echo "--------------------------------------------------------"
  echo "ITK TEST RESULTS:"
  echo "--------------------------------------------------------"
  echo "$RESPONSE" | python3 -c "
import sys, json
try:
    data = json.load(sys.stdin)
    all_passed = data.get('all_passed', False)
    results = data.get('results', {})
    for test, details in results.items():
        if isinstance(details, dict):
            status = 'PASSED' if details.get('passed', False) else 'FAILED'
        else:
            status = 'PASSED' if details else 'FAILED'
        print(f'{test}: {status}')
    print('--------------------------------------------------------')
    print(f'OVERALL STATUS: {\"PASSED\" if all_passed else \"FAILED\"}')
    if not all_passed:
        sys.exit(1)
except Exception as e:
    print(f'Error parsing results: {e}')
    print(f'Raw response: {data if \"data\" in locals() else \"no data\"}')
    sys.exit(1)
"
  RESULT=$?
fi
set -e

if [ $RESULT -ne 0 ]; then
  echo "Tests failed. Container logs:"
  docker logs itk-service
fi
echo "--------------------------------------------------------"

# Final exit result will be captured by trap cleanup
exit $RESULT
