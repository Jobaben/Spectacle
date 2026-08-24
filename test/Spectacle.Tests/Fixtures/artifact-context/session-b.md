---
title: Poller architecture
status: draft
owner: platform
artifact_context:
  purpose: >
    Decide how the ingest poller consumes upstream change events, and record why, so a later
    session does not re-litigate the choice.
  decisions:
    - decision: Consume changes through a projection reader.
      reason: >
        It is the only option that can replay a window after an outage without upstream
        cooperation, which the recovery requirement makes mandatory.
    - decision: Retry a failed fetch after 30 seconds.
      reason: >
        Production telemetry over the first week showed the original interval exhausting the
        tenant rate limit during upstream incidents.
  constraints:
    - The upstream API is rate limited to 60 requests per minute per tenant.
    - Recovery must replay at least 24 hours without upstream cooperation.
  rejected:
    - alternative: Queue reader with at-least-once delivery.
      reason: Cannot replay past the queue retention window of one hour.
    - alternative: Direct polling of the changes endpoint.
      reason: Costs one request per tenant per interval and breaches the rate limit above 40 tenants.
  history: >
    Three consumption architectures were investigated. Queue reading and direct polling were
    rejected for retention and rate-limit reasons respectively; the projection reader was chosen
    for its replay window. The retry interval began at a conservative 10 seconds and was raised to
    30 after a week of production telemetry.
---

# Poller architecture

The ingest poller consumes upstream change events through a projection reader and retries a
failed fetch after 30 seconds.
