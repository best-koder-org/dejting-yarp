# SignalR Contract: Messaging Hub

## Hub Route
`/hubs/messages`

## Connection Requirements
- JWT access token passed via `Authorization: Bearer` header.
- Client must reconnect within 30 seconds to maintain session.
- Heartbeat interval: 15 seconds; disconnect after 45 seconds of inactivity.

## Client-to-Server Methods
| Method | Payload | Description |
|--------|---------|-------------|
| `SendMessage` | `{ matchId: guid, body: string }` | Publishes text message to match participants. |
| `Typing` | `{ matchId: guid, isTyping: bool }` | Broadcasts typing state for UI presence. |
| `Acknowledge` | `{ messageId: guid }` | Confirms reception for delivery receipts when client processes message offline. |

## Server-to-Client Methods
| Method | Payload | Description |
|--------|---------|-------------|
| `MessageReceived` | `MessageDto` | Delivers new message to connected clients. |
| `MessageUpdated` | `MessageDto` | Signals read/delivery status changes. |
| `TypingChanged` | `{ matchId: guid, userId: string, isTyping: bool }` | Updates typing indicator. |
| `PresenceChanged` | `{ userId: string, state: "Online" | "Offline" }` | Broadcasts participant presence. |
| `MatchArchived` | `{ matchId: guid, reason: string }` | Notifies clients when match is blocked or archived. |

## MessageDto Schema
```json
{
  "messageId": "guid",
  "matchId": "guid",
  "senderId": "string",
  "body": "string",
  "bodyType": "Text",
  "sentAt": "2025-10-20T09:03:00Z",
  "deliveredAt": "2025-10-20T09:03:00Z",
  "readAt": null,
  "moderationFlag": null
}
```

## Error Handling
- Server responds with `HubException` containing `code` and `message`.
- Common codes: `match-not-found`, `not-authorized`, `message-too-long`, `content-blocked`.
- Clients must retry `SendMessage` after transient failures with exponential backoff (max 3 attempts).

## Security Considerations
- All payloads validated against match ownership.
- Messages scanned by moderation service before dispatch; flagged content returns `content-blocked`.
- Audit events emitted to structured logs with correlation ID `messageId`.
