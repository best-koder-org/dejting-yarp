# Rate Limiting Documentation

## Overview

The YARP API Gateway enforces rate limiting on all user-facing endpoints to prevent abuse and ensure fair resource allocation. Rate limits are applied **per user** (identified by JWT `sub` claim) using sliding window algorithms.

## Rate Limit Policies

| Policy Name | Limit | Window | Applied To | Routes |
|------------|-------|--------|------------|--------|
| **MessagesPerMinute** | 10 requests | 1 minute | Messaging | `/api/messages/*` |
| **PhotoUploadsPerDay** | 20 requests | 1 day | Photo Operations | `/api/photos/*` |
| **ProfileViewsPerMinute** | 60 requests | 1 minute | Profile Browsing | `/profile/*` |
| **ProfileUpdatesPerHour** | 10 requests | 1 hour | Profile CRUD | `/api/userprofiles/*` |
| **MatchActionsPerMinute** | 20 requests | 1 minute | Matchmaking | `/api/matchmaking/*` |
| **SwipesPerMinute** | 60 requests | 1 minute | Swipe Actions | `/api/swipes/*` |
| **SafetyReportsDaily** | 10 requests | 1 day | Safety Reports | `/api/safety/*` |

## Algorithm: Sliding Window

All policies use a **sliding window** rate limiter with segments:
- **2 segments**: Fine-grained policies (messages, profile updates, match actions, safety reports)
- **4-6 segments**: Higher-frequency policies (swipes, profile views, photos)

Sliding windows provide smoother rate limiting compared to fixed windows, preventing request bursts at window boundaries.

## Response Format

### 429 Too Many Requests

When rate limit is exceeded:

```json
{
  "error": "Rate limit exceeded",
  "message": "Too many requests. Please try again later.",
  "retryAfterSeconds": 60
}
```

**Headers**:
```
HTTP/1.1 429 Too Many Requests
Retry-After: 60
X-RateLimit-Limit: N/A
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1704067200
```

## Configuration

Rate limit policies are defined in:
- **Code**: `dejting-yarp/src/dejting-yarp/Program.cs` (lines 65-142)
- **Route Mapping**: `appsettings.Development.json` / `appsettings.Demo.json`

Example route configuration:
```json
{
  "swipeRoute": {
    "ClusterId": "swipeCluster",
    "Match": {
      "Path": "/api/swipes/{**catch-all}"
    },
    "Metadata": {
      "RateLimitPolicy": "SwipesPerMinute"
    }
  }
}
```

## Partitioning Strategy

Rate limits are partitioned **per user**:
1. **JWT Token**: User ID extracted from `sub` claim
2. **Fallback**: `nameidentifier` claim
3. **Anonymous**: Remote IP address
4. **Default**: "anonymous" (shared limit for unauthenticated requests)

```csharp
var userId = context.User?.FindFirst("sub")?.Value 
             ?? context.User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
             ?? context.Connection.RemoteIpAddress?.ToString()
             ?? "anonymous";
```

## Exempt Endpoints

The following endpoints are **NOT rate-limited**:
- `/health/*` - Health checks
- WebSocket Hubs: `/messagingHub/*`, `/hubs/matchmaking/*` (SignalR uses connection-based rate limiting internally)

## Testing

Run the rate limit test suite:

```bash
# Without authentication (limited coverage)
./test-rate-limits.sh

# With authentication (full coverage)
TEST_USER_TOKEN="<jwt-token>" ./test-rate-limits.sh

# Custom gateway URL
GATEWAY_URL="http://localhost:9000" ./test-rate-limits.sh
```

The test script validates:
1. 429 responses returned when limits exceeded
2. Correct response format and error messages
3. X-RateLimit headers present
4. Retry-After header accuracy

## Production Considerations

### Adjusting Limits

To modify rate limits for production:

1. **Update Policy in Program.cs**:
   ```csharp
   options.AddSlidingWindowLimiter("MessagesPerMinute", opt =>
   {
       opt.Window = TimeSpan.FromMinutes(1);
       opt.PermitLimit = 20; // Increase from 10 to 20
       opt.QueueLimit = 0;
       opt.SegmentsPerWindow = 2;
   });
   ```

2. **Update Documentation**: Reflect changes in this file and API docs

3. **Monitor Impact**: Watch for increased 429 errors in logs/metrics

### Distributed Scenario

Current implementation uses **in-memory** rate limiting. For multi-instance YARP deployments:

1. **Use Redis Distributed Cache**:
   ```bash
   dotnet add package Microsoft.Extensions.Caching.StackExchangeRedis
   ```

2. **Configure Redis**:
   ```csharp
   builder.Services.AddStackExchangeRedisCache(options =>
   {
       options.Configuration = "localhost:6379";
   });
   ```

3. **Switch to Distributed Limiter**: Replace `AddSlidingWindowLimiter` with Redis-backed implementation

### Monitoring

Rate limit metrics are logged with correlation IDs:

```
[15:42:33 WRN] [YarpGateway] [abc123] Rate limit exceeded for user usr_123 on MessagesPerMinute policy
```

**Recommended Monitoring**:
- Count of 429 responses per policy per hour (detect abuse patterns)
- 95th percentile of requests per user (identify heavy users)
- Retry-After header distribution (optimize window sizes)

## Troubleshooting

### "Rate limit not enforced"

1. Verify middleware order in `Program.cs`:
   ```csharp
   app.UseRateLimiter(); // Must be BEFORE MapReverseProxy
   ```

2. Check route metadata in `appsettings.json`:
   ```json
   "Metadata": {
       "RateLimitPolicy": "SwipesPerMinute" // Must match policy name exactly
   }
   ```

3. Ensure `AddRateLimiter` is called in `builder.Services`

### "All users share the same limit"

- Verify JWT token contains `sub` claim
- Check authentication middleware is running before rate limiter
- Inspect logs for user ID extraction (should not be "anonymous")

### "429 responses but no Retry-After header"

- Check OnRejected handler implementation (lines 153-166 in Program.cs)
- Ensure `Lease.TryGetMetadata` succeeds
- Validate middleware chain order

## MVP Status

✅ **Complete**: Rate limiting fully implemented and enforced across all routes
- 7 policies defined with appropriate limits
- All routes mapped to policies (except health/websockets)
- 429 responses with proper error messages
- X-RateLimit headers included
- Test suite available
