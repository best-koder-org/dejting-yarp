# API Contract: DatingApp MVP Foundation

## Authentication
**Authentication is handled by Keycloak OIDC** (external to this API).

- **Token Endpoint**: `http://localhost:8090/realms/DatingApp/protocol/openid-connect/token`
- **Authorization Endpoint**: `http://localhost:8090/realms/DatingApp/protocol/openid-connect/auth`
- **User Registration**: Managed through Keycloak Admin API or realm configuration
- **JWT Validation**: All backend services validate tokens against Keycloak Authority

**Client Configuration**:
- Client ID: `datingapp-backend` (for backend services)
- Audience: `datingapp-api`
- Grant Types: `authorization_code`, `refresh_token`

> **Note**: The legacy `/auth/*` endpoints were removed on Oct 22, 2025 (T008). Keycloak is now the sole authentication provider.

## Profile Service (`UserService`)
- **GET** `/profile/me` → returns `MemberProfileDto`
- **PUT** `/profile/me` → updates demographics, bio, preferences
- **POST** `/profile/photos` → multipart upload; returns `PhotoAssetDto`
- **PATCH** `/profile/photos/{photoId}` → update `privacyLevel`, `orderIndex`
- **DELETE** `/profile/photos/{photoId}`

### DTOs
```json
MemberProfileDto {
  "id": "string",
  "displayName": "string",
  "age": 28,
  "bio": "string",
  "interests": ["hiking", "music"],
  "preference": {
    "distanceKm": 50,
    "ageRange": { "min": 25, "max": 35 },
    "relationshipGoals": "serious"
  },
  "photos": [PhotoAssetDto],
  "onboardingStatus": "Ready"
}

PhotoAssetDto {
  "id": "guid",
  "url": "https://...",
  "blurUrl": "https://...",
  "privacyLevel": "MatchOnly",
  "moderationStatus": "Approved",
  "orderIndex": 0
}
```

## Matchmaking Service
- **GET** `/matches/candidates` → returns list of `MatchCandidateDto`
- **POST** `/matches/swipe` → `{ targetUserId, direction }`
- **GET** `/matches` → list of `MatchSummaryDto`

### DTOs
```json
MatchCandidateDto {
  "userId": "string",
  "displayName": "string",
  "age": 30,
  "compatibility": 0.81,
  "distanceKm": 6,
  "photoUrl": "https://...",
  "privacyLevel": "MatchOnly",
  "interestsOverlap": ["hiking"]
}

MatchSummaryDto {
  "matchId": "guid",
  "user": {
    "userId": "string",
    "displayName": "string",
    "photoUrl": "https://..."
  },
  "createdAt": "2025-10-20T09:00:00Z",
  "lastMessagePreview": "See you Friday!",
  "unreadCount": 2
}
```

## Messaging Service
- **GET** `/messages/{matchId}` → paginated message history
- **POST** `/messages/{matchId}` → `{ body }` sends message
- **POST** `/messages/{matchId}/read` → mark read
- **SignalR Hub** `/hubs/messages`
  - `SendMessage(matchId, body)`
  - `Typing(matchId)`
  - `MessageReceived(messageDto)`

```json
MessageDto {
  "messageId": "guid",
  "matchId": "guid",
  "senderId": "string",
  "body": "string",
  "sentAt": "2025-10-20T09:03:00Z",
  "status": "Delivered"
}
```

## Safety & Reporting
- **POST** `/safety/report` → `{ subjectType, subjectId, reason }`
- **POST** `/safety/block` → `{ targetUserId }`
- **GET** `/safety/audit` (admin only) → filterable list of reports

## YARP Routes & Security Expectations
- All endpoints enforce Keycloak JWT with audience `datingapp-api`.
- Photo downloads served through PhotoService with privacy filter; blurred URL returned when viewer is not matched.
- Rate limiting via YARP middleware: 60 swipe actions per minute, 30 reports per day.
