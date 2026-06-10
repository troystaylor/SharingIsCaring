---
name: cancel-upload
description: |
  Cancel an in-flight upload session. Use when the user asks "cancel
  the upload", "stop the file transfer", "abort the upload", "kill
  the upload session", or "I don't need that upload anymore".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Files
cowork.icon: Dismiss
---

# Cancel Upload

1. Ask the user for the session token or upload URL from the original upload response.
2. Call `get_upload_status` to confirm the upload is still in progress.
3. Confirm with the user that they want to cancel — this deletes the partial file and the stored session record.
4. Call `cancel_upload` with the session token or upload URL.
5. Confirm the upload was cancelled. Note that a 404 from Graph is treated as success (session already expired).
