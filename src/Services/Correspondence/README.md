# TaxVision Correspondence

Correspondence owns tenant email threads, drafts and inbound messages matched to known customers.

## Incoming email event

`connectors.raw_message_received.v1` carries message metadata and the mailbox ownership resolved by Connectors. Correspondence persists the message and publishes `correspondence.customer_email_received.v1` with:

- `AccountId`: mailbox identifier.
- `MailboxOwnerUserId`: personal mailbox owner; `null` means office mailbox.
- Missing `AccountId`: legacy ownership is unknown and downstream delivery must fail closed.

The event never carries the message body or attachments. Communication sends the realtime refresh only to the personal owner or active office mailbox readers.

## Unified search (`GET /correspondence/search?q=&limit=`)

Outlook-style search for the Mail module. Returns `{ emails[], files[] }` from every folder with the same access rules as the Inbox: visible mailbox and, without `customers.view_all`, only assigned customers. Open drafts are limited to the user's own unless they read the office mailbox. Clients are not returned; the CRM searches its customer directory in parallel.

- `q` is trimmed, 2–200 characters (`400 Correspondence.SearchTermTooShort`); `limit` applies per group (default 10, max 50).
- Emails: conversation subject, incoming sender and snippet, outgoing subject and recipients. Each hit carries `source` (`Thread`, `Incoming`, `Outgoing`), `folder` (`Inbox`, `Archived`, `Sent`, `Drafts`, `Trash`), `threadId`, `messageId`, `customerId` (null = not a client), `matchedField` (`Subject`, `From`, `Snippet`, `To`) and the date.
- Files: non-inline attachments of incoming emails outside the trash, and attachments of sent emails (`DraftAttachments`).
- Plain `LIKE '%term%'` bounded by tenant and limit (no full-text, D-6). Indexes: `IX_IncomingEmailAttachments_TenantId_IncomingEmailId`, `IX_DraftAttachments_TenantId_DraftId`.

`GET /correspondence/sent`, `/drafts` and `/trash` also accept `q`. `GET /correspondence/threads/{id}` returns one Inbox row (404 when the user cannot see it). Listing a message's attachments now checks the customer assignment as well as the mailbox.

Outgoing attachments live in the `DraftAttachments` child table (D-7). Migration `MoveDraftAttachmentsToTable` backfills it from `Drafts.AttachmentsJson` with `OPENJSON` and drops the JSON column, so there is a single source of truth; `Down` rebuilds the JSON from the table.

## Delivery notices (bounces in the conversation)

Correspondence consumes `postmaster.correspondence_delivery_feedback.v1` from the `taxvision-events` fanout. Postmaster owns the correlation (Message-ID + recipient), so a notice only enters Mail when it belongs to an email sent from the CRM. `RawMessageReceivedConsumer` keeps dropping senders that are not clients, so mailer-daemon messages never flood the Inbox.

- Idempotent on (sent message, recipient, outcome); unique index `IX_DeliveryNotices_TenantId_SentMessageId_Recipient_Outcome_Unique`.
- `Delivered` only updates the recipient's delivery status on the sent `Draft`.
- `Delayed` / `NotDelivered` create a `DeliveryNotice` in the email's conversation. A new email without a conversation gets one (its id is the sent draft id) and the draft is linked to it. Conversations for non-clients have `CustomerId = null` (D-1) and are visible by mailbox only (D-2).
- `NotDelivered` is unread, moves the conversation to the top and brings it back from Archived (D-3). `Delayed` is born read and does not move the conversation (D-4).
- After saving it publishes `correspondence.delivery_notice_received.v1` (`SentByUserId` = draft author) and `correspondence.thread_state_changed.v1`. Communication emits `mail.incoming` with `kind: "delivery_notice"`; Notification shows "Email to {recipient} couldn't be delivered" to the sender.
- `correspondence.thread_state_changed.v1` keeps `CustomerId` required: a conversation without a client is sent as `Guid.Empty`, which Communication forwards as `null`.

API:

- `GET /correspondence/threads/{id}/messages` interleaves notices by time as `kind: "DeliveryNotice"` with `deliveryNotice { deliveryNoticeId, draftId, recipient, outcome, category, reasonCode, statusCode, diagnostic, suppressed, canViewOriginal, isRead, occurredAtUtc, receivedAtUtc }`. Outbound messages carry `deliveryStatus` (worst of `Delivered`/`Delayed`/`NotDelivered`, null = no news) and `deliveries[]` per recipient. `GET /correspondence/sent` rows carry the same two fields.
- Inbox rows (`GET /correspondence/threads`, `/threads/{id}`) carry `customerId` (nullable) and `latestDeliveryNotice`. Unread counts include unread `NotDelivered` notices; `POST /threads/{id}/read|unread` and archive cover them.
- `GET /correspondence/delivery-notices/{id}/original` fetches the original notice live from the mailbox where it arrived (like message bodies). 404 when the conversation is not visible, 409 `DeliveryNotice.OriginalUnavailable` when the result came from a webhook.
- `reasonCode`: `invalid_address`, `domain_not_found`, `mailbox_full`, `rejected`, `too_large`, `delayed`, `other`.

## Server-side filters

Every list endpoint filters in SQL so totals and pages stay correct (the CRM never filters a loaded page).

| Endpoint | Filters |
|---|---|
| `GET /correspondence/threads` | `status`, `customerId` **or** `unlinked=true` (only conversations without a client), `unreadOnly`, `hasAttachments` (incoming or sent files of the conversation), `q`, `fromUtc`/`toUtc` on the last activity |
| `GET /correspondence/sent`, `/drafts`, `/trash` | scope (`customerId` / `unlinked` / `scope=all`), `q`, `hasAttachments`, `fromUtc`/`toUtc` on the email date (sent or last saved; trash uses the email date, not the deletion date) |
| `GET /correspondence/search` | `q`, `limit`, `customerId` **or** `unlinked`, `fromUtc`/`toUtc`; `hasAttachments` and `unreadOnly` filter emails only |

- Dates are ISO 8601 and inclusive. A value without a zone is read as UTC. A malformed date is a 400 from model binding; `fromUtc > toUtc` is `400 Correspondence.InvalidDateRange`.
- `customerId` together with `unlinked=true` is `400 Correspondence.CustomerFilterConflict`.
- Inbox rows' `hasAttachments` now also counts files of sent emails in the conversation. Draft rows gain `hasAttachments`. Search email hits gain `isUnread` (conversation: anything unread; incoming: its read state; outgoing: false).

## Resend after a bounce

`POST /correspondence/delivery-notices/{id}/resend` with `{ "recipient": "fixed@x.com" }` (omit or leave empty to resend to the bounced address) creates a new draft and returns `{ "draftId": "..." }`. The CRM opens it in the composer; nothing is sent until the user sends it.

- The draft clones the original sent email: subject, HTML and text body, and attachments (new `DraftAttachments` rows pointing to the same CloudStorage `FileId`s; Postmaster reads files by tenant at send time and never deletes them, so sharing them needs nothing from CloudStorage). If a file was deleted from CloudStorage in the meantime, the send fails like any missing attachment.
- To = only that recipient. The draft keeps the conversation (`EmailThreadId`) and the original reply context when there was one, so the resend stays in the same thread (Graph replies override the recipients, so a corrected address is respected).
- Gate: the notice's conversation must be visible (mailbox + assigned client, 404 `DeliveryNotice.NotFound` otherwise), the sending mailbox must be visible to the user (400 `Draft.AccountNotVisible`), permission `correspondence.compose`, rate limit `correspondence.g.draft_manage`. An invalid address is 400 `EmailAddress.Invalid`.
- Two clicks create two drafts (accepted; a draft is discarded at no cost).
