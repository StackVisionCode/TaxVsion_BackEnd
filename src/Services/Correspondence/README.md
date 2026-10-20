# TaxVision Correspondence

Correspondence owns tenant email threads, drafts and inbound messages matched to known customers.

## Incoming email event

`connectors.raw_message_received.v1` carries message metadata and the mailbox ownership resolved by Connectors. Correspondence persists the message and publishes `correspondence.customer_email_received.v1` with:

- `AccountId`: mailbox identifier.
- `MailboxOwnerUserId`: personal mailbox owner; `null` means office mailbox.
- Missing `AccountId`: legacy ownership is unknown and downstream delivery must fail closed.

The event never carries the message body or attachments. Communication sends the realtime refresh only to the personal owner or active office mailbox readers.
