#!/bin/bash
# Rate Limit Testing Script for YARP Gateway
# Tests that rate limiting is enforced with 429 responses

set -e

GATEWAY_URL="${GATEWAY_URL:-http://localhost:8080}"
TOKEN="${TEST_USER_TOKEN:-}"

echo "🔍 Testing YARP Rate Limiting Enforcement"
echo "Gateway: $GATEWAY_URL"
echo ""

if [ -z "$TOKEN" ]; then
    echo "⚠️  No TEST_USER_TOKEN provided. Testing without authentication (limited endpoints)."
    echo "   Set TEST_USER_TOKEN environment variable for full testing."
    echo ""
fi

AUTH_HEADER=""
if [ -n "$TOKEN" ]; then
    AUTH_HEADER="-H \"Authorization: Bearer $TOKEN\""
fi

# Test 1: Swipe Rate Limit (60/min)
echo "Test 1: Swipe Rate Limit (60 per minute)"
echo "Sending 65 swipe requests..."
RATE_LIMITED=0
for i in {1..65}; do
    STATUS=$(eval curl -s -o /dev/null -w "%{http_code}" $AUTH_HEADER -X POST "$GATEWAY_URL/api/swipes" -H "Content-Type: application/json" -d '{"targetUserId":"test","direction":"right"}')
    if [ "$STATUS" == "429" ]; then
        echo "✅ Rate limit enforced at request $i (HTTP 429)"
        RATE_LIMITED=1
        break
    fi
    if [ $((i % 10)) -eq 0 ]; then
        echo "  Sent $i requests..."
    fi
done

if [ $RATE_LIMITED -eq 0 ]; then
    echo "❌ FAILED: No 429 response received after 65 requests"
else
    echo "✅ PASSED: Swipe rate limit working"
fi
echo ""

# Test 2: Messaging Rate Limit (10/min)
echo "Test 2: Messaging Rate Limit (10 per minute)"
echo "Sending 12 message requests..."
RATE_LIMITED=0
for i in {1..12}; do
    STATUS=$(eval curl -s -o /dev/null -w "%{http_code}" $AUTH_HEADER -X POST "$GATEWAY_URL/api/messages" -H "Content-Type: application/json" -d '{"recipientId":"test","content":"test"}')
    if [ "$STATUS" == "429" ]; then
        echo "✅ Rate limit enforced at request $i (HTTP 429)"
        RATE_LIMITED=1
        break
    fi
    echo "  Request $i: $STATUS"
done

if [ $RATE_LIMITED -eq 0 ]; then
    echo "❌ FAILED: No 429 response received after 12 requests"
else
    echo "✅ PASSED: Messaging rate limit working"
fi
echo ""

# Test 3: Check 429 Response Format
echo "Test 3: Validate 429 Response Format"
RESPONSE=$(eval curl -s $AUTH_HEADER -X POST "$GATEWAY_URL/api/messages" -H "Content-Type: application/json" -d '{"recipientId":"test","content":"test"}')
if echo "$RESPONSE" | grep -q "error.*Rate limit exceeded"; then
    echo "✅ PASSED: 429 response contains proper error message"
    echo "   Response: $RESPONSE"
else
    echo "⚠️  Response format: $RESPONSE"
fi
echo ""

# Test 4: Check Rate Limit Headers
echo "Test 4: Validate Rate Limit Headers"
HEADERS=$(eval curl -s -I $AUTH_HEADER -X GET "$GATEWAY_URL/api/swipes")
if echo "$HEADERS" | grep -q "X-RateLimit"; then
    echo "✅ PASSED: X-RateLimit headers present"
    echo "$HEADERS" | grep "X-RateLimit"
else
    echo "⚠️  No X-RateLimit headers found"
fi
echo ""

echo "🏁 Rate limit testing complete"
echo ""
echo "Rate Limit Policies:"
echo "  • Messages: 10/minute"
echo "  • Photo Uploads: 20/day"
echo "  • Profile Views: 60/minute"
echo "  • Profile Updates: 10/hour"
echo "  • Match Actions: 20/minute"
echo "  • Swipes: 60/minute"
echo "  • Safety Reports: 10/day"
