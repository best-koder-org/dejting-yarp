#!/bin/bash
# OpenAPI Spec Generation Script
# Generates OpenAPI 3.0 JSON specifications from running services
# Usage: Start services with ./dev-start.sh first, then run this script

set -e

echo "================================================="
echo "OpenAPI Specification Generator"
echo "================================================="
echo ""

# Service endpoints for Swagger JSON
declare -A SERVICES=(
    ["UserService"]="http://localhost:5001/swagger/v1/swagger.json"
    ["MatchmakingService"]="http://localhost:5002/swagger/v1/swagger.json"
    ["photo-service"]="http://localhost:5004/swagger/v1/swagger.json"
    ["swipe-service"]="http://localhost:5005/swagger/v1/swagger.json"
    ["messaging-service"]="http://localhost:5006/swagger/v1/swagger.json"
    ["safety-service"]="http://localhost:5007/swagger/v1/swagger.json"
)

OUTPUT_DIR="$(dirname "$0")"
SUCCESS_COUNT=0
FAIL_COUNT=0

for service in "${!SERVICES[@]}"; do
    url="${SERVICES[$service]}"
    output_file="${OUTPUT_DIR}/${service}.openapi.json"
    
    echo -n "Fetching $service spec... "
    
    if curl -s -f -o "$output_file" "$url"; then
        # Validate JSON
        if jq empty "$output_file" 2>/dev/null; then
            file_size=$(du -h "$output_file" | cut -f1)
            echo "✅ ($file_size)"
            ((SUCCESS_COUNT++))
        else
            echo "❌ (Invalid JSON)"
            rm -f "$output_file"
            ((FAIL_COUNT++))
        fi
    else
        echo "❌ (Service not responding)"
        ((FAIL_COUNT++))
    fi
done

echo ""
echo "================================================="
echo "Summary: $SUCCESS_COUNT succeeded, $FAIL_COUNT failed"
echo "================================================="
echo ""

if [ $SUCCESS_COUNT -eq 6 ]; then
    echo "✨ All OpenAPI specs generated successfully!"
    echo "Location: $OUTPUT_DIR"
    exit 0
else
    echo "⚠️  Some services failed. Make sure all services are running."
    echo "   Start services with: ./dev-start.sh"
    exit 1
fi
