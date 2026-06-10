---
name: resume-failed-upload
description: |
  Resume a previously failed or partially completed large file upload.
  Use when the user asks "resume my upload", "the upload failed, try
  again", "continue the interrupted upload", "retry the file transfer",
  or "pick up where the upload left off".
license: MIT
metadata:
  author: Troy Taylor
  version: "1.0"
  pattern: mutation
cowork.category: Files
cowork.icon: ArrowSync
---

# Resume Failed Upload

1. Ask the user for the session token from the original `upload_from_url` response.
2. Call `get_upload_status` with the session token to check the current state and uploaded byte count.
3. If the status is Failed or Running (stalled), confirm with the user that the source URL is still accessible.
4. Call `resume_upload_from_url` with the session token to restart the chunked upload from where it left off.
5. Inform the user the upload is resuming in the background.
6. Offer to check progress again with `get_upload_status` or cancel with `cancel_upload`.
